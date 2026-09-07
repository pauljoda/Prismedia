using Prismedia.Application.Acquisition;

namespace Prismedia.Application.Jobs.Handlers;

/// <summary>Removes failed video units from an untouched plan without losing their payload evidence.</summary>
internal static class TvVerifiedImportPlan {
    public static TvImportCheckpoint? RetainFailures(TvImportCheckpoint checkpoint, TvNewFileValidation validation,
        string libraryRootPath) {
        if (!validation.DecodedAllPendingFiles || validation.DecodeFailures.Count == 0) return null;
        var units = checkpoint.Units.Where(unit => !validation.DecodeFailures.ContainsKey(
            unit.SourceRelativePath.Replace('\\', '/'))).ToArray();
        if (units.Length == 0 || units.Length == checkpoint.Units.Count) return null;
        var ledger = checkpoint.ImportFileLedger ?? AcquisitionImportFileLedger.Create(checkpoint, libraryRootPath);
        return checkpoint with { Units = units, DiscardRemainingPayload = false,
            ImportFileLedger = ledger.RetainUnverifiedVideos(validation.DecodeFailures) };
    }
}
