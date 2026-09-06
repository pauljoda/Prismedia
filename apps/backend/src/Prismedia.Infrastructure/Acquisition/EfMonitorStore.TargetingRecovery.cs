using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Acquisition;

public sealed partial class EfMonitorStore {
    /// <summary>Repairs missing targeting on unselected fileless requests from their explicit direct monitor.</summary>
    private async Task RestoreUnselectedTargetingAsync(Guid? entityId, Guid? monitorId, CancellationToken cancellationToken) {
        var eligibleRequests = UntargetedUnselectedRequests();
        var candidates = await db.Monitors.AsNoTracking()
            .Where(monitor => monitor.Status == MonitorStatus.Active && monitor.EntityId != null
                && monitor.AcquisitionId != null && monitor.UpgradeChildAcquisitionId == null
                && (monitor.ProfileId != null || monitor.TargetLibraryRootId != null)
                && (entityId == null || monitor.EntityId == entityId)
                && (monitorId == null || monitor.Id == monitorId)
                && eligibleRequests.Any(acquisition => acquisition.Id == monitor.AcquisitionId))
            .Select(monitor => new { monitor.Id, EntityId = monitor.EntityId!.Value }).ToArrayAsync(cancellationToken);
        foreach (var candidate in candidates) {
            await lifecycle.ExecuteAsync(candidate.EntityId, async token => {
                var monitor = await db.Monitors.FirstOrDefaultAsync(row => row.Id == candidate.Id, token);
                if (monitor is null) return;
                await db.Entry(monitor).ReloadAsync(token);
                if (monitor.Status != MonitorStatus.Active || monitor.EntityId != candidate.EntityId
                    || monitor.AcquisitionId is null || monitor.UpgradeChildAcquisitionId != null
                    || monitor.ProfileId is null && monitor.TargetLibraryRootId is null) return;
                var profileKind = AcquisitionProfileKinds.For(monitor.Kind);
                if (monitor.ProfileId is { } profileId && !await db.BookAcquisitionProfiles.AnyAsync(
                        profile => profile.Id == profileId && profile.Kind == profileKind, token)) return;
                if (monitor.TargetLibraryRootId is { } rootId && !await db.LibraryRoots.AnyAsync(root => root.Id == rootId, token)) return;
                var query = UntargetedUnselectedRequests().Where(row => row.Id == monitor.AcquisitionId
                    && row.EntityId == monitor.EntityId && row.Kind == monitor.Kind
                    && (row.BookRendition == monitor.BookRendition
                        || row.BookRendition == BookRendition.Ebook && monitor.BookRendition == null));
                var observed = await query.AsNoTracking().FirstOrDefaultAsync(token);
                if (observed is null) return;
                var now = DateTimeOffset.UtcNow;
                if (db.Database.IsRelational()) {
                    var affected = await query.Where(row => row.UpdatedAt == observed.UpdatedAt && row.Status == observed.Status)
                        .ExecuteUpdateAsync(update => update
                            .SetProperty(row => row.ProfileId, monitor.ProfileId)
                            .SetProperty(row => row.TargetLibraryRootId, monitor.TargetLibraryRootId)
                            .SetProperty(row => row.UpdatedAt, now), token);
                    if (affected != 1) return;
                    if (db.ChangeTracker.Entries<AcquisitionRow>().FirstOrDefault(entry => entry.Entity.Id == observed.Id) is { } tracked)
                        await tracked.ReloadAsync(token);
                } else {
                    var acquisition = await query.FirstOrDefaultAsync(token);
                    if (acquisition is null || acquisition.UpdatedAt != observed.UpdatedAt || acquisition.Status != observed.Status) return;
                    acquisition.ProfileId = monitor.ProfileId;
                    acquisition.TargetLibraryRootId = monitor.TargetLibraryRootId;
                    acquisition.UpdatedAt = now;
                }
                // The prior search used fallback rules. Reconsider normally now; keep the same
                // request and let the search publisher replace its obsolete rejected candidates.
                monitor.LastSearchedAt = null;
                monitor.BarrenSearches = 0;
                monitor.UpdatedAt = now;
                await db.SaveChangesAsync(token);
            }, cancellationToken);
        }
    }

    private IQueryable<AcquisitionRow> UntargetedUnselectedRequests() => db.Acquisitions
        .Where(row => row.ProfileId == null && row.TargetLibraryRootId == null
            && (row.Status == AcquisitionStatus.AwaitingSelection || row.Status == AcquisitionStatus.WaitingForRelease)
            && row.SelectedReleaseJson == null && row.ImportCheckpointJson == null && row.FinalSourcePath == null
            && row.ImportResultJson == null && row.ImportClaimJobId == null && !row.ImportManualReview
            && row.UpgradeOfAcquisitionId == null
            && !db.DownloadTransfers.Any(transfer => transfer.AcquisitionId == row.Id)
            && !db.ReleaseCandidates.Any(release => release.AcquisitionId == row.Id && release.Accepted)
            && !db.AcquisitionImportHints.Any(hint => hint.AcquisitionId == row.Id)
            && !db.EntityFiles.Any(file => file.EntityId == row.EntityId && file.Role == EntityFileRole.Source));
}
