using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Entities;
using Prismedia.Application.Security;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Entities;

/// <summary>
/// EF implementation of the progress-topology port. It deliberately reads definition-declared
/// topology and persisted parent links instead of maintaining a second kind registry here.
/// </summary>
public sealed class EfEntityProgressTopologyResolver(
    PrismediaDbContext db,
    ICurrentUserContext? currentUser = null) : IEntityProgressTopologyResolver {
    private const int MaximumStructuralDepth = 32;

    // Progress resolution repeatedly touches the same rows (owner, cursor, ancestors) across
    // its entry points within one request; a per-instance memo turns those repeats into
    // dictionary hits. The resolver is scoped per request, so the snapshot cannot go stale
    // within its lifetime.
    private readonly Dictionary<Guid, EntityRow?> _rowsById = [];

    /// <inheritdoc />
    public async Task<ProgressOwnerResolution?> ResolveOwnerAsync(
        Guid requestedEntityId,
        CancellationToken cancellationToken) {
        var requested = await FindRowAsync(requestedEntityId, cancellationToken);
        if (requested is null) {
            return null;
        }

        var topology = EntityKindRegistry.Describe(EntityKindRegistry.Require(requested.KindCode)).ProgressTopology;
        return topology switch {
            EntityProgressTopology.NoneTopology => null,
            EntityProgressTopology.DirectTopology => new(requested.Id),
            EntityProgressTopology.OrderedContainerTopology => new(requested.Id),
            EntityProgressTopology.OrderedRollupTopology => new(requested.Id),
            EntityProgressTopology.WorkTopology work => await ResolveWorkOwnerAsync(requested, work, cancellationToken),
            _ => null
        };
    }

    /// <inheritdoc />
    public async Task<ProgressCursorResolution?> ResolveCursorAsync(
        Guid ownerId,
        Guid cursorId,
        CancellationToken cancellationToken) {
        var owner = await FindRowAsync(ownerId, cancellationToken);
        var cursor = await FindRowAsync(cursorId, cancellationToken);
        if (owner is null || cursor is null) {
            return null;
        }

        var topology = EntityKindRegistry.Describe(EntityKindRegistry.Require(owner.KindCode)).ProgressTopology;
        return topology switch {
            EntityProgressTopology.DirectTopology when owner.Id == cursor.Id => new(cursor.Id, cursor.Id),
            EntityProgressTopology.OrderedRollupTopology when owner.Id == cursor.Id => new(cursor.Id, cursor.Id),
            EntityProgressTopology.WorkTopology work => await ResolveWorkCursorAsync(owner, cursor, work, cancellationToken),
            EntityProgressTopology.OrderedContainerTopology container =>
                await ResolveContainerCursorAsync(owner, cursor, container, cancellationToken),
            _ => null
        };
    }

    /// <inheritdoc />
    public async Task<ProgressWorkPosition?> ResolveWorkPositionAsync(
        Guid ownerId,
        Guid cursorId,
        int index,
        int total,
        CancellationToken cancellationToken) {
        var cursor = await ResolveCursorAsync(ownerId, cursorId, cancellationToken);
        if (cursor is null) {
            return null;
        }

        var owner = await FindRowAsync(ownerId, cancellationToken);
        var current = await FindRowAsync(cursorId, cancellationToken);
        if (owner is null || current is null ||
            EntityKindRegistry.Describe(EntityKindRegistry.Require(owner.KindCode)).ProgressTopology is not EntityProgressTopology.WorkTopology) {
            return null;
        }
        // A work root may carry a direct CFI-like cursor. Only structural descendant cursors are
        // translated into an absolute work item position.
        if (current.Id == owner.Id) {
            return null;
        }

        var rows = await LoadDescendantsAsync(owner.Id, cancellationToken);
        var children = rows
            .Where(row => row.ParentEntityId is not null)
            .GroupBy(row => row.ParentEntityId!.Value)
            .ToDictionary(group => group.Key, group => Order(group));

        // A cached page stat lets a work flatten chapter-local page progress without page
        // Entities. This remains capability-driven: kinds without persisted page counts take
        // the generic structural path below.
        var sameKindLeaves = DepthFirst(rows, owner.Id)
            .Where(row => row.KindCode == current.KindCode && !children.ContainsKey(row.Id))
            .ToArray();
        if (sameKindLeaves.Length > 0) {
            var leafIds = sameKindLeaves.Select(row => row.Id).ToArray();
            var pageCounts = await db.EntityStats.AsNoTracking()
                .Where(row => leafIds.Contains(row.EntityId) && row.Code == EntityStatCodes.Pages)
                .ToDictionaryAsync(row => row.EntityId, row => row.Value, cancellationToken);
            var currentPageCount = pageCounts.GetValueOrDefault(current.Id);
            if (currentPageCount > 0) {
                var absoluteIndex = 0;
                foreach (var leaf in sameKindLeaves) {
                    if (leaf.Id == current.Id) {
                        absoluteIndex += Math.Clamp(index, 0, currentPageCount - 1);
                        break;
                    }
                    absoluteIndex += Math.Max(0, pageCounts.GetValueOrDefault(leaf.Id));
                }
                var absoluteTotal = sameKindLeaves.Sum(
                    leaf => Math.Max(0, pageCounts.GetValueOrDefault(leaf.Id)));
                if (absoluteTotal > 0) {
                    return new ProgressWorkPosition(current.Id, absoluteIndex, absoluteTotal);
                }
            }
        }

        // A local cursor belongs to the nearest structural container which has children of a
        // single declared kind. This lets a definition-defined work flatten pages, tracks, or
        // future work items without naming any concrete media kind here.
        var containerId = children.ContainsKey(current.Id) ? current.Id : current.ParentEntityId;
        if (containerId is not { } resolvedContainerId ||
            !children.TryGetValue(resolvedContainerId, out var localItems) ||
            localItems.Count == 0 ||
            resolvedContainerId == owner.Id) {
            return null;
        }

        var itemKindCode = localItems[0].KindCode;
        if (localItems.Any(item => item.KindCode != itemKindCode)) {
            return null;
        }
        // Work position is the ordered leaf sequence. Intermediate structural nodes (for
        // example a grouping volume) remain valid cursors but must not masquerade as a page/item
        // position merely because their children share a kind.
        if (localItems.Any(item => children.ContainsKey(item.Id))) {
            return null;
        }

        var orderedItems = DepthFirst(rows, owner.Id)
            .Where(row => row.KindCode == itemKindCode)
            .ToArray();
        if (orderedItems.Length == 0) {
            return null;
        }

        var offset = Array.FindIndex(orderedItems, item => item.Id == localItems[0].Id);
        if (offset < 0) {
            return null;
        }

        var localTotal = localItems.Count > 0 ? localItems.Count : Math.Max(0, total);
        var localIndex = localTotal == 0 ? 0 : Math.Clamp(index, 0, localTotal - 1);
        return new ProgressWorkPosition(
            resolvedContainerId,
            Math.Min(orderedItems.Length - 1, offset + localIndex),
            orderedItems.Length);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OrderedProgressScope>> ResolveOrderedScopesAsync(
        Guid itemId,
        CancellationToken cancellationToken) {
        var item = await FindRowAsync(itemId, cancellationToken);
        if (item is null ||
            EntityKindRegistry.Describe(EntityKindRegistry.Require(item.KindCode)).ProgressTopology is not EntityProgressTopology.OrderedRollupTopology rollup ||
            rollup.ItemKind != EntityKindRegistry.Require(item.KindCode)) {
            return [];
        }

        var ancestors = await LoadAncestorsAsync(item, cancellationToken);
        var scopes = new List<OrderedProgressScope>(rollup.ContainerKinds.Count);
        foreach (var containerKind in rollup.ContainerKinds) {
            var owner = ancestors.FirstOrDefault(row => EntityKindRegistry.Require(row.KindCode) == containerKind);
            if (owner is null ||
                EntityKindRegistry.Describe(EntityKindRegistry.Require(owner.KindCode)).ProgressTopology is not EntityProgressTopology.OrderedContainerTopology container ||
                container.ItemKind != rollup.ItemKind) {
                continue;
            }

            var ordered = await LoadOrderedScopeItemsAsync(owner, container, item.Id, cancellationToken);
            var position = ordered.ToList().FindIndex(candidate => candidate == item.Id);
            if (position >= 0) {
                var completedCount = currentUser?.UserId is { } userId && userId != Guid.Empty
                    ? await db.UserEntityStates.AsNoTracking().CountAsync(
                        state => state.UserId == userId &&
                                 ordered.Contains(state.EntityId) &&
                                 state.CompletedAt != null,
                        cancellationToken)
                    : 0;
                scopes.Add(new OrderedProgressScope(
                    owner.Id,
                    item.Id,
                    position,
                    ordered.Count,
                    position + 1 < ordered.Count ? ordered[position + 1] : null,
                    completedCount));
            }
        }

        return scopes;
    }

    private async Task<ProgressOwnerResolution?> ResolveWorkOwnerAsync(
        EntityRow requested,
        EntityProgressTopology.WorkTopology work,
        CancellationToken cancellationToken) {
        var lineage = await LoadAncestorsAsync(requested, cancellationToken);
        var owner = lineage.FirstOrDefault(row => EntityKindRegistry.Require(row.KindCode) == work.WorkKind);
        if (owner is not null) {
            return new ProgressOwnerResolution(owner.Id);
        }

        return work.FallsBackToDirect
            ? new ProgressOwnerResolution(requested.Id)
            : null;
    }

    private async Task<ProgressCursorResolution?> ResolveWorkCursorAsync(
        EntityRow owner,
        EntityRow cursor,
        EntityProgressTopology.WorkTopology work,
        CancellationToken cancellationToken) {
        var lineage = await LoadAncestorsAsync(cursor, cancellationToken);
        if (owner.Id == cursor.Id && work.FallsBackToDirect &&
            !lineage.Any(row => EntityKindRegistry.Require(row.KindCode) == work.WorkKind)) {
            return new ProgressCursorResolution(cursor.Id, cursor.Id);
        }

        if (EntityKindRegistry.Require(owner.KindCode) != work.WorkKind) {
            return null;
        }

        var nearestWorkOwner = lineage.FirstOrDefault(row =>
            EntityKindRegistry.Require(row.KindCode) == work.WorkKind);
        if (nearestWorkOwner?.Id != owner.Id ||
            EntityKindRegistry.Describe(EntityKindRegistry.Require(cursor.KindCode)).ProgressTopology is not EntityProgressTopology.WorkTopology cursorWork ||
            cursorWork.WorkKind != work.WorkKind) {
            return null;
        }

        var normalized = cursor.KindCode == owner.KindCode ? owner.Id : cursor.Id;
        return new ProgressCursorResolution(cursor.Id, normalized);
    }

    private async Task<ProgressCursorResolution?> ResolveContainerCursorAsync(
        EntityRow owner,
        EntityRow cursor,
        EntityProgressTopology.OrderedContainerTopology topology,
        CancellationToken cancellationToken) {
        var ordered = await LoadOrderedScopeItemsAsync(owner, topology, cursor.Id, cancellationToken);
        return ordered.Contains(cursor.Id)
            ? new ProgressCursorResolution(cursor.Id, cursor.Id)
            : null;
    }

    private async Task<IReadOnlyList<Guid>> LoadOrderedScopeItemsAsync(
        EntityRow owner,
        EntityProgressTopology.OrderedContainerTopology topology,
        Guid? focusedItemId,
        CancellationToken cancellationToken) {
        var children = await db.Entities.AsNoTracking()
            .Where(row => row.ParentEntityId == owner.Id)
            .OrderBy(row => row.SortOrder)
            .ThenBy(row => row.CreatedAt)
            .ThenBy(row => row.Id)
            .ToArrayAsync(cancellationToken);
        var nested = children
            .Where(row => EntityKindRegistry.Describe(EntityKindRegistry.Require(row.KindCode)).ProgressTopology is EntityProgressTopology.OrderedContainerTopology nestedTopology &&
                          nestedTopology.ItemKind == topology.ItemKind)
            .ToArray();

        var focusedItem = focusedItemId is { } id
            ? children.FirstOrDefault(row => row.Id == id)
            : null;
        if (nested.Length == 0 || focusedItem is not null) {
            return children
                .Where(row => EntityKindRegistry.Require(row.KindCode) == topology.ItemKind)
                .Select(row => row.Id)
                .ToArray();
        }

        var nestedIds = nested.Select(row => row.Id).ToArray();
        if (focusedItemId is { } focusedId) {
            var focused = await FindRowAsync(focusedId, cancellationToken);
            if (focused?.ParentEntityId == owner.Id) {
                return children
                    .Where(row => EntityKindRegistry.Require(row.KindCode) == topology.ItemKind)
                    .Select(row => row.Id)
                    .ToArray();
            }
        }
        var items = await db.Entities.AsNoTracking()
            .Where(row => row.ParentEntityId != null && nestedIds.Contains(row.ParentEntityId.Value))
            .Where(row => row.KindCode == topology.ItemKind.ToCode())
            .OrderBy(row => row.SortOrder)
            .ThenBy(row => row.CreatedAt)
            .ThenBy(row => row.Id)
            .Select(row => new { ParentId = row.ParentEntityId!.Value, row.Id })
            .ToArrayAsync(cancellationToken);
        var byParent = items.GroupBy(row => row.ParentId).ToDictionary(group => group.Key, group => group.Select(row => row.Id));
        return nested.SelectMany(row => byParent.GetValueOrDefault(row.Id) ?? []).ToArray();
    }

    private async Task<EntityRow?> FindRowAsync(Guid id, CancellationToken cancellationToken) {
        if (_rowsById.TryGetValue(id, out var cached)) {
            return cached;
        }

        var row = await db.Entities.AsNoTracking().FirstOrDefaultAsync(row => row.Id == id, cancellationToken);
        _rowsById[id] = row;
        return row;
    }

    /// <summary>Batch-loads rows for the given ids, feeding the per-request memo.</summary>
    private async Task<IReadOnlyDictionary<Guid, EntityRow>> LoadRowsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken) {
        var missing = ids.Where(id => !_rowsById.ContainsKey(id)).Distinct().ToArray();
        if (missing.Length > 0) {
            var loaded = await db.Entities.AsNoTracking()
                .Where(row => missing.Contains(row.Id))
                .ToArrayAsync(cancellationToken);
            foreach (var row in loaded) {
                _rowsById[row.Id] = row;
            }
            foreach (var id in missing) {
                _rowsById.TryAdd(id, null);
            }
        }

        return ids
            .Select(id => _rowsById.GetValueOrDefault(id))
            .Where(row => row is not null)
            .Select(row => row!)
            .DistinctBy(row => row.Id)
            .ToDictionary(row => row.Id);
    }

    private async Task<IReadOnlyList<EntityRow>> LoadAncestorsAsync(EntityRow start, CancellationToken cancellationToken) {
        // One recursive CTE resolves the whole chain instead of one round-trip per level.
        if (db.Database.IsNpgsql()) {
            var lineageIds = await db.Database
                .SqlQueryRaw<Guid>(
                    """
                    WITH RECURSIVE lineage(id, parent_entity_id, depth, path) AS (
                        SELECT entity.id, entity.parent_entity_id, 0, ARRAY[entity.id]
                        FROM entities AS entity
                        WHERE entity.id = {0}
                        UNION ALL
                        SELECT parent.id, parent.parent_entity_id, lineage.depth + 1, lineage.path || parent.id
                        FROM lineage
                        INNER JOIN entities AS parent ON parent.id = lineage.parent_entity_id
                        WHERE NOT parent.id = ANY (lineage.path) AND lineage.depth < {1}
                    )
                    SELECT id AS "Value" FROM lineage ORDER BY depth
                    """,
                    start.Id,
                    MaximumStructuralDepth)
                .ToArrayAsync(cancellationToken);
            var rowById = await LoadRowsAsync(lineageIds, cancellationToken);
            return lineageIds
                .Select(id => rowById.GetValueOrDefault(id))
                .Where(row => row is not null)
                .Select(row => row!)
                .ToArray();
        }

        var rows = new List<EntityRow> { start };
        var seen = new HashSet<Guid> { start.Id };
        var parentId = start.ParentEntityId;
        for (var depth = 0; parentId is { } id && depth < MaximumStructuralDepth && seen.Add(id); depth++) {
            var parent = await FindRowAsync(id, cancellationToken);
            if (parent is null) {
                break;
            }

            rows.Add(parent);
            parentId = parent.ParentEntityId;
        }

        return rows;
    }

    private async Task<IReadOnlyList<EntityRow>> LoadDescendantsAsync(Guid ownerId, CancellationToken cancellationToken) {
        // One recursive CTE resolves the subtree ids, then one batch load hydrates the rows,
        // instead of one round-trip per structural depth.
        if (db.Database.IsNpgsql()) {
            var descendantIds = await db.Database
                .SqlQueryRaw<Guid>(
                    """
                    WITH RECURSIVE subtree(id, depth, path) AS (
                        SELECT entity.id, 0, ARRAY[entity.id]
                        FROM entities AS entity
                        WHERE entity.id = {0}
                        UNION ALL
                        SELECT child.id, subtree.depth + 1, subtree.path || child.id
                        FROM subtree
                        INNER JOIN entities AS child ON child.parent_entity_id = subtree.id
                        WHERE NOT child.id = ANY (subtree.path) AND subtree.depth < {1}
                    )
                    SELECT id AS "Value" FROM subtree WHERE depth > 0
                    """,
                    ownerId,
                    MaximumStructuralDepth)
                .ToArrayAsync(cancellationToken);
            var rowById = await LoadRowsAsync(descendantIds, cancellationToken);
            return descendantIds
                .Select(id => rowById.GetValueOrDefault(id))
                .Where(row => row is not null)
                .Select(row => row!)
                .ToArray();
        }

        var all = new List<EntityRow>();
        var parents = new[] { ownerId };
        var seen = new HashSet<Guid> { ownerId };
        for (var depth = 0; parents.Length > 0 && depth < MaximumStructuralDepth; depth++) {
            var loaded = await db.Entities.AsNoTracking()
                .Where(row => row.ParentEntityId != null && parents.Contains(row.ParentEntityId.Value))
                .ToArrayAsync(cancellationToken);
            var children = loaded.Where(row => seen.Add(row.Id)).ToArray();
            all.AddRange(children);
            parents = children.Select(row => row.Id).ToArray();
        }

        return all;
    }

    private static IReadOnlyList<EntityRow> Order(IEnumerable<EntityRow> rows) =>
        rows.OrderBy(row => row.SortOrder).ThenBy(row => row.CreatedAt).ThenBy(row => row.Id).ToArray();

    private static IEnumerable<EntityRow> DepthFirst(IReadOnlyList<EntityRow> rows, Guid rootId) {
        var children = rows.Where(row => row.ParentEntityId is not null)
            .GroupBy(row => row.ParentEntityId!.Value)
            .ToDictionary(group => group.Key, group => Order(group));
        return Visit(rootId, children, new HashSet<Guid> { rootId });
    }

    private static IEnumerable<EntityRow> Visit(
        Guid parentId,
        IReadOnlyDictionary<Guid, IReadOnlyList<EntityRow>> children,
        ISet<Guid> visited) {
        if (!children.TryGetValue(parentId, out var descendants)) {
            yield break;
        }

        foreach (var child in descendants) {
            if (!visited.Add(child.Id)) {
                continue;
            }

            yield return child;
            foreach (var nested in Visit(child.Id, children, visited)) {
                yield return nested;
            }
        }
    }
}
