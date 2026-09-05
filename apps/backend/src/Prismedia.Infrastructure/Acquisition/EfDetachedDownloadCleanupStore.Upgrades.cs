using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Acquisition;

public sealed partial class EfDetachedDownloadCleanupStore {
    /// <inheritdoc />
    public async Task<bool> PreserveUpgradeAsync(Guid acquisitionId, CancellationToken cancellationToken) {
        var acquisition = await db.Acquisitions.FirstOrDefaultAsync(row => row.Id == acquisitionId, cancellationToken);
        if (acquisition is null) return true;
        if (acquisition.Status != AcquisitionStatus.Importing || acquisition.UpgradeOfAcquisitionId is null) return false;
        var transfers = await db.DownloadTransfers.Where(row => row.AcquisitionId == acquisitionId).ToArrayAsync(cancellationToken);
        if (transfers.Length == 0) return true;
        var remote = transfers.Where(row => uploads?.Owns(row.ClientItemId) != true).ToArray();
        var clientIds = remote.Select(row => row.DownloadClientConfigId).ToArray();
        var knownClients = await db.DownloadClientConfigs.Where(row => clientIds.Contains(row.Id))
            .Select(row => row.Id).ToArrayAsync(cancellationToken);
        if (remote.Any(row => row.DownloadClientConfigId is not { } id || !knownClients.Contains(id)
            || string.IsNullOrWhiteSpace(row.ClientItemId) || !double.IsFinite(row.Progress) || row.Progress < 1)) return false;
        var importedSource = await db.Acquisitions.Where(row => row.Id == acquisition.UpgradeOfAcquisitionId)
            .Select(row => row.FinalSourcePath).FirstOrDefaultAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        foreach (var upload in transfers.Except(remote)) {
            await uploads!.DeleteAsync(upload.ClientItemId, cancellationToken);
        }
        foreach (var transfer in remote) {
            db.DetachedDownloadCleanups.Add(new DetachedDownloadCleanupRow {
                Id = transfer.Id,
                SourceAcquisitionId = acquisitionId,
                DownloadClientConfigId = transfer.DownloadClientConfigId!.Value,
                ClientItemId = transfer.ClientItemId,
                ContentPath = transfer.ContentPath,
                ImportedSourcePath = importedSource,
                CreatedAt = now,
                UpdatedAt = now
            });
        }
        db.DownloadTransfers.RemoveRange(transfers);
        // This status CAS and the transfer-to-receipt handoff commit together. Teardown must win
        // without losing its active pointer if it changes the acquisition while readiness completes.
        db.Entry(acquisition).Property(row => row.Status).IsModified = true;
        try {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        } catch (DbUpdateConcurrencyException) {
            db.ChangeTracker.Clear();
            return false;
        }
    }
}
