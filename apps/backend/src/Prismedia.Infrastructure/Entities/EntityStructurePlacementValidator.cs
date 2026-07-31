using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Entities;

/// <summary>
/// Validates definition-owned structural placement rules at EF writer and materialization
/// boundaries. The validator is scoped with a <see cref="PrismediaDbContext"/> so parent rows
/// already tracked by a scan or repository save never cause a database round trip.
/// </summary>
internal sealed class EntityStructurePlacementValidator(PrismediaDbContext db) {
    private readonly Dictionary<Guid, ParentSnapshot?> _parents = [];

    /// <summary>Clears snapshots after an operation boundary or EF tracking reset.</summary>
    public void Reset() => _parents.Clear();

    /// <summary>Validates a child kind against an already-known direct parent kind.</summary>
    public static void ValidatePlacement(EntityKind childKind, EntityKind? parentKind) {
        var policy = EntityKindRegistry.Describe(childKind).StructurePolicy;
        if (parentKind is null) {
            if (!policy.AllowsRoot) {
                throw new InvalidOperationException(
                    $"Entity kind '{childKind.ToCode()}' requires a structural parent.");
            }

            return;
        }

        if (!policy.AllowsParent(parentKind.Value)) {
            throw new InvalidOperationException(
                $"Entity kind '{childKind.ToCode()}' cannot have structural parent kind '{parentKind.Value.ToCode()}'.");
        }
    }

    /// <summary>
    /// Validates an EF structural assignment, rejecting a self-parent immediately and ancestry cycles
    /// when the parent changes.
    /// </summary>
    public async Task ValidateAsync(
        EntityKind childKind,
        Guid childId,
        Guid? parentId,
        Guid? currentParentId,
        EntityKind? knownParentKind,
        CancellationToken cancellationToken) {
        var parent = parentId is null
            ? null
            : knownParentKind is { } known
                ? new ParentSnapshot(known, null)
                : await ResolveParentAsync(parentId.Value, cancellationToken)
                    ?? throw new InvalidOperationException(
                        $"Entity '{childId}' references missing structural parent '{parentId}'.");

        ValidatePlacement(childKind, parent?.Kind);
        if (parentId == childId) {
            throw new InvalidOperationException($"Entity '{childId}' cannot be its own structural parent.");
        }

        if (parentId is { } changedParentId && changedParentId != currentParentId) {
            await ThrowIfCycleAsync(childId, changedParentId, cancellationToken);
        }
    }

    /// <summary>Resolves and memoizes a parent kind for callers that validate a uniform child batch.</summary>
    public async Task<EntityKind> RequireParentKindAsync(Guid parentId, CancellationToken cancellationToken) =>
        (await ResolveParentAsync(parentId, cancellationToken)
            ?? throw new InvalidOperationException($"Structural parent '{parentId}' was not found.")).Kind;

    private async Task ThrowIfCycleAsync(Guid childId, Guid parentId, CancellationToken cancellationToken) {
        var visited = new HashSet<Guid>();
        Guid? currentId = parentId;
        while (currentId is { } id) {
            if (!visited.Add(id)) {
                throw new InvalidOperationException($"Structural ancestry for parent '{parentId}' already contains a cycle.");
            }
            if (id == childId) {
                throw new InvalidOperationException(
                    $"Entity '{childId}' cannot be reparented under '{parentId}' because that would create a structural cycle.");
            }

            currentId = (await ResolveParentAsync(id, cancellationToken)
                ?? throw new InvalidOperationException($"Structural ancestor '{id}' was not found.")).ParentId;
        }
    }

    private async Task<ParentSnapshot?> ResolveParentAsync(Guid id, CancellationToken cancellationToken) {
        var tracked = db.ChangeTracker.Entries<EntityRow>()
            .FirstOrDefault(entry => entry.Entity.Id == id);
        if (tracked is not null) {
            return Cache(id, tracked.State == EntityState.Deleted ? null : FromRow(tracked.Entity));
        }

        if (_parents.TryGetValue(id, out var cached)) {
            return cached;
        }

        var row = await db.Entities.AsNoTracking()
            .Where(entity => entity.Id == id)
            .Select(entity => new { entity.KindCode, entity.ParentEntityId })
            .SingleOrDefaultAsync(cancellationToken);
        return Cache(id, row is null ? null : new ParentSnapshot(EntityKindRegistry.Require(row.KindCode), row.ParentEntityId));
    }

    private ParentSnapshot? Cache(Guid id, ParentSnapshot? value) {
        _parents[id] = value;
        return value;
    }

    private static ParentSnapshot FromRow(EntityRow row) =>
        new(EntityKindRegistry.Require(row.KindCode), row.ParentEntityId);

    private sealed record ParentSnapshot(EntityKind Kind, Guid? ParentId);
}
