using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Entities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Entities;

/// <summary>
/// EF Core implementation of the canonical entity external-identity store. It reconciles tracked
/// rows with persisted rows so callers see their current unit of work without forcing a commit.
/// </summary>
/// <param name="db">Scoped Prismedia unit of work.</param>
/// <param name="timeProvider">Clock used for deterministic identity-row timestamps.</param>
public sealed class EfEntityExternalIdentityStore(
    PrismediaDbContext db,
    TimeProvider timeProvider) : IEntityExternalIdentityStore {
    /// <inheritdoc />
    public async Task<IReadOnlyList<EntityExternalId>> ListAsync(
        Guid entityId,
        CancellationToken cancellationToken) {
        var persisted = await db.EntityExternalIds.AsNoTracking()
            .Where(row => row.EntityId == entityId)
            .ToArrayAsync(cancellationToken);
        return MergeTrackedRows(persisted, row => row.EntityId == entityId)
            .OrderBy(row => row.CreatedAt)
            .ThenBy(row => row.Id)
            .Select(ToDomain)
            .ToArray();
    }

    /// <inheritdoc />
    public async Task<ExternalIdentityResolution> ResolveAsync(
        EntityKind kind,
        IReadOnlyCollection<ExternalIdentity> identities,
        Guid? parentEntityId,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(identities);
        var requested = NormalizeRequestedIdentities(identities);
        if (requested.Count == 0) {
            return new ExternalIdentityResolution([]);
        }

        var identityRows = await LoadResolutionIdentityRowsAsync(requested, cancellationToken);
        if (identityRows.Count == 0) {
            return new ExternalIdentityResolution([]);
        }

        var entities = await LoadResolutionEntitiesAsync(
            identityRows.Select(row => row.EntityId).Distinct().ToArray(),
            cancellationToken);
        var kindCode = kind.ToCode();
        var matches = identityRows
            .Where(row => entities.TryGetValue(row.EntityId, out var entity) &&
                entity.KindCode == kindCode &&
                (parentEntityId is null || entity.ParentEntityId == parentEntityId))
            .GroupBy(row => row.EntityId)
            .Select(group => new ExternalIdentityMatch(
                group.Key,
                group.Select(row => row.Identity)
                    .Distinct()
                    .OrderBy(identity => identity.Namespace, StringComparer.Ordinal)
                    .ThenBy(identity => identity.Value, StringComparer.Ordinal)
                    .ToArray()))
            .OrderBy(match => match.EntityId)
            .ToArray();
        return new ExternalIdentityResolution(matches);
    }

    /// <inheritdoc />
    public async Task WriteAsync(
        Guid entityId,
        IReadOnlyCollection<EntityExternalId> identities,
        ExternalIdentityWriteMode mode,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(identities);
        if (!Enum.IsDefined(mode)) {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown external identity write mode.");
        }

        var incoming = PrepareIncoming(identities);
        var existing = await LoadRowsForWriteAsync(entityId, cancellationToken);
        var existingByNamespace = IndexExistingRows(entityId, existing);
        var now = timeProvider.GetUtcNow();

        if (mode == ExternalIdentityWriteMode.ReplaceAll) {
            RemoveMissingRows(existingByNamespace, incoming);
        }

        foreach (var (identityNamespace, association) in incoming) {
            if (!existingByNamespace.TryGetValue(identityNamespace, out var row)) {
                AddRow(entityId, association, now);
                continue;
            }

            CanonicalizeExistingRow(row, now);
            if (mode != ExternalIdentityWriteMode.AddMissing) {
                UpdateRow(row, association, mode, now);
            }
        }
    }

    private async Task<IReadOnlyList<IdentityRowMatch>> LoadResolutionIdentityRowsAsync(
        IReadOnlySet<ExternalIdentity> requested,
        CancellationToken cancellationToken) {
        var namespaces = requested.Select(identity => identity.Namespace).Distinct().ToArray();
        var values = requested.Select(identity => identity.Value).Distinct().ToArray();
        var persisted = await db.EntityExternalIds.AsNoTracking()
            .Where(row =>
                namespaces.Contains(row.Provider.Trim().ToLower()) &&
                values.Contains(row.Value.Trim()))
            .ToArrayAsync(cancellationToken);
        var persistedIds = persisted.Select(row => row.Id).ToHashSet();
        return MergeTrackedRows(
                persisted,
                row => persistedIds.Contains(row.Id) || CouldMatch(row, namespaces, values))
            .Select(row => new IdentityRowMatch(row.EntityId, new ExternalIdentity(row.Provider, row.Value)))
            .Where(row => requested.Contains(row.Identity))
            .ToArray();
    }

    private static bool CouldMatch(
        EntityExternalIdRow row,
        IReadOnlyCollection<string> namespaces,
        IReadOnlyCollection<string> values) =>
        namespaces.Contains(row.Provider.Trim().ToLowerInvariant()) &&
        values.Contains(row.Value.Trim());

    private async Task<IReadOnlyDictionary<Guid, EntityRow>> LoadResolutionEntitiesAsync(
        IReadOnlyCollection<Guid> entityIds,
        CancellationToken cancellationToken) {
        var persisted = await db.Entities.AsNoTracking()
            .Where(row => entityIds.Contains(row.Id))
            .ToArrayAsync(cancellationToken);
        var tracked = db.ChangeTracker.Entries<EntityRow>()
            .Where(entry => entityIds.Contains(entry.Entity.Id))
            .ToArray();
        var trackedIds = tracked.Select(entry => entry.Entity.Id).ToHashSet();
        return persisted
            .Where(row => !trackedIds.Contains(row.Id))
            .Concat(tracked
                .Where(entry => entry.State is not EntityState.Deleted and not EntityState.Detached)
                .Select(entry => entry.Entity))
            .ToDictionary(row => row.Id);
    }

    private IReadOnlyList<EntityExternalIdRow> MergeTrackedRows(
        IReadOnlyCollection<EntityExternalIdRow> persisted,
        Func<EntityExternalIdRow, bool> includeTracked) {
        var tracked = db.ChangeTracker.Entries<EntityExternalIdRow>()
            .Where(entry => includeTracked(entry.Entity))
            .ToArray();
        var trackedIds = tracked.Select(entry => entry.Entity.Id).ToHashSet();
        return persisted
            .Where(row => !trackedIds.Contains(row.Id))
            .Concat(tracked
                .Where(entry => entry.State is not EntityState.Deleted and not EntityState.Detached)
                .Select(entry => entry.Entity))
            .ToArray();
    }

    private async Task<IReadOnlyList<EntityExternalIdRow>> LoadRowsForWriteAsync(
        Guid entityId,
        CancellationToken cancellationToken) {
        var persisted = await db.EntityExternalIds
            .Where(row => row.EntityId == entityId)
            .ToArrayAsync(cancellationToken);
        return persisted
            .Concat(db.ChangeTracker.Entries<EntityExternalIdRow>()
                .Where(entry => entry.Entity.EntityId == entityId)
                .Select(entry => entry.Entity))
            .DistinctBy(row => row.Id)
            .Where(row => db.Entry(row).State is not EntityState.Deleted and not EntityState.Detached)
            .ToArray();
    }

    private static IReadOnlySet<ExternalIdentity> NormalizeRequestedIdentities(
        IReadOnlyCollection<ExternalIdentity> identities) {
        if (identities.Any(identity => identity is null)) {
            throw new ArgumentException("External identity sets cannot contain null values.", nameof(identities));
        }

        return identities.ToHashSet();
    }

    private static IReadOnlyDictionary<string, EntityExternalId> PrepareIncoming(
        IReadOnlyCollection<EntityExternalId> identities) {
        if (identities.Any(identity => identity is null)) {
            throw new ArgumentException("External identity writes cannot contain null values.", nameof(identities));
        }

        var incoming = new Dictionary<string, EntityExternalId>(StringComparer.Ordinal);
        foreach (var group in identities.GroupBy(identity => identity.Identity.Namespace, StringComparer.Ordinal)) {
            var values = group.Select(identity => identity.Identity.Value).Distinct(StringComparer.Ordinal).ToArray();
            if (values.Length > 1) {
                throw new ArgumentException(
                    $"External identity namespace '{group.Key}' has conflicting values.",
                    nameof(identities));
            }

            incoming.Add(group.Key, group.FirstOrDefault(identity => !string.IsNullOrWhiteSpace(identity.Url)) ?? group.First());
        }

        return incoming;
    }

    private static Dictionary<string, EntityExternalIdRow> IndexExistingRows(
        Guid entityId,
        IReadOnlyCollection<EntityExternalIdRow> rows) {
        var indexed = new Dictionary<string, EntityExternalIdRow>(StringComparer.Ordinal);
        foreach (var row in rows) {
            var identityNamespace = new ExternalIdentity(row.Provider, row.Value).Namespace;
            if (!indexed.TryAdd(identityNamespace, row)) {
                throw new InvalidOperationException(
                    $"Entity '{entityId}' has more than one external identity in namespace '{identityNamespace}'.");
            }
        }

        return indexed;
    }

    private void RemoveMissingRows(
        IReadOnlyDictionary<string, EntityExternalIdRow> existing,
        IReadOnlyDictionary<string, EntityExternalId> incoming) {
        foreach (var (identityNamespace, row) in existing) {
            if (!incoming.ContainsKey(identityNamespace)) {
                db.EntityExternalIds.Remove(row);
            }
        }
    }

    private void AddRow(Guid entityId, EntityExternalId association, DateTimeOffset now) =>
        db.EntityExternalIds.Add(new EntityExternalIdRow {
            Id = Guid.NewGuid(),
            EntityId = entityId,
            Provider = association.Identity.Namespace,
            Value = association.Identity.Value,
            Url = association.Url,
            CreatedAt = now,
            UpdatedAt = now
        });

    private static void CanonicalizeExistingRow(EntityExternalIdRow row, DateTimeOffset now) {
        var canonical = new ExternalIdentity(row.Provider, row.Value);
        if (row.Provider == canonical.Namespace && row.Value == canonical.Value) {
            return;
        }

        row.Provider = canonical.Namespace;
        row.Value = canonical.Value;
        row.UpdatedAt = now;
    }

    private static void UpdateRow(
        EntityExternalIdRow row,
        EntityExternalId association,
        ExternalIdentityWriteMode mode,
        DateTimeOffset now) {
        var url = mode == ExternalIdentityWriteMode.ReplaceAll
            ? association.Url
            : association.Url ?? row.Url;
        if (row.Provider == association.Identity.Namespace &&
            row.Value == association.Identity.Value &&
            row.Url == url) {
            return;
        }

        row.Provider = association.Identity.Namespace;
        row.Value = association.Identity.Value;
        row.Url = url;
        row.UpdatedAt = now;
    }

    private static EntityExternalId ToDomain(EntityExternalIdRow row) =>
        new(new ExternalIdentity(row.Provider, row.Value), row.Url);

    private sealed record IdentityRowMatch(Guid EntityId, ExternalIdentity Identity);
}
