using Prismedia.Application.Acquisition;
using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>Elects independent recovery attempts under the stable Entity monitor lease.</summary>
public sealed class EfHeldAcquisitionAlternativeService(PrismediaDbContext db, IAcquisitionStore acquisitions,
    IMonitorStore monitors) : IHeldAcquisitionAlternativeService {
    /// <inheritdoc />
    public async Task RecordSearchAsync(AcquisitionSearchInput input, AcquisitionSearchOutcome outcome, CancellationToken cancellationToken) {
        if (input.RecoveryOfAcquisitionId is null || input.EntityId is not { } entityId) return;
        await monitors.ExecuteIfActiveEntityMutationAsync(entityId, async token => {
            var search = await db.Acquisitions.AsNoTracking().SingleOrDefaultAsync(row => row.Id == input.Id
                && row.RecoveryOfAcquisitionId == input.RecoveryOfAcquisitionId && row.Status == AcquisitionStatus.AwaitingSelection, token);
            var monitor = await db.Monitors.SingleOrDefaultAsync(row => row.EntityId == entityId
                && row.AcquisitionId == input.Id && row.Status == MonitorStatus.Active, token);
            if (search is null || monitor is null || monitor.LastSearchedAt >= search.UpdatedAt) return;
            if (outcome.Candidates.Any(candidate => candidate.Accepted)) monitor.BarrenSearches = 0;
            else if (outcome.Errors.Count == 0) monitor.BarrenSearches = Math.Min(10, monitor.BarrenSearches + 1);
            monitor.LastSearchedAt = search.UpdatedAt;
            monitor.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(token);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Guid?> CreateAsync(DueMonitor monitor, CancellationToken cancellationToken) {
        if (monitor.EntityId is not { } entityId || monitor.AcquisitionId is not { } heldId) return null;
        Guid? alternativeId = null;
        await monitors.ExecuteIfActiveEntityMutationAsync(entityId, async token => {
            var current = await db.Monitors.SingleOrDefaultAsync(row => row.Id == monitor.MonitorId
                && row.Status == MonitorStatus.Active && row.AcquisitionId == heldId && row.EntityId == entityId
                && row.UpgradeChildAcquisitionId == null, token);
            var source = await db.Acquisitions.AsNoTracking().SingleOrDefaultAsync(row => row.Id == heldId
                && row.EntityId == entityId && row.Status == AcquisitionStatus.ManualImportRequired
                && !row.ImportManualReview && row.ImportClaimJobId == null && row.ImportCheckpointJson == null
                && row.UpgradeOfAcquisitionId == null && row.FinalSourcePath == null
                && (row.Kind == EntityKind.VideoEpisode || row.Kind == EntityKind.VideoSeason || row.Kind == EntityKind.Movie), token);
            if (current is null || source is null
                || await acquisitions.GetSelectedReleaseAsync(heldId, token) is not { ManualPick: false }) return;
            var transfer = await db.DownloadTransfers.AsNoTracking().Where(row => row.AcquisitionId == heldId)
                .OrderByDescending(row => row.CreatedAt).FirstOrDefaultAsync(token);
            if (transfer is not { Progress: >= 1, ContentPath: not null }) return;
            var id = await acquisitions.CloneForRetryAsync(heldId, token);
            if (id is null) return;
            var child = await db.Acquisitions.SingleAsync(row => row.Id == id, token);
            child.RecoveryOfAcquisitionId = heldId;
            child.ProfileId = current.ProfileId ?? source.ProfileId;
            child.TargetLibraryRootId = current.TargetLibraryRootId ?? source.TargetLibraryRootId;
            child.Status = AcquisitionStatus.AwaitingSelection;
            child.StatusMessage = "Looking for a better match while preserving the previous held download.";
            current.AcquisitionId = child.Id;
            current.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(token);
            alternativeId = child.Id;
        }, cancellationToken);
        return alternativeId;
    }
}
