using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Files;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Acquisition;

public sealed partial class EfAcquisitionStore {
    /// <inheritdoc />
    public async Task<bool> TryReviseTvImportCheckpointAsync(Guid acquisitionId, TvImportCheckpoint expected,
        TvImportCheckpoint revised, CancellationToken cancellationToken) {
        if (!await UsesCheckpointProtocolAsync(acquisitionId, AcquisitionCheckpointProtocol.Television, cancellationToken)
            || revised.ImportFileLedger is null || revised.Units.Count == 0 || revised.Units.Count >= expected.Units.Count
            || revised.Units.Distinct().Count() != revised.Units.Count
            || revised.Units.Any(unit => !expected.Units.Contains(unit))
            || expected.Units.Any(unit => unit.PreviousFilePath is not null || unit.FinalPath is not null || unit.AdoptedExistingTarget)) return false;
        // Every removed video must remain visible in the persisted payload audit.
        if (expected.Units.Except(revised.Units).Any(unit => !revised.ImportFileLedger.Files.Any(file =>
                FileSystemPathComparison.Comparer.Equals(file.SourceRelativePath, unit.SourceRelativePath.Replace('\\', '/'))
                && file.Role == AcquisitionImportFileRole.Media && file.ContentKind == AcquisitionImportContentKind.Video
                && file.Status == AcquisitionImportFileStatus.Skipped && file.Decision == AcquisitionImportDecision.HoldVerification
                && !string.IsNullOrWhiteSpace(file.TechnicalError)))) return false;
        var revisedJson = TvImportCheckpointJson.Serialize(revised);
        var allowed = expected with { Units = revised.Units, ImportFileLedger = revised.ImportFileLedger, DiscardRemainingPayload = false };
        if (revisedJson != TvImportCheckpointJson.Serialize(allowed)) return false;
        var expectedJson = TvImportCheckpointJson.Serialize(expected);
        var rootPath = await ResolveTvLedgerRootAsync(revised, cancellationToken);
        var resultJson = AcquisitionImportFileLedgerJson.Serialize(
            AcquisitionImportFileLedger.Synchronize(revised.ImportFileLedger, revised, rootPath));
        var now = DateTimeOffset.UtcNow;
        if (db.Database.IsRelational()) {
            var affected = await db.Acquisitions.Where(row => row.Id == acquisitionId
                    && row.Status == AcquisitionStatus.Importing && row.ImportClaimJobId == expected.ClaimJobId
                    && row.ImportCheckpointJson == expectedJson)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(row => row.ImportCheckpointJson, revisedJson)
                    .SetProperty(row => row.ImportResultJson, resultJson)
                    .SetProperty(row => row.UpdatedAt, now), cancellationToken);
            return await SynchronizeTrackedAcquisitionAsync(acquisitionId, affected, cancellationToken);
        }
        var row = await db.Acquisitions.FirstOrDefaultAsync(row => row.Id == acquisitionId, cancellationToken);
        if (row is null || row.Status != AcquisitionStatus.Importing || row.ImportClaimJobId != expected.ClaimJobId
            || row.ImportCheckpointJson != expectedJson) return false;
        row.ImportCheckpointJson = revisedJson;
        row.ImportResultJson = resultJson;
        row.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> TryHoldTvImportCheckpointAsync(Guid acquisitionId, TvImportCheckpoint checkpoint,
        string message, CancellationToken cancellationToken) {
        if (!await UsesCheckpointProtocolAsync(acquisitionId, AcquisitionCheckpointProtocol.Television, cancellationToken)) return false;
        var expected = TvImportCheckpointJson.Serialize(checkpoint);
        var now = DateTimeOffset.UtcNow;
        if (db.Database.IsRelational()) {
            var affected = await db.Acquisitions.Where(row => row.Id == acquisitionId
                    && row.Status == AcquisitionStatus.Importing && row.ImportClaimJobId == checkpoint.ClaimJobId
                    && row.ImportCheckpointJson == expected)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(row => row.Status, AcquisitionStatus.ManualImportRequired)
                    .SetProperty(row => row.StatusMessage, message)
                    .SetProperty(row => row.ImportClaimJobId, (Guid?)null)
                    .SetProperty(row => row.UpdatedAt, now), cancellationToken);
            return await SynchronizeTrackedAcquisitionAsync(acquisitionId, affected, cancellationToken);
        }
        var row = await db.Acquisitions.FirstOrDefaultAsync(row => row.Id == acquisitionId, cancellationToken);
        if (row is null || row.Status != AcquisitionStatus.Importing || row.ImportClaimJobId != checkpoint.ClaimJobId
            || row.ImportCheckpointJson != expected) return false;
        row.Status = AcquisitionStatus.ManualImportRequired;
        row.StatusMessage = message;
        row.ImportClaimJobId = null;
        row.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
