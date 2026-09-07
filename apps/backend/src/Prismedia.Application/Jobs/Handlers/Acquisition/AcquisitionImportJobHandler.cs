using Microsoft.Extensions.Logging;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Entities;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers;

/// <summary>
/// Imports a completed acquisition by dispatching to the media kind's <see cref="IAcquisitionImportEngine"/>
/// (books, movies, music — each owns its planning, placement, hint, and scan chaining). A payload carrying
/// an executable or dangerous file is held for manual review before any engine runs. A reviewed mapping
/// may explicitly select safe media from that payload; dangerous paths themselves remain blocked. A kind
/// with no registered engine stays Downloaded (files intact in the client) with an honest status instead
/// of being pushed through the wrong pipeline.
/// </summary>
[JobDefinition(JobType.AcquisitionImport)]
public sealed class AcquisitionImportJobHandler(
    IAcquisitionStore acquisitions,
    IAcquisitionImportEngineFactory engines,
    IDownloadPayloadReader payloads,
    IAcquisitionHistoryStore history,
    ILogger<AcquisitionImportJobHandler> logger,
    IEntityLifecycleMutationLease? lifecycle = null,
    IImportTargetIndex? importTargets = null,
    TvAcquisitionImportPlanner? tvPlanner = null) : IJobHandler {
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
        // Old queued imports and explicit retries must preserve the atomic upgrade boundary.
        // Dispatch before claiming a normal import or allowing its engines to place any bytes.
        if (AcquisitionCompletionService.CompletionJobType(import.Kind, import.UpgradeOfAcquisitionId is not null,
                import.BookRendition) == JobType.AcquisitionUpgradeReplace) {
            if (payload.ManualRetry) {
                if (!await acquisitions.TryTransitionStatusAsync(payload.AcquisitionId,
                    [AcquisitionStatus.Downloaded, AcquisitionStatus.ManualImportRequired, AcquisitionStatus.Failed, AcquisitionStatus.AwaitingSelection],
                    AcquisitionStatus.Downloaded, "Retrying the downloaded upgrade with current ownership checks.", cancellationToken)) return;
            } else if (await acquisitions.GetStatusAsync(payload.AcquisitionId, cancellationToken)
                is not (AcquisitionStatus.Downloaded or AcquisitionStatus.Importing)) return;
            await context.EnqueueIfNeededAsync(new EnqueueJobRequest(JobType.AcquisitionUpgradeReplace,
                PayloadJson: AcquisitionJobPayload.Serialize(payload.AcquisitionId, payload.AllowFormatChange, manualRetry: payload.ManualRetry),
                TargetEntityId: payload.AcquisitionId.ToString(), TargetLabel: import.Title), cancellationToken);
            return;
        }
        import.EnsureCheckpointApplicability();

        TvImportCheckpoint? tvCheckpoint = null;
        ImportPlacementCheckpoint? placementCheckpoint = null;
        switch (import.CheckpointProtocol) {
            case AcquisitionCheckpointProtocol.Television:
                tvCheckpoint = import.TelevisionCheckpoint;
                break;
            case AcquisitionCheckpointProtocol.Placement:
                placementCheckpoint = import.PlacementCheckpoint;
                break;
            default:
                throw new InvalidOperationException(
                    $"Unknown acquisition checkpoint protocol '{import.CheckpointProtocol}'.");
        }
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

        switch (import.CheckpointProtocol) {
            case AcquisitionCheckpointProtocol.Television when tvCheckpoint is not null:
                tvCheckpoint = tvCheckpoint with { ClaimJobId = context.Job.Id };
                import = import with { TvImportCheckpoint = tvCheckpoint };
                break;
            case AcquisitionCheckpointProtocol.Placement when placementCheckpoint is not null:
                placementCheckpoint = placementCheckpoint with { ClaimJobId = context.Job.Id };
                import = import with { ImportPlacementCheckpoint = placementCheckpoint };
                break;
        }

        if (payload.AllowFormatChange) {
            // The user's explicit "import anyway": genuine upgrades may replace the owned file across
            // formats. The dangerous-file hold below still applies unless the user separately reviewed
            // exact safe media mappings.
            import = import with { AllowFormatChange = true };
        }
        if (payload.ManualFileMappings is { Count: > 0 } manualFileMappings) {
            import = import with { ManualFileMappings = manualFileMappings };
        }

        var payloadFiles = string.IsNullOrWhiteSpace(import.ContentPath)
            ? []
            : payloads.Read(import.ContentPath)?.Files.Select(file => file.RelativePath).ToArray() ?? [];

        // The dangerous-file hold runs before ANY engine: a release whose payload carries an executable
        // (the classic fake-release .scr) is never imported automatically and never silently skipped —
        // it waits, visibly, for the user to review or blocklist. An exact reviewed mapping is the only
        // override: the mapped paths must all be non-dangerous and the engine receives only those choices.
        // A generic manual retry never bypasses this gate.
        var hasReviewedSafeMappings = payload.ManualFileMappings is { Count: > 0 } reviewedMappings
            && reviewedMappings.All(mapping => !DangerousFileDetection.IsDangerousFile(mapping.SourceRelativePath));
        if (!hasReviewedSafeMappings
            && DangerousFileDetection.FindDangerousFile(payloadFiles) is { } dangerous) {
            logger.LogWarning("AcquisitionImport: dangerous file {File} held for acquisition {Id}", dangerous, payload.AcquisitionId);
            var holdMessage = $"The download contains a potentially dangerous file (\"{Path.GetFileName(dangerous)}\") and was not imported automatically. Review and map only verified media, or block this release and search again.";
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
        var hasDurableCheckpoint = import.CheckpointProtocol switch {
            AcquisitionCheckpointProtocol.Television => tvCheckpoint is not null,
            AcquisitionCheckpointProtocol.Placement => placementCheckpoint is not null,
            _ => throw new InvalidOperationException(
                $"Unknown acquisition checkpoint protocol '{import.CheckpointProtocol}'.")
        };
        if (!hasDurableCheckpoint
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
        var expectedSeason = import.SeasonNumber;
        if (tvPlanner is not null && import.CheckpointProtocol == AcquisitionCheckpointProtocol.Television
            && !string.IsNullOrWhiteSpace(import.FinalSourcePath)
            && (await acquisitions.GetTransferInfoAsync(acquisitionId, cancellationToken))?.ImportResult?.HasRetainedTvVideos() == true) {
            var plan = await tvPlanner.PlanAsync(import,
                new DownloadPayload(string.Empty, payloadFiles.Select(path => new ImportCandidateFile(path, 0)).ToArray()),
                null, null, cancellationToken);
            if (!plan.Plan.Blocked && plan.MonitoredExtras.Count > 0) {
                // A partial import may now contain only retained extras. The shared planner proves
                // their destination intent; keep the independent series/year check below.
                expectedSeason = null;
            }
        }
        var episodeTitles = importTargets is not null
            && import.EntityId is { } entityId
            && import.SeasonNumber is { } season
            && import.EpisodeNumber is not null
                ? await importTargets.GetSeasonEpisodeTitlesAsync(entityId, season, cancellationToken)
                : [];
        return AcquisitionPayloadValidation.FindConflict(
            payloadFiles,
            import.Kind,
            input?.WorkTitle ?? import.Series ?? import.Title,
            input?.Year ?? import.Year,
            expectedSeason,
            import.EpisodeNumber,
            selected is not null && TvReleaseTokens.NamesCompleteSeries(selected.Title),
            input?.Title,
            input?.AbsoluteEpisodeNumber,
            episodeTitles, input?.AlternativeWorkTitles ?? import.AlternativeWorkTitles);
    }

    /// <summary>Records a durable ImportFailed event (a manual-import hold or an import exception) against the acquisition. Best-effort.</summary>
    private Task RecordImportFailedAsync(AcquisitionImportContext import, string message, CancellationToken cancellationToken) =>
        history.SafeAddAsync(logger, new AcquisitionHistoryEntry(
            import.Id,
            import.EntityId,
            import.Kind,
            AcquisitionHistoryEvent.ImportFailed,
            import.Title,
            Message: message),
            cancellationToken);
}
