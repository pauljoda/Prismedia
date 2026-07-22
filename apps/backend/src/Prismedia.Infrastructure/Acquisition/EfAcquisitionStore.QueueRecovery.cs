using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>Queue-recovery and EF identity-map consistency behavior for <see cref="EfAcquisitionStore"/>.</summary>
public sealed partial class EfAcquisitionStore {
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
        };
        // Seeding watches keep the monitor scheduled after import, so seed goals are actually enforced.
        // Pre-Add placeholders and transferless claims also count so the monitor repairs interrupted handoffs.
        var hasTransfer = await (
            from transfer in db.DownloadTransfers.AsNoTracking()
            join acquisition in db.Acquisitions.AsNoTracking() on transfer.AcquisitionId equals acquisition.Id
            where active.Contains(acquisition.Status) || transfer.SeedingSince != null
            select transfer.Id).AnyAsync(cancellationToken);
        return hasTransfer || await db.Acquisitions.AsNoTracking().AnyAsync(
            row => row.Status == AcquisitionStatus.Queued
                && row.SelectedReleaseJson == null
                && !db.DownloadTransfers.Any(transfer => transfer.AcquisitionId == row.Id),
            cancellationToken);
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
