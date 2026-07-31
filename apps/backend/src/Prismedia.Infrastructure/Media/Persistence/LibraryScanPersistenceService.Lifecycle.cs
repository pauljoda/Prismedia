using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Entities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Media.Persistence;

public sealed partial class LibraryScanPersistenceService {
    /// <summary>
    /// Commits scanner mutations only while the shared Entity lifecycle lease owns every existing Entity
    /// touched by the unit of work. New scan-discovered Entities resolve to their nearest persisted ancestor;
    /// an entirely new root needs no lease because destructive ownership cannot reference it yet.
    /// </summary>
    private async Task SaveChangesWithLifecycleAsync(CancellationToken cancellationToken) {
        _structurePlacement.Reset();
        _db.ChangeTracker.DetectChanges();
        await ValidatePendingStructuralPlacementsAsync(cancellationToken);

        var addedEntities = _db.ChangeTracker.Entries<EntityRow>()
            .Where(entry => entry.State == EntityState.Added)
            .ToDictionary(entry => entry.Entity.Id, entry => entry.Entity);
        var lifecycleEntityIds = new HashSet<Guid>();

        Guid? ResolvePersistedAnchor(Guid entityId) {
            var visited = new HashSet<Guid>();
            while (addedEntities.TryGetValue(entityId, out var addedEntity)) {
                if (!visited.Add(entityId) || addedEntity.ParentEntityId is not { } parentEntityId) {
                    return null;
                }
                entityId = parentEntityId;
            }
            return entityId;
        }

        void AddPersistedAnchor(Guid? entityId) {
            if (entityId is { } id && ResolvePersistedAnchor(id) is { } persistedId) {
                lifecycleEntityIds.Add(persistedId);
            }
        }

        foreach (var entry in _db.ChangeTracker.Entries()
                     .Where(entry => entry.State is EntityState.Added
                         or EntityState.Modified
                         or EntityState.Deleted)) {
            if (entry.Entity is EntityRow entity) {
                if (entry.State == EntityState.Added) {
                    AddPersistedAnchor(entity.ParentEntityId);
                    continue;
                }

                AddPersistedAnchor(entity.Id);
                var parentProperty = entry.Property(nameof(EntityRow.ParentEntityId));
                if (parentProperty.IsModified) {
                    AddPersistedAnchor(parentProperty.OriginalValue is Guid originalParentId
                        ? originalParentId
                        : null);
                    AddPersistedAnchor(parentProperty.CurrentValue is Guid currentParentId
                        ? currentParentId
                        : null);
                }
                continue;
            }

            var entityIdProperty = entry.Metadata.FindProperty(nameof(EntityFileRow.EntityId));
            if (entityIdProperty is null) {
                continue;
            }
            var value = entry.Property(entityIdProperty.Name).CurrentValue
                ?? entry.Property(entityIdProperty.Name).OriginalValue;
            if (value is Guid entityId) {
                AddPersistedAnchor(entityId);
            }
        }

        if (lifecycleEntityIds.Count == 0) {
            await _db.SaveChangesAsync(cancellationToken);
            return;
        }

        var executed = await _lifecycle.ExecuteManyAsync(
            lifecycleEntityIds,
            token => _db.SaveChangesAsync(token),
            cancellationToken);
        if (!executed) {
            throw new EntityLifecycleMutationConflictException(lifecycleEntityIds.Order().First());
        }
    }

    /// <summary>
    /// Scanner ingress is intentionally centralized here rather than in a DbContext interceptor:
    /// every scan upsert commits through this method, while unrelated EF callers retain their own
    /// explicit boundary. The scoped validator memoizes each existing parent/ancestor lookup.
    /// </summary>
    private async Task ValidatePendingStructuralPlacementsAsync(CancellationToken cancellationToken) {
        foreach (var entry in _db.ChangeTracker.Entries<EntityRow>()) {
            if (entry.State is not (EntityState.Added or EntityState.Modified)) {
                continue;
            }

            var parentProperty = entry.Property(nameof(EntityRow.ParentEntityId));
            if (entry.State != EntityState.Added && !parentProperty.IsModified) {
                continue;
            }

            await _structurePlacement.ValidateAsync(
                EntityKindRegistry.Require(entry.Entity.KindCode),
                entry.Entity.Id,
                entry.Entity.ParentEntityId,
                entry.State == EntityState.Added
                    ? null
                    : parentProperty.OriginalValue is Guid originalParentId ? originalParentId : null,
                null,
                cancellationToken);
        }
    }
}
