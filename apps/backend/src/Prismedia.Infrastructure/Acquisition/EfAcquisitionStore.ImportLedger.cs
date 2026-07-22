using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>Privacy-safe import-ledger path resolution kept separate from checkpoint persistence.</summary>
public sealed partial class EfAcquisitionStore {
    public async Task<AcquisitionTransferInfo?> GetTransferInfoAsync(
        Guid acquisitionId,
        CancellationToken cancellationToken) {
        var row = await db.Acquisitions
            .AsNoTracking()
            .Where(row => row.Id == acquisitionId)
            .Select(row => new { row.Status, row.FinalSourcePath, row.ImportResultJson })
            .FirstOrDefaultAsync(cancellationToken);
        if (row is null) {
            return null;
        }

        var transfer = await db.DownloadTransfers
            .AsNoTracking()
            .Where(transfer => transfer.AcquisitionId == acquisitionId)
            .OrderByDescending(transfer => transfer.CreatedAt)
            .Select(transfer => new {
                transfer.ClientItemId,
                transfer.DownloadClientConfigId,
                transfer.Category,
                transfer.State
            })
            .FirstOrDefaultAsync(cancellationToken);

        var readable = AcquisitionImportFileLedgerJson.TryDeserialize(row.ImportResultJson, out var result);
        return new AcquisitionTransferInfo(
            row.Status,
            row.FinalSourcePath,
            transfer?.ClientItemId,
            transfer?.DownloadClientConfigId,
            transfer?.Category,
            transfer?.State,
            result,
            !readable);
    }

    public async Task SetFinalSourcePathAsync(
        Guid acquisitionId,
        string finalSourcePath,
        CancellationToken cancellationToken) {
        var row = await db.Acquisitions.FirstOrDefaultAsync(row => row.Id == acquisitionId, cancellationToken);
        if (row is null) {
            return;
        }

        row.FinalSourcePath = finalSourcePath;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SetTvImportCheckpointAsync(
        Guid acquisitionId,
        TvImportCheckpoint? checkpoint,
        CancellationToken cancellationToken) {
        var row = await db.Acquisitions.FirstOrDefaultAsync(row => row.Id == acquisitionId, cancellationToken);
        if (row is null) {
            return;
        }

        row.ImportCheckpointJson = checkpoint is null ? null : TvImportCheckpointJson.Serialize(checkpoint);
        if (checkpoint is not null) {
            var libraryRootPath = await ResolveTvLedgerRootAsync(checkpoint, cancellationToken);
            row.ImportResultJson = AcquisitionImportFileLedgerJson.Serialize(
                AcquisitionImportFileLedger.Synchronize(
                    checkpoint.ImportFileLedger ?? AcquisitionImportFileLedger.Create(checkpoint, libraryRootPath),
                    checkpoint,
                    libraryRootPath));
        }
        row.ImportClaimJobId = checkpoint?.ClaimJobId;
        if (checkpoint is null) {
            row.FinalSourcePath = null;
            var staleHints = await db.AcquisitionImportHints
                .Where(hint => hint.AcquisitionId == acquisitionId)
                .ToArrayAsync(cancellationToken);
            db.AcquisitionImportHints.RemoveRange(staleHints);
        }
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> TryCreateTvImportCheckpointAsync(
        Guid acquisitionId,
        TvImportCheckpoint checkpoint,
        CancellationToken cancellationToken) {
        var checkpointJson = TvImportCheckpointJson.Serialize(checkpoint);
        var libraryRootPath = await ResolveTvLedgerRootAsync(checkpoint, cancellationToken);
        var resultJson = AcquisitionImportFileLedgerJson.Serialize(
            AcquisitionImportFileLedger.Synchronize(
                checkpoint.ImportFileLedger ?? AcquisitionImportFileLedger.Create(checkpoint, libraryRootPath),
                checkpoint,
                libraryRootPath));
        var transferClientItemId = checkpoint.TransferClientItemId;
        var now = DateTimeOffset.UtcNow;
        if (db.Database.IsRelational()) {
            var affected = await db.Acquisitions
                .Where(row => row.Id == acquisitionId
                    && row.Status == AcquisitionStatus.Importing
                    && row.ImportCheckpointJson == null
                    && row.ImportClaimJobId == checkpoint.ClaimJobId
                    && (transferClientItemId == null || db.DownloadTransfers.Any(transfer =>
                        transfer.AcquisitionId == acquisitionId
                        && transfer.ClientItemId == transferClientItemId)))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(row => row.ImportCheckpointJson, checkpointJson)
                    .SetProperty(row => row.ImportResultJson, resultJson)
                    .SetProperty(row => row.UpdatedAt, now), cancellationToken);
            return await SynchronizeTrackedAcquisitionAsync(acquisitionId, affected, cancellationToken);
        }

        var row = await db.Acquisitions.FirstOrDefaultAsync(value => value.Id == acquisitionId, cancellationToken);
        if (row is null
            || row.Status != AcquisitionStatus.Importing
            || row.ImportCheckpointJson is not null
            || row.ImportClaimJobId != checkpoint.ClaimJobId
            || (transferClientItemId is not null && !await db.DownloadTransfers.AnyAsync(transfer =>
                transfer.AcquisitionId == acquisitionId
                && transfer.ClientItemId == transferClientItemId, cancellationToken))) {
            return false;
        }

        row.ImportCheckpointJson = checkpointJson;
        row.ImportResultJson = resultJson;
        row.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<string> ResolveTvLedgerRootAsync(
        TvImportCheckpoint checkpoint,
        CancellationToken cancellationToken) {
        if (!string.IsNullOrWhiteSpace(checkpoint.LibraryRootPath)) {
            return checkpoint.LibraryRootPath;
        }

        var configured = await db.LibraryRoots
            .Where(root => root.Id == checkpoint.LibraryRootId)
            .Select(root => root.Path)
            .SingleOrDefaultAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(configured)) {
            return configured;
        }

        // Checkpoint-only tests and legacy recovery records can predate the captured root. Falling back to
        // the series parent remains privacy-safe; production-created checkpoints always use the exact root.
        return Path.GetDirectoryName(checkpoint.SeriesFolderPath) ?? checkpoint.SeriesFolderPath;
    }
}
