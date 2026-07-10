using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Entities;

/// <summary>
/// Projects acquisition availability through the structural Entity hierarchy. Every root receives the
/// latest directly-linked acquisition for each Entity in its subtree, plus every active and the latest
/// terminal upgrade descendant of those acquisitions. This keeps TV, music, books, and future Entity
/// trees on one availability definition for both server filtering and thumbnail projection.
/// </summary>
internal sealed class EfEntityAcquisitionStatusProjection(PrismediaDbContext db) {
    private static readonly AcquisitionStatus[] ActiveUpgradeStatuses = [
        AcquisitionStatus.Pending,
        AcquisitionStatus.Searching,
        AcquisitionStatus.AwaitingSelection,
        AcquisitionStatus.Queued,
        AcquisitionStatus.Downloading,
        AcquisitionStatus.Downloaded,
        AcquisitionStatus.Importing,
        AcquisitionStatus.Stopping,
    ];

    private static readonly IReadOnlySet<AcquisitionStatus> ActiveUpgradeStatusSet =
        ActiveUpgradeStatuses.ToHashSet();
    private static readonly string[] ActiveUpgradeStatusCodes =
        ActiveUpgradeStatuses.Select(status => status.ToCode()).ToArray();

    /// <summary>Applies subtree status membership before count, ordering, and paging.</summary>
    public async Task<IQueryable<Persistence.Entities.EntityRow>> ApplyFilterAsync(
        IQueryable<Persistence.Entities.EntityRow> query,
        AcquisitionStatus? status,
        CancellationToken cancellationToken) {
        if (status is not { } requestedStatus) {
            return query;
        }

        if (db.Database.IsNpgsql()) {
            var requestedCode = requestedStatus.ToCode();
            var matchingRootIds = PostgreSqlStatusRows([])
                .Where(row => row.StatusCode == requestedCode)
                .Select(row => row.RootId)
                .Distinct();
            return query.Where(entity => matchingRootIds.Contains(entity.Id));
        }

        var rootIds = await db.Entities.AsNoTracking()
            .Select(entity => entity.Id)
            .ToArrayAsync(cancellationToken);
        var snapshots = await ResolveNonRelationalAsync(rootIds, cancellationToken);
        var matchingIds = snapshots
            .Where(pair => pair.Value.Statuses.Contains(requestedStatus))
            .Select(pair => pair.Key)
            .ToArray();
        return query.Where(entity => matchingIds.Contains(entity.Id));
    }

    /// <summary>Resolves acquisition status sets for a bounded thumbnail batch.</summary>
    public async Task<IReadOnlyDictionary<Guid, EntityAcquisitionStatusSnapshot>> ResolveAsync(
        IReadOnlyCollection<Guid> rootIds,
        CancellationToken cancellationToken) {
        if (rootIds.Count == 0) {
            return new Dictionary<Guid, EntityAcquisitionStatusSnapshot>();
        }

        var distinctIds = rootIds.Distinct().ToArray();
        if (!db.Database.IsNpgsql()) {
            return await ResolveNonRelationalAsync(distinctIds, cancellationToken);
        }

        var rows = await PostgreSqlStatusRows(distinctIds).ToArrayAsync(cancellationToken);
        return BuildSnapshots(distinctIds, rows);
    }

    private IQueryable<EntityAcquisitionStatusSqlRow> PostgreSqlStatusRows(Guid[] rootIds) =>
        db.Database.SqlQuery<EntityAcquisitionStatusSqlRow>($"""
            WITH RECURSIVE requested_roots(root_id, entity_id, path) AS (
                SELECT entity.id, entity.id, ARRAY[entity.id]
                FROM entities AS entity
                WHERE cardinality({rootIds}) = 0 OR entity.id = ANY ({rootIds})
            ),
            entity_tree(root_id, entity_id, path) AS (
                SELECT root_id, entity_id, path
                FROM requested_roots
                UNION ALL
                SELECT tree.root_id, child.id, tree.path || child.id
                FROM entity_tree AS tree
                INNER JOIN entities AS child ON child.parent_entity_id = tree.entity_id
                WHERE NOT child.id = ANY (tree.path)
            ),
            direct_ranked AS (
                SELECT
                    tree.root_id,
                    tree.entity_id,
                    acquisition.id AS acquisition_id,
                    acquisition.status,
                    acquisition.created_at,
                    ROW_NUMBER() OVER (
                        PARTITION BY tree.root_id, tree.entity_id
                        ORDER BY acquisition.created_at DESC, acquisition.id DESC
                    ) AS direct_rank
                FROM entity_tree AS tree
                INNER JOIN acquisitions AS acquisition ON acquisition.entity_id = tree.entity_id
            ),
            direct_latest AS (
                SELECT root_id, entity_id, acquisition_id, status, created_at
                FROM direct_ranked
                WHERE direct_rank = 1
            ),
            upgrade_tree(root_id, anchor_acquisition_id, acquisition_id, status, created_at, path) AS (
                SELECT
                    direct.root_id,
                    direct.acquisition_id,
                    child.id,
                    child.status,
                    child.created_at,
                    ARRAY[direct.acquisition_id, child.id]
                FROM direct_latest AS direct
                INNER JOIN acquisitions AS child
                    ON child.upgrade_of_acquisition_id = direct.acquisition_id
                UNION ALL
                SELECT
                    tree.root_id,
                    tree.anchor_acquisition_id,
                    child.id,
                    child.status,
                    child.created_at,
                    tree.path || child.id
                FROM upgrade_tree AS tree
                INNER JOIN acquisitions AS child
                    ON child.upgrade_of_acquisition_id = tree.acquisition_id
                WHERE NOT child.id = ANY (tree.path)
            ),
            upgrade_ranked AS (
                SELECT
                    root_id,
                    anchor_acquisition_id,
                    acquisition_id,
                    status,
                    ROW_NUMBER() OVER (
                        PARTITION BY root_id, anchor_acquisition_id
                        ORDER BY created_at DESC, acquisition_id DESC
                    ) AS latest_rank
                FROM upgrade_tree
            ),
            selected_statuses AS (
                SELECT root_id, status, entity_id = root_id AS is_root_direct
                FROM direct_latest
                UNION ALL
                SELECT root_id, status, FALSE
                FROM upgrade_ranked
                WHERE status = ANY ({ActiveUpgradeStatusCodes}) OR latest_rank = 1
            )
            SELECT
                root_id AS "RootId",
                status AS "StatusCode",
                BOOL_OR(is_root_direct) AS "IsRootDirect"
            FROM selected_statuses
            GROUP BY root_id, status
            """);

    private async Task<IReadOnlyDictionary<Guid, EntityAcquisitionStatusSnapshot>> ResolveNonRelationalAsync(
        IReadOnlyCollection<Guid> rootIds,
        CancellationToken cancellationToken) {
        var entities = await db.Entities.AsNoTracking()
            .Select(entity => new { entity.Id, entity.ParentEntityId })
            .ToArrayAsync(cancellationToken);
        var childrenByParent = entities
            .Where(entity => entity.ParentEntityId != null)
            .GroupBy(entity => entity.ParentEntityId!.Value)
            .ToDictionary(group => group.Key, group => group.Select(entity => entity.Id).ToArray());
        var acquisitions = await db.Acquisitions.AsNoTracking()
            .Select(acquisition => new AcquisitionNode(
                acquisition.Id,
                acquisition.EntityId,
                acquisition.UpgradeOfAcquisitionId,
                acquisition.Status,
                acquisition.CreatedAt))
            .ToArrayAsync(cancellationToken);
        var upgradesByParent = acquisitions
            .Where(acquisition => acquisition.UpgradeOfAcquisitionId != null)
            .GroupBy(acquisition => acquisition.UpgradeOfAcquisitionId!.Value)
            .ToDictionary(group => group.Key, group => group.ToArray());

        return rootIds.Distinct().ToDictionary(
            rootId => rootId,
            rootId => BuildSnapshot(rootId, childrenByParent, acquisitions, upgradesByParent));
    }

    private static EntityAcquisitionStatusSnapshot BuildSnapshot(
        Guid rootId,
        IReadOnlyDictionary<Guid, Guid[]> childrenByParent,
        IReadOnlyList<AcquisitionNode> acquisitions,
        IReadOnlyDictionary<Guid, AcquisitionNode[]> upgradesByParent) {
        var subtreeIds = StructuralSubtree(rootId, childrenByParent);
        var directLatest = acquisitions
            .Where(acquisition => acquisition.EntityId is { } entityId && subtreeIds.Contains(entityId))
            .GroupBy(acquisition => acquisition.EntityId!.Value)
            .Select(group => group
                .OrderByDescending(acquisition => acquisition.CreatedAt)
                .ThenByDescending(acquisition => acquisition.Id)
                .First())
            .ToArray();
        var statuses = directLatest.Select(acquisition => acquisition.Status).ToHashSet();

        foreach (var direct in directLatest) {
            var descendants = UpgradeDescendants(direct.Id, upgradesByParent);
            statuses.UnionWith(descendants
                .Where(acquisition => ActiveUpgradeStatusSet.Contains(acquisition.Status))
                .Select(acquisition => acquisition.Status));
            var latest = descendants
                .OrderByDescending(acquisition => acquisition.CreatedAt)
                .ThenByDescending(acquisition => acquisition.Id)
                .FirstOrDefault();
            if (latest is not null) {
                statuses.Add(latest.Status);
            }
        }

        var latestRootDirect = directLatest
            .FirstOrDefault(acquisition => acquisition.EntityId == rootId)
            ?.Status;
        return new EntityAcquisitionStatusSnapshot(
            latestRootDirect,
            statuses.OrderBy(status => status).ToArray());
    }

    private static HashSet<Guid> StructuralSubtree(
        Guid rootId,
        IReadOnlyDictionary<Guid, Guid[]> childrenByParent) {
        var subtree = new HashSet<Guid>();
        var pending = new Stack<Guid>();
        pending.Push(rootId);
        while (pending.TryPop(out var entityId)) {
            if (!subtree.Add(entityId) || !childrenByParent.TryGetValue(entityId, out var children)) {
                continue;
            }

            foreach (var childId in children) {
                pending.Push(childId);
            }
        }

        return subtree;
    }

    private static IReadOnlyList<AcquisitionNode> UpgradeDescendants(
        Guid acquisitionId,
        IReadOnlyDictionary<Guid, AcquisitionNode[]> upgradesByParent) {
        var descendants = new List<AcquisitionNode>();
        var visited = new HashSet<Guid> { acquisitionId };
        var pending = new Stack<Guid>();
        pending.Push(acquisitionId);
        while (pending.TryPop(out var parentId)) {
            if (!upgradesByParent.TryGetValue(parentId, out var children)) {
                continue;
            }

            foreach (var child in children) {
                if (!visited.Add(child.Id)) {
                    continue;
                }

                descendants.Add(child);
                pending.Push(child.Id);
            }
        }

        return descendants;
    }

    private static IReadOnlyDictionary<Guid, EntityAcquisitionStatusSnapshot> BuildSnapshots(
        IReadOnlyCollection<Guid> rootIds,
        IReadOnlyList<EntityAcquisitionStatusSqlRow> rows) {
        var decoded = rows
            .Select(row => new {
                Row = row,
                Parsed = row.StatusCode.TryDecodeAs<AcquisitionStatus>(out var status)
                    ? status
                    : (AcquisitionStatus?)null,
            })
            .Where(item => item.Parsed != null)
            .ToArray();

        return rootIds.Distinct().ToDictionary(
            rootId => rootId,
            rootId => {
                var rootRows = decoded.Where(item => item.Row.RootId == rootId).ToArray();
                return new EntityAcquisitionStatusSnapshot(
                    rootRows.FirstOrDefault(item => item.Row.IsRootDirect)?.Parsed,
                    rootRows.Select(item => item.Parsed!.Value).Distinct().OrderBy(status => status).ToArray());
            });
    }

    private sealed record AcquisitionNode(
        Guid Id,
        Guid? EntityId,
        Guid? UpgradeOfAcquisitionId,
        AcquisitionStatus Status,
        DateTimeOffset CreatedAt);
}

/// <summary>Availability state projected for one Entity root.</summary>
internal sealed record EntityAcquisitionStatusSnapshot(
    AcquisitionStatus? LatestDirectStatus,
    IReadOnlyList<AcquisitionStatus> Statuses);

/// <summary>Unmapped row returned by the PostgreSQL recursive acquisition projection.</summary>
internal sealed class EntityAcquisitionStatusSqlRow {
    public Guid RootId { get; init; }
    public string StatusCode { get; init; } = string.Empty;
    public bool IsRootDirect { get; init; }
}
