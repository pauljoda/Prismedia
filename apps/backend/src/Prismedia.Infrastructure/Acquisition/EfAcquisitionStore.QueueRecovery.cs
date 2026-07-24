using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>Queue-recovery and EF identity-map consistency behavior for <see cref="EfAcquisitionStore"/>.</summary>
public sealed partial class EfAcquisitionStore {
    private static readonly AcquisitionStatus[] ResettableDownloadStatuses = [
        AcquisitionStatus.Queued,
        AcquisitionStatus.Downloading,
        AcquisitionStatus.WaitingForDownloadClient,
    ];

    /// <inheritdoc />
    public async Task<IReadOnlyList<TransferlessQueueClaim>> ListTransferlessQueueClaimsAsync(
        TimeSpan olderThan,
        CancellationToken cancellationToken) {
        var cutoff = DateTimeOffset.UtcNow - olderThan;
        return await db.Acquisitions.AsNoTracking()
            .Where(row => row.Status == AcquisitionStatus.Queued
                && row.SelectedReleaseJson == null
                && row.UpdatedAt < cutoff
                && !db.DownloadTransfers.Any(transfer => transfer.AcquisitionId == row.Id))
            .Select(row => new TransferlessQueueClaim(
                row.Id,
                db.ReleaseCandidates.Any(candidate => candidate.AcquisitionId == row.Id),
                row.UpdatedAt))
            .ToArrayAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> TryRecoverTransferlessQueueClaimAsync(
        Guid acquisitionId,
        AcquisitionStatus recoveryStatus,
        string message,
        CancellationToken cancellationToken) {
        var now = DateTimeOffset.UtcNow;
        if (db.Database.IsRelational()) {
            var affected = await db.Acquisitions
                .Where(row => row.Id == acquisitionId
                    && row.Status == AcquisitionStatus.Queued
                    && row.SelectedReleaseJson == null
                    && !db.DownloadTransfers.Any(transfer => transfer.AcquisitionId == row.Id))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(row => row.Status, recoveryStatus)
                    .SetProperty(row => row.StatusMessage, message)
                    .SetProperty(row => row.ImportClaimJobId, (Guid?)null)
                    .SetProperty(row => row.UpdatedAt, now), cancellationToken);
            return await SynchronizeTrackedAcquisitionAsync(acquisitionId, affected, cancellationToken);
        }

        var row = await db.Acquisitions.FirstOrDefaultAsync(
            candidate => candidate.Id == acquisitionId,
            cancellationToken);
        if (row is null
            || row.Status != AcquisitionStatus.Queued
            || row.SelectedReleaseJson is not null
            || await db.DownloadTransfers.AnyAsync(
                transfer => transfer.AcquisitionId == acquisitionId,
                cancellationToken)) {
            return false;
        }

        row.Status = recoveryStatus;
        row.StatusMessage = message;
        row.ImportClaimJobId = null;
        row.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> HasActiveTransfersAsync(CancellationToken cancellationToken) {
        var active = new[] {
            AcquisitionStatus.Queued,
            AcquisitionStatus.Downloading,
            AcquisitionStatus.WaitingForDownloadClient,
        };
        // Seeding watches keep the monitor scheduled after import, so seed goals are actually enforced.
        // Pre-Add placeholders and transferless claims also count so the monitor repairs interrupted handoffs.
        var hasTransfer = await (
            from transfer in db.DownloadTransfers.AsNoTracking()
            join acquisition in db.Acquisitions.AsNoTracking() on transfer.AcquisitionId equals acquisition.Id
            where active.Contains(acquisition.Status) || transfer.SeedingSince != null
            select transfer.Id).AnyAsync(cancellationToken);
        return hasTransfer || await db.Acquisitions.AsNoTracking().AnyAsync(
            row => (row.Status == AcquisitionStatus.Queued
                    && row.SelectedReleaseJson == null
                    && !db.DownloadTransfers.Any(transfer => transfer.AcquisitionId == row.Id))
                || (row.Status == AcquisitionStatus.WaitingForDownloadClient
                    && !db.DownloadTransfers.Any(transfer => transfer.AcquisitionId == row.Id)),
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> ListTransferlessDownloadClientWaitsAsync(
        CancellationToken cancellationToken) =>
        await db.Acquisitions.AsNoTracking()
            .Where(row => row.Status == AcquisitionStatus.WaitingForDownloadClient
                && !db.DownloadTransfers.Any(transfer => transfer.AcquisitionId == row.Id))
            .OrderBy(row => row.UpdatedAt)
            .Select(row => row.Id)
            .ToArrayAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<bool> TryResetDownloadAttemptAsync(
        Guid acquisitionId,
        Guid transferId,
        AcquisitionStatus recoveryStatus,
        string message,
        CancellationToken cancellationToken) {
        if (recoveryStatus is not (AcquisitionStatus.Searching or AcquisitionStatus.WaitingForDownloadClient)) {
            throw new ArgumentOutOfRangeException(
                nameof(recoveryStatus),
                recoveryStatus,
                "A cleared download attempt can only restart searching or wait for a replacement client.");
        }

        var acquisition = await db.Acquisitions.FirstOrDefaultAsync(
            row => row.Id == acquisitionId,
            cancellationToken);
        if (acquisition is null || !ResettableDownloadStatuses.Contains(acquisition.Status)) {
            return false;
        }

        var transfers = await db.DownloadTransfers
            .Where(row => row.AcquisitionId == acquisitionId)
            .ToArrayAsync(cancellationToken);
        if (!transfers.Any(row => row.Id == transferId)) {
            return false;
        }

        var candidates = await db.ReleaseCandidates
            .Where(row => row.AcquisitionId == acquisitionId)
            .ToArrayAsync(cancellationToken);
        var hints = await db.AcquisitionImportHints
            .Where(row => row.AcquisitionId == acquisitionId)
            .ToArrayAsync(cancellationToken);

        db.DownloadTransfers.RemoveRange(transfers);
        db.ReleaseCandidates.RemoveRange(candidates);
        db.AcquisitionImportHints.RemoveRange(hints);

        acquisition.Status = recoveryStatus;
        acquisition.StatusMessage = message;
        acquisition.SelectedReleaseJson = null;
        acquisition.FinalSourcePath = null;
        acquisition.ImportCheckpointJson = null;
        acquisition.ImportResultJson = null;
        acquisition.ImportClaimJobId = null;
        acquisition.UpdatedAt = DateTimeOffset.UtcNow;

        try {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        } catch (DbUpdateConcurrencyException) {
            db.ChangeTracker.Clear();
            return false;
        }
    }

    /// <summary>
    /// ExecuteUpdate bypasses EF's identity map. Refresh a same-scope row after a successful compare-and-set
    /// so the next tracking query cannot observe and later persist the lifecycle state that preceded it.
    /// </summary>
    private async Task<bool> SynchronizeTrackedAcquisitionAsync(
        Guid acquisitionId,
        int affected,
        CancellationToken cancellationToken) {
        if (affected != 1) {
            return false;
        }

        var entry = db.ChangeTracker.Entries<AcquisitionRow>()
            .FirstOrDefault(candidate => candidate.Entity.Id == acquisitionId);
        if (entry is not null) {
            await entry.ReloadAsync(cancellationToken);
        }

        return true;
    }

    private void DetachTrackedReleaseCandidates(Guid acquisitionId) {
        foreach (var entry in db.ChangeTracker.Entries<ReleaseCandidateRow>()
                     .Where(candidate => candidate.Entity.AcquisitionId == acquisitionId)
                     .ToArray()) {
            entry.State = EntityState.Detached;
        }
    }

    private void DetachTrackedTransferAdds(
        Guid acquisitionId,
        Guid downloadClientConfigId,
        string correlation,
        string addingCode) {
        foreach (var entry in db.ChangeTracker.Entries<DownloadTransferRow>()
                     .Where(candidate => candidate.Entity.AcquisitionId == acquisitionId
                         && candidate.Entity.DownloadClientConfigId == downloadClientConfigId
                         && candidate.Entity.ClientItemId == correlation
                         && candidate.Entity.State == addingCode)
                     .ToArray()) {
            entry.State = EntityState.Detached;
        }
    }
}
