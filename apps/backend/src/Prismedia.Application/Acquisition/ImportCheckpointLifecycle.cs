using Prismedia.Application.Jobs.Scanning;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Acquisition;

/// <summary>
/// Owns the policy for abandoning any durable import placement. An untouched reserved plan may be
/// cleared; once a source disappeared, a target appeared, or completion was checkpointed, the exact
/// idempotent attempt must finish before another release can supersede it.
/// </summary>
public static class ImportCheckpointLifecycle {
    public const string CheckpointMustFinishMessage =
        "This acquisition has a partially applied import. Retry it to finish safe recovery before choosing another release.";
    public const string CorruptCheckpointMessage =
        "The saved import recovery checkpoint is damaged and was not guessed from library files. Review the partial import before retrying.";

    /// <summary>True when no durable plan exists, or the current plan has not mutated the filesystem.</summary>
    public static async Task<bool> CanAbandonAsync(
        AcquisitionImportContext import,
        CancellationToken cancellationToken,
        VideoScanConcurrencyGate? scanGate = null) {
        import.EnsureCheckpointApplicability();
        return import.CheckpointProtocol switch {
            AcquisitionCheckpointProtocol.Placement =>
                import.PlacementCheckpoint is not { } checkpoint || CanAbandon(checkpoint),
            AcquisitionCheckpointProtocol.Television =>
                await TvImportCheckpointLifecycle.CanAbandonAsync(import, cancellationToken, scanGate),
            _ => throw new InvalidOperationException(
                $"Unknown acquisition checkpoint protocol '{import.CheckpointProtocol}'.")
        };
    }

    /// <summary>
    /// Clears the exact untouched plan with compare-and-swap lifecycle protection. Returns false after
    /// any filesystem mutation may have started.
    /// </summary>
    public static async Task<bool> TryAbandonAsync(
        IAcquisitionStore acquisitions,
        AcquisitionImportContext import,
        CancellationToken cancellationToken,
        VideoScanConcurrencyGate? scanGate = null) {
        import.EnsureCheckpointApplicability();
        switch (import.CheckpointProtocol) {
            case AcquisitionCheckpointProtocol.Placement:
                if (import.PlacementCheckpoint is not { } checkpoint) {
                    return true;
                }

                if (!CanAbandon(checkpoint)) {
                    return false;
                }

                return await acquisitions.TryClearImportPlacementCheckpointAsync(
                    import.Id,
                    checkpoint,
                    cancellationToken);
            case AcquisitionCheckpointProtocol.Television:
                return await TvImportCheckpointLifecycle.TryAbandonAsync(
                    acquisitions,
                    import,
                    cancellationToken,
                    scanGate);
            default:
                throw new InvalidOperationException(
                    $"Unknown acquisition checkpoint protocol '{import.CheckpointProtocol}'.");
        }
    }

    private static bool CanAbandon(ImportPlacementCheckpoint checkpoint) =>
        checkpoint.Units.All(unit =>
            unit.FinalPath is null
            && File.Exists(unit.SourceAbsolutePath)
            && !File.Exists(unit.TargetAbsolutePath));
}
