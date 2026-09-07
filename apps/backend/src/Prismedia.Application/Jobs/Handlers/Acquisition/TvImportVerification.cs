using Prismedia.Application.Acquisition;
using Prismedia.Application.Files;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Application.Jobs.Scanning;

namespace Prismedia.Application.Jobs.Handlers;

/// <summary>Lets scans run during decoding only while every planned video still lives outside the library.</summary>
internal sealed class TvImportVerification(ILibraryScanRootPersistence roots, IImportTargetIndex targets,
    IAcquisitionStore acquisitions, VideoScanConcurrencyGate scanGate, TvNewFileValidation validation) {
    public async Task<TvImportVerificationLease> VerifyAsync(IAsyncDisposable planningLease, JobContext context,
        AcquisitionImportContext import, DownloadPayload? payload, TvImportCheckpoint checkpoint,
        SelectedRelease? selected, CancellationToken token) {
        var enabledRoots = await roots.GetEnabledRootsAsync(token);
        var canRelease = checkpoint.Units.Count > 0 && checkpoint.Units.All(unit =>
            unit.PreviousFilePath is null && unit.FinalPath is null && !unit.AdoptedExistingTarget
            && !string.IsNullOrWhiteSpace(unit.SourceAbsolutePath) && File.Exists(unit.SourceAbsolutePath)
            && !File.Exists(unit.TargetAbsolutePath) && !Directory.Exists(unit.TargetAbsolutePath)
            && !enabledRoots.Any(root => root.ScanVideos
                && FileSystemPathComparison.IsSameOrDescendant(root.Path, unit.SourceAbsolutePath)));
        if (!canRelease) {
            var failure = await validation.ValidateAsync(context, import, payload, checkpoint, selected, token);
            return new(null, true, failure);
        }

        var before = await TvImportVerificationSnapshot.ReadAsync(roots, targets, import, checkpoint, token);
        await planningLease.DisposeAsync();
        var hold = await validation.ValidateAsync(context, import, payload, checkpoint, selected, token);
        await context.ReportProgressAsync(35, "Waiting for library access after verification", token);
        var placementLease = await scanGate.EnterAsync(token);
        try {
            // Cancellation, a new download or edited mappings can supersede the plan during decoding.
            // A stale job must not publish either its old success or its old failure over that decision.
            if (!await acquisitions.IsCurrentTvImportCheckpointAsync(import.Id, checkpoint, token)) {
                return new(placementLease, false, null);
            }
            var after = await TvImportVerificationSnapshot.ReadAsync(roots, targets, import, checkpoint, token);
            if (!string.Equals(before, after, StringComparison.Ordinal)) {
                if (await acquisitions.TryHoldTvImportCheckpointAsync(import.Id, checkpoint,
                        "The episode catalog, library ownership, or downloaded files changed during verification. Review the import plan before retrying; all files were preserved.", token)) {
                    // Discard only a still-untouched plan so retry cannot reuse obsolete mappings.
                    // A competing target or a consumed source retains the full recovery checkpoint.
                    await TvImportCheckpointLifecycle.TryAbandonAsync(acquisitions,
                        import with { TvImportCheckpoint = checkpoint }, token);
                }
                return new(placementLease, false, null);
            }
            hold ??= await validation.ValidateCurrentProfileAsync(context, import, payload, checkpoint, selected, token);
            if (hold is not null) {
                await acquisitions.TryHoldTvImportCheckpointAsync(import.Id, checkpoint, hold, token);
                return new(placementLease, false, null);
            }
            return new(placementLease, await acquisitions.IsCurrentTvImportCheckpointAsync(import.Id, checkpoint, token), null);
        } catch {
            await placementLease.DisposeAsync();
            throw;
        }
    }
}

/// <summary>Keeps placement exclusive after verification and distinguishes superseded work from a review hold.</summary>
internal sealed record TvImportVerificationLease(IAsyncDisposable? Lease, bool IsCurrent, string? Failure) : IAsyncDisposable {
    public ValueTask DisposeAsync() => Lease?.DisposeAsync() ?? ValueTask.CompletedTask;
}
