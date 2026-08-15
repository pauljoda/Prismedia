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
        AcquisitionStatus.WaitingForDownloadClient,
        AcquisitionStatus.Downloaded,
        AcquisitionStatus.Importing,
        AcquisitionStatus.Stopping,
    ];

    private static readonly IReadOnlySet<AcquisitionStatus> ActiveUpgradeStatusSet =
        ActiveUpgradeStatuses.ToHashSet();
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
            var persistedMatchingIds = db.EntityAvailability
                .Where(availability => availability.AcquisitionStatusCodes.Contains(requestedCode))
                .Select(availability => availability.EntityId);
            return query.Where(entity => persistedMatchingIds.Contains(entity.Id));
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

        var rows = await db.EntityAvailability.AsNoTracking()
            .Where(availability => distinctIds.Contains(availability.EntityId))
            .ToArrayAsync(cancellationToken);
        var byId = rows.ToDictionary(row => row.EntityId);
        return distinctIds.ToDictionary(
            id => id,
            id => byId.TryGetValue(id, out var row)
                ? new EntityAcquisitionStatusSnapshot(
                    DecodeStatus(row.LatestAcquisitionStatusCode),
                    row.AcquisitionStatusCodes
                        .Select(DecodeStatus)
                        .Where(status => status is not null)
                        .Select(status => status!.Value)
                        .Distinct()
                        .OrderBy(status => status)
                        .ToArray())
                : new EntityAcquisitionStatusSnapshot(null, []));
    }

    private static AcquisitionStatus? DecodeStatus(string? code) =>
        code is not null && code.TryDecodeAs<AcquisitionStatus>(out var status)
            ? status
            : null;

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
