using Microsoft.Extensions.Logging;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Entities;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers;

/// <summary>
/// Imports a completed acquisition by dispatching to the media kind's <see cref="IAcquisitionImportEngine"/>
/// (books, movies, music — each owns its planning, placement, hint, and scan chaining). A payload carrying
/// an executable or dangerous file is held for manual review before any engine runs. A kind with no
/// registered engine stays Downloaded (files intact in the client) with an honest status instead of being
/// pushed through the wrong pipeline.
/// </summary>
public sealed class AcquisitionImportJobHandler(
    IAcquisitionStore acquisitions,
    IAcquisitionImportEngineFactory engines,
    IDownloadPayloadReader payloads,
    IAcquisitionHistoryStore history,
    ILogger<AcquisitionImportJobHandler> logger,
    IEntityLifecycleMutationLease? lifecycle = null) : IJobHandler {
    public JobType Type => JobType.AcquisitionImport;

    public async Task HandleAsync(JobContext context, CancellationToken cancellationToken) {
        var payload = AcquisitionJobPayload.Parse(context.Job.PayloadJson);
        AcquisitionImportContext? import;
        try {
            import = await acquisitions.GetImportContextAsync(payload.AcquisitionId, cancellationToken);
        } catch (InvalidDataException ex) {
            logger.LogError(ex, "AcquisitionImport: corrupt placement checkpoint held for acquisition {Id}", payload.AcquisitionId);
            await acquisitions.TryHoldCorruptImportCheckpointAsync(
                payload.AcquisitionId,
                context.Job.Id,
                ImportCheckpointLifecycle.CorruptCheckpointMessage,
                CancellationToken.None);
            return;
        }
        if (import is null) {
            return;
        }

        var tvCheckpoint = import.TvImportCheckpoint;
        var placementCheckpoint = import.ImportPlacementCheckpoint;
        var claimed = false;
        async Task ClaimImportAsync(CancellationToken leaseCancellationToken) {
            if (tvCheckpoint is not null) {
                claimed = await acquisitions.TryClaimTvImportCheckpointAsync(
                    payload.AcquisitionId,
                    tvCheckpoint,
                    context.Job.Id,
                    leaseCancellationToken);
                return;
            }

            if (placementCheckpoint is not null) {
                claimed = await acquisitions.TryClaimImportPlacementCheckpointAsync(
                    payload.AcquisitionId,
                    placementCheckpoint,
                    context.Job.Id,
                    leaseCancellationToken);
                return;
            }

            claimed = await acquisitions.TryClaimInitialImportAsync(
                payload.AcquisitionId,
                context.Job.Id,
                payload.ManualRetry,
                leaseCancellationToken);
        }

        if (import.EntityId is { } entityId) {
            if (lifecycle is null) {
                throw new InvalidOperationException(
                    "Entity-linked import requires the Entity lifecycle mutation lease.");
            }

            if (!await lifecycle.ExecuteAsync(
                    entityId,
                    ClaimImportAsync,
                    cancellationToken)) {
                throw new EntityLifecycleMutationConflictException(entityId);
            }
        } else {
            await ClaimImportAsync(cancellationToken);
        }

        if (!claimed) {
            logger.LogInformation(
                "AcquisitionImport: acquisition {Id} left its claimable state before this job could claim it; skipping stale work.",
                payload.AcquisitionId);
            return;
        }

        if (tvCheckpoint is not null) {
            tvCheckpoint = tvCheckpoint with { ClaimJobId = context.Job.Id };
            import = import with { TvImportCheckpoint = tvCheckpoint };
        } else if (placementCheckpoint is not null) {
            placementCheckpoint = placementCheckpoint with { ClaimJobId = context.Job.Id };
            import = import with { ImportPlacementCheckpoint = placementCheckpoint };
        }

        if (payload.AllowFormatChange) {
            // The user's explicit "import anyway": genuine upgrades may replace the owned file across
            // formats. The dangerous-file hold below still applies — consent to a format change is not
            // consent to import an executable payload.
            import = import with { AllowFormatChange = true };
        }

        var payloadFiles = string.IsNullOrWhiteSpace(import.ContentPath)
            ? []
            : payloads.Read(import.ContentPath)?.Files.Select(file => file.RelativePath).ToArray() ?? [];

        // The dangerous-file hold runs before ANY engine: a release whose payload carries an executable
        // (the classic fake-release .scr) is never imported automatically and never silently skipped —
        // it waits, visibly, for the user to review, blocklist, or import manually. Deliberately NOT
        // bypassed by a manual retry.
        if (DangerousFileDetection.FindDangerousFile(payloadFiles) is { } dangerous) {
            logger.LogWarning("AcquisitionImport: dangerous file {File} held for acquisition {Id}", dangerous, payload.AcquisitionId);
            var holdMessage = $"The download contains a potentially dangerous file (\"{Path.GetFileName(dangerous)}\") and was not imported. Review it, or block this release and search again.";
            await acquisitions.SetStatusAsync(payload.AcquisitionId, AcquisitionStatus.ManualImportRequired, holdMessage, cancellationToken);
            await RecordImportFailedAsync(import, holdMessage, cancellationToken);
            return;
        }

        // The wrong-content hold: the downloaded files must not contradict the work this acquisition is
        // for — otherwise a mislabeled release would be renamed into the expected work's folder, masking
        // the mismatch forever. Skipped for the user's own picks (manual release queue, uploaded torrent)
        // and for a manual retry-import — reviewing and clicking "import anyway" is the override.
        // A durable placement checkpoint was created only after the ORIGINAL complete payload passed this
        // validation. Move-mode resumes intentionally see a partial download directory, so re-validating
        // that remainder can manufacture a false wrong-season conflict and strand a valid checkpoint.
        if (import.TvImportCheckpoint is null
            && import.ImportPlacementCheckpoint is null
            && !payload.ManualRetry
            && await FindPayloadConflictAsync(payload.AcquisitionId, import, payloadFiles, cancellationToken) is { } conflict) {
            logger.LogWarning("AcquisitionImport: wrong content held for acquisition {Id}: {Conflict}", payload.AcquisitionId, conflict);
            var holdMessage = $"The download does not look like the expected content: {conflict} Review the files, import anyway, or block this release and search again.";
            await acquisitions.SetStatusAsync(payload.AcquisitionId, AcquisitionStatus.ManualImportRequired, holdMessage, cancellationToken);
            await RecordImportFailedAsync(import, holdMessage, cancellationToken);
            return;
        }

        var engine = engines.Find(import.Kind);
        if (engine is null) {
            await acquisitions.SetStatusAsync(
                payload.AcquisitionId,
                AcquisitionStatus.Downloaded,
                $"Downloaded. Automatic import for {import.Kind.ToCode()} acquisitions isn't available yet — the files remain in the download client.",
                cancellationToken);
            return;
        }

        try {
            await engine.ImportAsync(context, import, cancellationToken);
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            logger.LogWarning(ex, "AcquisitionImport: failed for acquisition {Id}", payload.AcquisitionId);
            await acquisitions.SetStatusAsync(payload.AcquisitionId, AcquisitionStatus.Failed, $"Import failed: {ex.Message}", CancellationToken.None);
            await RecordImportFailedAsync(import, $"Import failed: {ex.Message}", CancellationToken.None);
            throw;
        }
    }

    /// <summary>
    /// Finds a contradiction between the downloaded files and the expected work (wrong year, wrong
    /// season/episode) per <see cref="AcquisitionPayloadValidation"/>. Null — no hold — for manual picks
    /// (the user chose that exact release), non-video kinds, and payloads carrying no contrary evidence.
    /// The expected year comes from the search input, which resolves it from the linked entity's graph.
    /// </summary>
    private async Task<string?> FindPayloadConflictAsync(
        Guid acquisitionId, AcquisitionImportContext import, IReadOnlyList<string> payloadFiles, CancellationToken cancellationToken) {
        if (payloadFiles.Count == 0) {
            return null;
        }

        var selected = await acquisitions.GetSelectedReleaseAsync(acquisitionId, cancellationToken);
        if (selected?.ManualPick == true) {
            return null;
        }

        var input = await acquisitions.GetSearchInputAsync(acquisitionId, cancellationToken);
        return AcquisitionPayloadValidation.FindConflict(
            payloadFiles,
            import.Kind,
            input?.WorkTitle ?? import.Series ?? import.Title,
            input?.Year ?? import.Year,
            import.SeasonNumber,
            import.EpisodeNumber,
            selected is not null && TvReleaseTokens.NamesCompleteSeries(selected.Title));
    }

    /// <summary>Records a durable ImportFailed event (a manual-import hold or an import exception) against the acquisition. Best-effort.</summary>
    private Task RecordImportFailedAsync(AcquisitionImportContext import, string message, CancellationToken cancellationToken) =>
        history.SafeAddAsync(logger, new AcquisitionHistoryEntry(
            import.Id,
            EntityId: null,
            import.Kind,
            AcquisitionHistoryEvent.ImportFailed,
            import.Title,
            Message: message),
            cancellationToken);
}
