using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Entities;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>Removed-target reconciliation for <see cref="EfMonitorStore"/>.</summary>
public sealed partial class EfMonitorStore {
    private static readonly AcquisitionStatus[] OrphanRetirableStatuses = [
        AcquisitionStatus.Pending,
        AcquisitionStatus.Searching,
        AcquisitionStatus.AwaitingSelection,
        AcquisitionStatus.Failed,
        AcquisitionStatus.ManualImportRequired
    ];

    /// <summary>Reconciles passive work whose target disappeared or was fulfilled outside that work.</summary>
    private async Task ReconcilePassiveTargetsAsync(CancellationToken cancellationToken) {
        await RetireMissingEntityTargetsAsync(cancellationToken);
        await RetireFulfilledEntityTargetsAsync(cancellationToken);
    }

    /// <summary>
    /// Retires passive monitoring rows whose target Entity no longer exists. Imported and cancelled
    /// acquisitions remain as history, while passive work is cancelled and its now-unusable import hints
    /// are removed. Transfer-owning states are deliberately left alone for the normal teardown workflow.
    /// </summary>
    private async Task RetireMissingEntityTargetsAsync(CancellationToken cancellationToken) {
        var missingTargets = await db.Monitors
            .Where(monitor => monitor.EntityId != null
                && !db.Entities.Any(entity => entity.Id == monitor.EntityId))
            .ToArrayAsync(cancellationToken);
        if (missingTargets.Length == 0) {
            return;
        }

        var acquisitionIds = missingTargets
            .Where(monitor => monitor.AcquisitionId != null)
            .Select(monitor => monitor.AcquisitionId!.Value)
            .Distinct()
            .ToArray();
        var acquisitions = await db.Acquisitions
            .Where(acquisition => acquisitionIds.Contains(acquisition.Id))
            .ToArrayAsync(cancellationToken);
        var acquisitionById = acquisitions.ToDictionary(acquisition => acquisition.Id);
        var safelyRetired = missingTargets
            .Where(monitor => monitor.AcquisitionId is not { } acquisitionId
                || !acquisitionById.TryGetValue(acquisitionId, out var acquisition)
                || acquisition.Status is AcquisitionStatus.Imported or AcquisitionStatus.Cancelled
                || OrphanRetirableStatuses.Contains(acquisition.Status))
            .ToArray();
        if (safelyRetired.Length == 0) {
            return;
        }

        var safelyRetiredAcquisitionIds = safelyRetired
            .Where(monitor => monitor.AcquisitionId != null)
            .Select(monitor => monitor.AcquisitionId!.Value)
            .Distinct()
            .ToArray();
        var now = DateTimeOffset.UtcNow;
        foreach (var acquisition in acquisitions.Where(acquisition =>
            safelyRetiredAcquisitionIds.Contains(acquisition.Id)
            && OrphanRetirableStatuses.Contains(acquisition.Status))) {
            acquisition.Status = AcquisitionStatus.Cancelled;
            acquisition.StatusMessage = "Target Entity no longer exists; monitoring was retired.";
            acquisition.ImportCheckpointJson = null;
            acquisition.ImportClaimJobId = null;
            acquisition.UpdatedAt = now;
        }

        db.AcquisitionImportHints.RemoveRange(await db.AcquisitionImportHints
            .Where(hint => safelyRetiredAcquisitionIds.Contains(hint.AcquisitionId))
            .ToArrayAsync(cancellationToken));
        db.Monitors.RemoveRange(safelyRetired);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Cancels passive non-upgrade acquisitions after another import or scan fulfills their complete Entity
    /// subtree. The stable Entity monitor remains Active and detaches from transient acquisition bookkeeping.
    /// </summary>
    private async Task RetireFulfilledEntityTargetsAsync(CancellationToken cancellationToken) {
        var candidates = await (
            from monitor in db.Monitors
            where monitor.AcquisitionId != null
            join acquisition in db.Acquisitions on monitor.AcquisitionId equals acquisition.Id
            where acquisition.EntityId != null
                && acquisition.UpgradeOfAcquisitionId == null
                && OrphanRetirableStatuses.Contains(acquisition.Status)
            select new { Monitor = monitor, Acquisition = acquisition })
            .ToArrayAsync(cancellationToken);
        if (candidates.Length == 0) {
            return;
        }

        var fulfilledEntityIds = await new EfEntityFulfillmentProjection(db).ResolveAsync(
            candidates.Select(candidate => candidate.Acquisition.EntityId!.Value).Distinct().ToArray(),
            cancellationToken);
        var fulfilled = candidates
            .Where(candidate => fulfilledEntityIds.Contains(candidate.Acquisition.EntityId!.Value))
            .ToArray();
        if (fulfilled.Length == 0) {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var acquisitionIds = new HashSet<Guid>();
        foreach (var candidate in fulfilled) {
            candidate.Acquisition.Status = AcquisitionStatus.Cancelled;
            candidate.Acquisition.StatusMessage = "Target Entity was fulfilled by another import or scan.";
            candidate.Acquisition.ImportCheckpointJson = null;
            candidate.Acquisition.ImportClaimJobId = null;
            candidate.Acquisition.UpdatedAt = now;
            acquisitionIds.Add(candidate.Acquisition.Id);

            candidate.Monitor.EntityId ??= candidate.Acquisition.EntityId;
            candidate.Monitor.AcquisitionId = null;
            candidate.Monitor.UpdatedAt = now;
        }

        db.AcquisitionImportHints.RemoveRange(await db.AcquisitionImportHints
            .Where(hint => acquisitionIds.Contains(hint.AcquisitionId))
            .ToArrayAsync(cancellationToken));
        await db.SaveChangesAsync(cancellationToken);
    }
}
