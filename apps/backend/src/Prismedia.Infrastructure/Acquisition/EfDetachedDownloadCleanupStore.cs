using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>
/// Atomically transfers exact client-item ownership out of an acquisition and retains it until a monitor
/// confirms removal from the recorded download client.
/// </summary>
public sealed class EfDetachedDownloadCleanupStore(PrismediaDbContext db) : IDetachedDownloadCleanupStore {
    /// <inheritdoc />
    public async Task<bool> DetachAsync(
        Guid acquisitionId,
        Guid downloadClientConfigId,
        string clientItemId,
        CancellationToken cancellationToken) {
        var acquisition = await db.Acquisitions.FirstOrDefaultAsync(
            row => row.Id == acquisitionId && row.Status == AcquisitionStatus.Queued,
            cancellationToken);
        var transfer = await db.DownloadTransfers.FirstOrDefaultAsync(
            row => row.AcquisitionId == acquisitionId
                && row.DownloadClientConfigId == downloadClientConfigId
                && row.ClientItemId == clientItemId,
            cancellationToken);
        if (acquisition is null
            || transfer is null
            || !await db.DownloadClientConfigs.AnyAsync(
                row => row.Id == downloadClientConfigId,
                cancellationToken)) {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        db.DetachedDownloadCleanups.Add(new DetachedDownloadCleanupRow {
            Id = Guid.NewGuid(),
            SourceAcquisitionId = acquisitionId,
            DownloadClientConfigId = downloadClientConfigId,
            ClientItemId = clientItemId,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.DownloadTransfers.Remove(transfer);
        // Status is a concurrency token. Marking the already-Queued value as modified turns the queue
        // claim check above into part of this transaction: a concurrent teardown rolls the detach back.
        db.Entry(acquisition).Property(row => row.Status).IsModified = true;
        try {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        } catch (DbUpdateConcurrencyException) {
            db.ChangeTracker.Clear();
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DetachedDownloadCleanup>> ListAsync(CancellationToken cancellationToken) =>
        await db.DetachedDownloadCleanups.AsNoTracking()
            .OrderBy(row => row.CreatedAt)
            .Select(row => new DetachedDownloadCleanup(
                row.Id,
                row.SourceAcquisitionId,
                row.DownloadClientConfigId,
                row.ClientItemId,
                row.CreatedAt))
            .ToArrayAsync(cancellationToken);

    /// <inheritdoc />
    public async Task CompleteAsync(Guid cleanupId, CancellationToken cancellationToken) {
        if (db.Database.IsRelational()) {
            await db.DetachedDownloadCleanups
                .Where(row => row.Id == cleanupId)
                .ExecuteDeleteAsync(cancellationToken);
            return;
        }

        var row = await db.DetachedDownloadCleanups.FirstOrDefaultAsync(
            candidate => candidate.Id == cleanupId,
            cancellationToken);
        if (row is null) {
            return;
        }

        db.DetachedDownloadCleanups.Remove(row);
        await db.SaveChangesAsync(cancellationToken);
    }
}
