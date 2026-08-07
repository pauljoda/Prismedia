using Microsoft.EntityFrameworkCore;
using Prismedia.Contracts.Acquisition;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>Entity-scoped acquisition queries and projections.</summary>
public sealed partial class EfAcquisitionStore {
    public async Task<bool> AnyOpenForEntityAsync(Guid entityId, CancellationToken cancellationToken) =>
        await db.Acquisitions.AsNoTracking().AnyAsync(
            row => row.EntityId == entityId
                && row.Status != AcquisitionStatus.Imported
                && row.Status != AcquisitionStatus.Cancelled,
            cancellationToken);

    /// <inheritdoc />
    public async Task<bool> AnyOpenForEntityAsync(
        Guid entityId,
        BookRendition? bookRendition,
        CancellationToken cancellationToken) =>
        await db.Acquisitions.AsNoTracking().AnyAsync(
            row => row.EntityId == entityId
                && row.BookRendition == bookRendition
                && row.Status != AcquisitionStatus.Imported
                && row.Status != AcquisitionStatus.Cancelled,
            cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlySet<Guid>> FilterOpenEntityIdsAsync(
        IReadOnlyCollection<Guid> entityIds,
        BookRendition? bookRendition,
        CancellationToken cancellationToken) {
        var ids = entityIds.Distinct().ToArray();
        if (ids.Length == 0) {
            return new HashSet<Guid>();
        }

        return await db.Acquisitions.AsNoTracking()
            .Where(row => row.EntityId != null
                && ids.Contains(row.EntityId.Value)
                && row.BookRendition == bookRendition
                && row.Status != AcquisitionStatus.Imported
                && row.Status != AcquisitionStatus.Cancelled)
            .Select(row => row.EntityId!.Value)
            .ToHashSetAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> ListIdsForEntityAsync(
        Guid entityId,
        CancellationToken cancellationToken) {
        var result = await db.Acquisitions.AsNoTracking()
            .Where(row => row.EntityId == entityId)
            .OrderByDescending(row => row.CreatedAt)
            .Select(row => row.Id)
            .ToListAsync(cancellationToken);
        var visited = result.ToHashSet();
        IReadOnlyList<Guid> frontier = result.ToArray();
        while (frontier.Count > 0) {
            var parentIds = frontier.ToArray();
            var children = await db.Acquisitions.AsNoTracking()
                .Where(row => row.UpgradeOfAcquisitionId != null
                    && parentIds.Contains(row.UpgradeOfAcquisitionId.Value))
                .OrderBy(row => row.CreatedAt)
                .Select(row => row.Id)
                .ToArrayAsync(cancellationToken);
            var next = new List<Guid>(children.Length);
            foreach (var childId in children) {
                if (!visited.Add(childId)) {
                    continue;
                }

                result.Add(childId);
                next.Add(childId);
            }
            frontier = next;
        }
        return result;
    }

    public async Task<AcquisitionDetail?> GetLatestForEntityAsync(Guid entityId, CancellationToken cancellationToken) {
        var rows = await db.Acquisitions
            .AsNoTracking()
            .Where(row => row.EntityId == entityId)
            .OrderByDescending(row => row.CreatedAt)
            .ThenByDescending(row => row.Id)
            .ToArrayAsync(cancellationToken);
        var latest = (await ExcludeFulfilledPassiveAcquisitionsAsync(rows, cancellationToken)).FirstOrDefault();
        return latest is not null ? await GetAsync(latest.Id, cancellationToken) : null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AcquisitionDetail>> ListForEntityAsync(
        Guid entityId,
        CancellationToken cancellationToken) {
        var visibleRows = await ListVisibleRowsForEntityAsync(entityId, cancellationToken);
        if (visibleRows.Length == 0) {
            return [];
        }

        var ids = visibleRows.Select(row => row.Id).ToArray();
        var candidates = await db.ReleaseCandidates.AsNoTracking()
            .Where(candidate => ids.Contains(candidate.AcquisitionId))
            .OrderByDescending(candidate => candidate.Accepted)
            .ThenByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Id)
            .ToArrayAsync(cancellationToken);
        var candidatesByAcquisition = candidates.ToLookup(candidate => candidate.AcquisitionId);
        var progress = await LatestProgressAsync(ids, cancellationToken);
        return visibleRows.Select(row => new AcquisitionDetail(
            ToSummary(row, progress.GetValueOrDefault(row.Id)),
            candidatesByAcquisition[row.Id].Select(ToView).ToArray())).ToArray();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AcquisitionSummary>> ListSummariesForEntityAsync(
        Guid entityId,
        CancellationToken cancellationToken) {
        var visibleRows = await ListVisibleRowsForEntityAsync(entityId, cancellationToken);
        if (visibleRows.Length == 0) {
            return [];
        }

        var progress = await LatestProgressAsync(
            visibleRows.Select(row => row.Id).ToArray(),
            cancellationToken);
        return visibleRows
            .Select(row => ToSummary(row, progress.GetValueOrDefault(row.Id)))
            .ToArray();
    }

    private async Task<AcquisitionRow[]> ListVisibleRowsForEntityAsync(
        Guid entityId,
        CancellationToken cancellationToken) {
        var rows = await db.Acquisitions.AsNoTracking()
            .Where(row => row.EntityId == entityId)
            .OrderByDescending(row => row.CreatedAt)
            .ThenByDescending(row => row.Id)
            .ToArrayAsync(cancellationToken);
        return rows.Length == 0
            ? []
            : await ExcludeFulfilledPassiveAcquisitionsAsync(rows, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, AcquisitionSummary>> ListLatestSummariesForEntityIdsAsync(
        IReadOnlyCollection<Guid> entityIds,
        CancellationToken cancellationToken) {
        var requestedIds = entityIds.Distinct().ToArray();
        if (requestedIds.Length == 0) {
            return new Dictionary<Guid, AcquisitionSummary>();
        }

        var rows = await db.Acquisitions.AsNoTracking()
            .Where(row => row.EntityId != null && requestedIds.Contains(row.EntityId.Value))
            .OrderByDescending(row => row.CreatedAt)
            .ToArrayAsync(cancellationToken);
        var visibleRows = await ExcludeFulfilledPassiveAcquisitionsAsync(rows, cancellationToken);
        var latest = visibleRows
            .GroupBy(row => row.EntityId!.Value)
            .Select(group => group.First())
            .ToArray();
        var progress = await LatestProgressAsync(latest.Select(row => row.Id).ToArray(), cancellationToken);
        return latest.ToDictionary(
            row => row.EntityId!.Value,
            row => ToSummary(row, progress.GetValueOrDefault(row.Id)));
    }
}
