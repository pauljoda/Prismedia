using Prismedia.Application.Files;
using Prismedia.Application.Jobs.Scanning;

namespace Prismedia.Application.Acquisition;

/// <summary>
/// Owns the explicit policy for abandoning a durable TV import when the user supersedes its release.
/// A checkpoint can be cleared only before any planned filesystem mutation may have started. Once a
/// file moved, copied, hardlinked, or entered replacement staging, the user must finish the idempotent
/// recovery first; this avoids deleting files that a crash-time scan may already have bound to Entities.
/// </summary>
public static class TvImportCheckpointLifecycle {
    public const string CheckpointMustFinishMessage =
        "This acquisition has a partially applied TV import. Retry the import to finish its safe recovery before choosing another release.";
    public const string CorruptCheckpointMessage =
        "The saved TV import recovery checkpoint is damaged and was not guessed from library files. Review the partial import before retrying.";

    /// <summary>
    /// Reads whether an import checkpoint is still untouched and therefore safe to abandon. The scan gate
    /// keeps this filesystem observation coherent with video materialization, but this method never clears
    /// the checkpoint or changes any durable state.
    /// </summary>
    public static async Task<bool> CanAbandonAsync(
        AcquisitionImportContext import,
        CancellationToken cancellationToken,
        VideoScanConcurrencyGate? scanGate = null) {
        if (import.TvImportCheckpoint is null) {
            return true;
        }

        await using var scanLease = scanGate is null
            ? null
            : await scanGate.EnterAsync(cancellationToken);
        return CanAbandon(import);
    }

    /// <summary>
    /// Clears an untouched plan. Returns false without changing state once any planned mutation may
    /// have started, because Source binding may already exist after a crash-time scan.
    /// </summary>
    public static async Task<bool> TryAbandonAsync(
        IAcquisitionStore acquisitions,
        AcquisitionImportContext import,
        CancellationToken cancellationToken,
        VideoScanConcurrencyGate? scanGate = null) {
        if (import.TvImportCheckpoint is not { } checkpoint) {
            return true;
        }

        await using var scanLease = scanGate is null
            ? null
            : await scanGate.EnterAsync(cancellationToken);

        if (!CanAbandon(import)) {
            return false;
        }

        return await acquisitions.TryClearTvImportCheckpointAsync(
            import.Id,
            checkpoint,
            cancellationToken);
    }

    private static bool CanAbandon(AcquisitionImportContext import) =>
        import.TvImportCheckpoint is not { } checkpoint
        || checkpoint.Units.All(unit => !MutationMayHaveStarted(import, unit));

    private static bool MutationMayHaveStarted(
        AcquisitionImportContext import,
        TvImportCheckpointUnit unit) {
        if (!string.IsNullOrWhiteSpace(unit.FinalPath)) {
            return true;
        }

        var source = ResolveSourcePath(import, unit);
        if (!File.Exists(source)) {
            return true;
        }

        if (File.Exists(unit.TargetAbsolutePath) && unit.PreviousFilePath is null) {
            return true;
        }

        if (unit.PreviousFilePath is null) {
            return false;
        }

        var previous = Path.GetFullPath(unit.PreviousFilePath!);
        if (File.Exists(OwnedFileReplacementArtifacts.StagedPath(previous))
            || (!string.IsNullOrWhiteSpace(unit.ReplacementBackupPath)
                && File.Exists(unit.ReplacementBackupPath))
            || (!string.IsNullOrWhiteSpace(unit.ReplacementEvidencePath)
                && File.Exists(unit.ReplacementEvidencePath))) {
            return true;
        }

        var target = Path.GetFullPath(unit.TargetAbsolutePath);
        return File.Exists(target)
            && !FileSystemPathComparison.Equals(target, previous)
            && FilesHaveSameContent(target, source);
    }

    private static string ResolveSourcePath(AcquisitionImportContext import, TvImportCheckpointUnit unit) {
        if (!string.IsNullOrWhiteSpace(unit.SourceAbsolutePath)) {
            return Path.GetFullPath(unit.SourceAbsolutePath);
        }

        var contentRoot = !string.IsNullOrWhiteSpace(import.ContentPath)
            ? Path.GetFullPath(import.ContentPath)
            : Path.GetDirectoryName(Path.GetFullPath(unit.TargetAbsolutePath))!;
        return Path.GetFullPath(Path.Combine(contentRoot, unit.SourceRelativePath));
    }

    private static bool FilesHaveSameContent(string firstPath, string secondPath) {
        try {
            var first = new FileInfo(firstPath);
            var second = new FileInfo(secondPath);
            if (!first.Exists || !second.Exists || first.Length != second.Length) {
                return false;
            }

            using var firstStream = first.OpenRead();
            using var secondStream = second.OpenRead();
            return System.Security.Cryptography.SHA256.HashData(firstStream)
                .AsSpan()
                .SequenceEqual(System.Security.Cryptography.SHA256.HashData(secondStream));
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            return false;
        }
    }

}
