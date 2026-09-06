using Microsoft.Extensions.Logging;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Entities;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers;

/// <summary>
/// Applies a fully-transferred replacement child to the single-file owned copy it replaces — a book, movie,
/// or single TV episode. Automatic upgrades must still be a strict improvement; an explicitly reviewed
/// release or upload may replace at the user's direction. The handler atomically swaps the file (keeping the
/// original as a backup), updates owned quality when the payload identifies it, refreshes the entity via a
/// re-scan, cleans up the transfer source, and releases any monitor upgrade slot. The owned file is the only
/// library file mutated, and only after every safety gate passes.
/// </summary>
[JobDefinition(JobType.AcquisitionUpgradeReplace)]
public sealed class AcquisitionUpgradeReplaceJobHandler(
    IAcquisitionStore acquisitions,
    IMonitorStore monitors,
    IBookAcquisitionProfileStore profiles,
    IAtomicUpgradeCheckpointStore checkpoints,
    IAtomicUpgradeFiles files,
    IAcquisitionHistoryStore history,
    ILogger<AcquisitionUpgradeReplaceJobHandler> logger,
    IEntityLifecycleMutationLease? entityLifecycle = null,
    IMediaUpgradePayloadInspector? mediaUpgradeInspector = null) : IJobHandler {
    public async Task HandleAsync(JobContext context, CancellationToken cancellationToken) {
        var payload = AcquisitionJobPayload.Parse(context.Job.PayloadJson);
        var childId = payload.AcquisitionId;

        var target = await acquisitions.GetUpgradeReplaceTargetAsync(childId, cancellationToken);
        if (target is null) {
            logger.LogInformation("AcquisitionUpgradeReplace: {Child} is not a resolvable upgrade child; skipping.", childId);
            return;
        }

        if (target.ParentEntityId is not { } parentEntityId) {
            logger.LogWarning(
                "AcquisitionUpgradeReplace: {Child} has no stable parent Entity lifecycle; refusing to mutate owned files.",
                childId);
            return;
        }

        AtomicUpgradeCheckpoint? checkpoint = null;
        var prepared = false;
        async Task PrepareAsync(CancellationToken token) {
            var current = await acquisitions.GetUpgradeReplaceTargetAsync(childId, token);
            if (current is null || current.ParentId != target.ParentId || current.ParentEntityId != parentEntityId) return;
            try {
                checkpoint = await checkpoints.GetAsync(childId, token);
                if (checkpoint is not null) {
                    if (!await checkpoints.TryClaimAsync(childId, checkpoint, context.Job.Id, token)) return;
                    checkpoint = checkpoint with { ClaimJobId = context.Job.Id };
                    prepared = true;
                    return;
                }
                if (!await acquisitions.TryTransitionStatusAsync(childId,
                        [AcquisitionStatus.Downloaded, AcquisitionStatus.Importing], AcquisitionStatus.Importing,
                        "Preparing downloaded upgrade.", token)) return;
                if (current.InstalledUpgradePath is not null || current.ParentVideoSourceShared
                    || EntityKindRegistry.Describe(current.ParentKind).UpgradeMode is not (EntityUpgradeMode.AtomicBookFile or EntityUpgradeMode.AtomicMediaFile)
                    || string.IsNullOrWhiteSpace(current.ChildContentPath) || string.IsNullOrWhiteSpace(current.ParentFinalSourcePath)
                    || string.IsNullOrWhiteSpace(current.ChildSelectedTitle)) {
                    prepared = true;
                    return;
                }
                var plan = await files.PrepareAsync(current, token);
                var selected = await acquisitions.GetSelectedReleaseAsync(childId, token);
                if (selected is null) throw new IOException("The upgrade release information is missing.");
                checkpoint = await checkpoints.TryPrepareAsync(childId, context.Job.Id,
                    new AtomicUpgradePreparation(current.ParentId, parentEntityId, current.ParentKind, plan,
                        Path.GetFullPath(current.ChildContentPath), current.ChildClientItemId, selected), token);
                if (checkpoint is null) throw new IOException("The upgrade no longer has exclusive Source ownership and the original completed transfer.");
                prepared = true;
            } catch (InvalidDataException) {
                await acquisitions.TryHoldCorruptImportCheckpointAsync(childId, context.Job.Id,
                    ImportCheckpointLifecycle.CorruptCheckpointMessage, token);
            } catch (IOException exception) {
                await acquisitions.SetStatusAsync(childId, AcquisitionStatus.ManualImportRequired, exception.Message, token);
            }
        }
        // This lease must commit preparation before the next lease can mutate files. A rollback of
        // installation, quality, history, or readiness therefore leaves the exact recovery plan durable.
        if (!await ExecuteLeaseAsync(parentEntityId, PrepareAsync, cancellationToken) || !prepared) return;
        var installed = false;
        var executed = await ExecuteLeaseAsync(parentEntityId, async token => {
            var current = await acquisitions.GetUpgradeReplaceTargetAsync(childId, token);
            if (current is null || current.ParentId != target.ParentId || current.ParentEntityId != parentEntityId) return;
            if (checkpoint is not null && !await checkpoints.IsCurrentAsync(childId, checkpoint, token)) {
                if (await checkpoints.GetAsync(childId, token) != checkpoint) return;
                await acquisitions.SetStatusAsync(childId, AcquisitionStatus.ManualImportRequired,
                    "The prepared replacement no longer matches its Source or transfer ownership. Recovery files were retained.", token);
                return;
            }
            await HandleClaimedAsync(context, current, childId, checkpoint, token);
            installed = (await acquisitions.GetUpgradeReplaceTargetAsync(childId, token))?.InstalledUpgradePath is not null;
        }, cancellationToken);
        if (executed && installed && checkpoint is not null) {
            try {
                await files.CompleteAsync(checkpoint, CancellationToken.None);
            } catch (Exception exception) {
                logger.LogWarning(exception, "Installed upgrade retained; incoming evidence cleanup failed for acquisition {Child}.", childId);
            }
        }
    }

    private Task<bool> ExecuteLeaseAsync(Guid entityId, Func<CancellationToken, Task> mutation, CancellationToken token) =>
        entityLifecycle is null ? monitors.ExecuteIfActiveEntityMutationAsync(entityId, mutation, token)
            : entityLifecycle.ExecuteAsync(entityId, mutation, token);

    /// <summary>Validates and applies an upgrade after the stable Entity lifecycle has been leased.</summary>
    private async Task HandleClaimedAsync(
        JobContext context,
        UpgradeReplaceTarget target,
        Guid childId,
        AtomicUpgradeCheckpoint? checkpoint,
        CancellationToken cancellationToken) {

        if (target.ParentVideoSourceShared) {
            await acquisitions.SetStatusAsync(childId, AcquisitionStatus.ManualImportRequired,
                "The owned video now serves multiple entities. A coverage-aware import is required; both files were preserved.",
                cancellationToken);
            return;
        }

        if (target.InstalledUpgradePath is { } installedPath) {
            // This receipt proves that the installation, quality update, and reconciliation enqueue
            // committed together. A later readiness failure resumes processing, not another swap of
            // the already consumed download. Pre-commit filesystem interruptions need their own recovery.
            if (!target.InstalledUpgradeSourceCurrent || !File.Exists(installedPath)
                || new FileInfo(installedPath).Length == 0) {
                await acquisitions.SetStatusAsync(childId, AcquisitionStatus.ManualImportRequired,
                    "The installed upgrade no longer matches an available owned Source. Review its current file binding; no replacement or cleanup was attempted.",
                    cancellationToken);
                return;
            }
            await FinishAsync(context, target, childId, cancellationToken);
            return;
        }

        if (string.IsNullOrWhiteSpace(target.ParentFinalSourcePath) || string.IsNullOrWhiteSpace(target.ChildContentPath)) {
            await AbortAsync(childId, "The owned book location or the upgrade download path is unknown.", cancellationToken);
            return;
        }

        // The downloaded child must carry the release it grabbed; without it we cannot judge the upgrade.
        if (string.IsNullOrWhiteSpace(target.ChildSelectedTitle)) {
            await AbortAsync(childId, "The upgrade release information is missing.", cancellationToken);
            return;
        }

        if (checkpoint is null && EntityKindRegistry.Describe(target.ParentKind).UpgradeMode
                is EntityUpgradeMode.AtomicMediaFile or EntityUpgradeMode.AtomicBookFile) {
            await acquisitions.SetStatusAsync(childId, AcquisitionStatus.Downloaded,
                "The payload or ownership changed during preparation; a fresh replacement plan is required.", cancellationToken);
            return;
        }

        OwnedFileReplaceResult? recovered = null;
        if (checkpoint is not null) {
            var recovery = await files.RecoverAsync(checkpoint, cancellationToken);
            if (recovery.HoldReason is { } reason) {
                await acquisitions.SetStatusAsync(childId, AcquisitionStatus.ManualImportRequired, reason, cancellationToken);
                return;
            }
            recovered = recovery.Installed;
        }

        // The parent definition owns the destructive replacement contract. Never infer Book merely because
        // a kind is absent from the media ladder: structural and multi-file kinds must use family import.
        switch (EntityKindRegistry.Describe(target.ParentKind).UpgradeMode) {
            case EntityUpgradeMode.AtomicMediaFile:
                await HandleMediaAsync(context, target, childId, checkpoint!, recovered, cancellationToken);
                break;
            case EntityUpgradeMode.AtomicBookFile:
                await HandleBookAsync(context, target, childId, checkpoint!, recovered, cancellationToken);
                break;
            default:
                await AbortAsync(
                    childId,
                    $"Entity kind '{target.ParentKind.ToCode()}' does not support atomic file replacement.",
                    cancellationToken);
                break;
        }
    }

    /// <summary>The book replace path: source/format Pareto dominance, an in-place same-extension book-file swap, and a book re-scan.</summary>
    private async Task HandleBookAsync(JobContext context, UpgradeReplaceTarget target, Guid childId, AtomicUpgradeCheckpoint checkpoint, OwnedFileReplaceResult? recovered, CancellationToken cancellationToken) {
        // Last gate before touching the file: re-confirm the downloaded release still strictly beats the
        // parent's CURRENT owned quality (it may have changed since the search). The format axis is re-checked
        // against the actual file inside the replacer; here we guard overall dominance from the release title.
        var candidate = BookFormatDetection.DetectQuality(target.ChildSelectedTitle!);
        if (recovered is null && !target.ChildManualPick && !candidate.StrictlyDominates(target.ParentOwnedQuality)) {
            if (!await checkpoints.TryClearAsync(childId, checkpoint, cancellationToken))
                throw new IOException("The rejected replacement preparation changed before it could be released.");
            await AbortAsync(childId, "The downloaded release is no longer an upgrade over the current copy.", cancellationToken);
            return;
        }

        var result = recovered ?? await files.ReplaceAsync(checkpoint, cancellationToken);
        if (!result.Succeeded) {
            await acquisitions.SetStatusAsync(childId, AcquisitionStatus.ManualImportRequired, result.FailureReason ?? "The upgrade could not be applied.", cancellationToken);
            return;
        }

        // Publish the installed path, owned quality, and reconciliation together in the lifecycle commit.
        // Source comes from the selected release and format from the installed file. Once committed,
        // the receipt lets interrupted readiness resume without swapping the consumed payload again.
        var detectedSource = BookFormatDetection.DetectSource(target.ChildSelectedTitle!);
        var newOwned = new BookQualityRank(
            target.ChildManualPick && detectedSource == BookSourceTier.Unknown
                ? target.ParentOwnedQuality.Source
                : detectedSource,
            result.NewFormat);
        await acquisitions.SetFinalSourcePathAsync(childId, result.SwappedPath!, cancellationToken);
        await acquisitions.UpdateOwnedQualityAsync(target.ParentId, newOwned, cancellationToken);
        await RecordUpgradedAsync(
            target,
            newCode: $"{newOwned.Source.ToCode()}/{newOwned.Format.ToCode()}",
            oldQuality: $"{target.ParentOwnedQuality.Source.ToCode()}/{target.ParentOwnedQuality.Format.ToCode()}",
            newQuality: $"{newOwned.Source.ToCode()}/{newOwned.Format.ToCode()}",
            cancellationToken);
        await FinishAsync(context, target, childId, cancellationToken);
        if (!await checkpoints.TryClearAsync(childId, checkpoint, cancellationToken))
            throw new IOException("The replacement checkpoint changed before installation could commit.");
    }

    /// <summary>The movie/single-episode replace path: ladder-position (or same-quality revision) dominance, an in-place same-extension video-file swap, and a library re-scan.</summary>
    private async Task HandleMediaAsync(JobContext context, UpgradeReplaceTarget target, Guid childId, AtomicUpgradeCheckpoint checkpoint, OwnedFileReplaceResult? recovered, CancellationToken cancellationToken) {
        // Re-confirm the downloaded release still beats the parent's CURRENT owned copy (it may have changed
        // since the search) before touching the file: a strictly higher ladder position, OR the same position
        // with a strictly higher PROPER/REPACK revision or custom-format score (the same accept rules the
        // search's upgrade gate used). Re-scoring against the parent's profile keeps the gate honest even if
        // the profile's formats changed since the search.
        var rules = await profiles.GetRulesAsync(target.ParentProfileId, target.ParentKind, cancellationToken);
        var ownedPosition = MediaQualityLadder.PositionOf(target.ParentKind, target.ParentOwnedMediaQuality);
        var (candidateCode, candidatePosition) = MediaQualityLadder.Detect(target.ParentKind, target.ChildSelectedTitle!);
        var candidateRevision = ReleaseRevisionDetection.Detect(target.ChildSelectedTitle!);
        var candidateFormatScore = CustomFormatEvaluation.Score(target.ChildSelectedTitle!, rules);
        var higherQuality = candidatePosition > ownedPosition;
        var sameQualityBetterRevision = candidatePosition == ownedPosition && candidateRevision > target.ParentOwnedMediaRevision;
        var sameQualityBetterFormatScore = candidatePosition == ownedPosition
            && rules.CutoffFormatScore is { } cutoff
            && target.ParentOwnedFormatScore < cutoff
            && candidateFormatScore > target.ParentOwnedFormatScore;

        MediaUpgradePayloadInspection? inspection = null;
        if (recovered is null && !target.ChildManualPick && mediaUpgradeInspector is not null) {
            inspection = await mediaUpgradeInspector.InspectAsync(
                target.ParentFinalSourcePath!,
                target.ChildContentPath!,
                cancellationToken);
        }

        if (recovered is null && !target.ChildManualPick) {
            if (VideoPayloadProfileValidation.ValidateProfile(candidateCode, inspection?.CandidateAudioLanguages, rules) is { } profileHold) {
                await acquisitions.SetStatusAsync(childId, AcquisitionStatus.ManualImportRequired, profileHold, cancellationToken);
                return;
            }
            if (mediaUpgradeInspector is not null
                && (inspection is not { OwnedDurationSeconds: > 0, CandidateDurationSeconds: > 0 }
                    || !double.IsFinite(inspection.OwnedDurationSeconds.Value)
                    || !double.IsFinite(inspection.CandidateDurationSeconds.Value))) {
                await acquisitions.SetStatusAsync(
                    childId,
                    AcquisitionStatus.ManualImportRequired,
                    "The owned and downloaded video could not both be inspected with a reliable runtime. Review the replacement; both files were preserved.",
                    cancellationToken);
                return;
            }

            if (inspection is { OwnedDurationSeconds: > 0, CandidateDurationSeconds: >= 0 }
                && double.IsFinite(inspection.OwnedDurationSeconds.Value)
                && double.IsFinite(inspection.CandidateDurationSeconds.Value)
                && inspection.CandidateDurationSeconds < inspection.OwnedDurationSeconds * VideoPayloadProfileValidation.MinimumAutomaticRuntimeRatio) {
                await acquisitions.SetStatusAsync(
                    childId,
                    AcquisitionStatus.ManualImportRequired,
                    "The downloaded video is substantially shorter than the owned copy. Review whether it is a complete replacement or a different edition; both files were preserved.",
                    cancellationToken);
                return;
            }

            var comparableOwnedLabel = inspection is null
                || MediaQualityLadder.VideoResolutionTierOf(target.ParentOwnedMediaQuality) == inspection.OwnedResolutionTier;
            if (inspection is { } facts) {
                higherQuality = facts.CandidateResolutionTier > facts.OwnedResolutionTier
                    || comparableOwnedLabel && higherQuality;
                sameQualityBetterRevision &= comparableOwnedLabel;
                sameQualityBetterFormatScore &= comparableOwnedLabel;
            }
            var actualResolutionDowngrade = inspection is { } measured
                && measured.CandidateResolutionTier < measured.OwnedResolutionTier;
            var claimedResolution = MediaQualityLadder.VideoResolutionTierOf(candidateCode);
            var payloadContradictsClaimedResolution = claimedResolution is { } claimed
                && inspection is { } inspected
                && inspected.CandidateResolutionTier < claimed;
            var sameQualityAddsSubtitles = (comparableOwnedLabel ? candidatePosition == ownedPosition
                    : inspection is { } comparable && comparable.CandidateResolutionTier == comparable.OwnedResolutionTier)
                && !target.ParentHasSubtitles
                && inspection is {
                    OwnedHasSubtitles: false,
                    CandidateHasSubtitles: true
                };

            if (actualResolutionDowngrade || payloadContradictsClaimedResolution) {
                await RejectDownloadedCandidateAsync(
                    context,
                    target,
                    childId,
                    checkpoint,
                    "The downloaded payload's measured resolution is lower than the owned copy or the release's claimed quality.",
                    cancellationToken);
                return;
            }

            if (!higherQuality && !sameQualityBetterRevision && !sameQualityBetterFormatScore && !sameQualityAddsSubtitles) {
                await RejectDownloadedCandidateAsync(
                    context,
                    target,
                    childId,
                    checkpoint,
                    "The downloaded payload did not prove an upgrade over the current copy.",
                    cancellationToken);
                return;
            }
        }

        // Video has no book format tier; the replacer's format-tier guard is a pass-through for this kind. The
        // same-extension rule still holds (an mkv → mp4 swap is refused, for entity/progress continuity).
        var result = recovered ?? await files.ReplaceAsync(checkpoint, cancellationToken);
        if (!result.Succeeded) {
            await acquisitions.SetStatusAsync(childId, AcquisitionStatus.ManualImportRequired, result.FailureReason ?? "The upgrade could not be applied.", cancellationToken);
            return;
        }

        // Commit the installation receipt with the parent's quality, revision, and custom-format score.
        // Advancing all three together lets a same-quality proper or format-score upgrade settle;
        // transfer cleanup waits for the subsequent required-readiness finalizer.
        var resolvedCode = target.ChildManualPick && candidatePosition == 0
            ? target.ParentOwnedMediaQuality ?? VideoQuality.Unknown.ToCode()
            : candidateCode;
        var resolvedRevision = target.ChildManualPick && candidatePosition == 0
            ? target.ParentOwnedMediaRevision
            : candidateRevision;
        var resolvedFormatScore = target.ChildManualPick && candidatePosition == 0
            ? target.ParentOwnedFormatScore
            : candidateFormatScore;
        await acquisitions.SetFinalSourcePathAsync(childId, result.SwappedPath!, cancellationToken);
        await acquisitions.UpdateOwnedMediaQualityAsync(target.ParentId, resolvedCode, resolvedRevision, resolvedFormatScore, cancellationToken);
        await RecordUpgradedAsync(
            target,
            newCode: resolvedCode,
            oldQuality: string.IsNullOrWhiteSpace(target.ParentOwnedMediaQuality) ? VideoQuality.Unknown.ToCode() : target.ParentOwnedMediaQuality!,
            newQuality: resolvedCode,
            cancellationToken);
        await FinishAsync(context, target, childId, cancellationToken);
        if (!await checkpoints.TryClearAsync(childId, checkpoint, cancellationToken))
            throw new IOException("The replacement checkpoint changed before installation could commit.");
    }

    /// <summary>
    /// Shared post-swap completion: append exact Entity reconciliation and let its required-readiness
    /// finalizer preserve cleanup ownership, release the monitor slot, and remove the consumed child.
    /// Keep the transfer and remaining payload until required readiness succeeds; deleting them during
    /// this lifecycle transaction would be irreversible if reconciliation or its database commit fails.
    /// </summary>
    private async Task FinishAsync(JobContext context, UpgradeReplaceTarget target, Guid childId, CancellationToken cancellationToken) {
        if (target.ParentEntityId is { } entityId) {
            await context.EnqueueIfNeededAsync(
                EnqueueJobRequest.ForEntity(
                    JobType.ReconcileEntity,
                    target.ParentKind,
                    entityId.ToString(),
                    target.ChildSelectedTitle,
                    AcquisitionFinalizeJobPayload.CreateUpgrade(
                        childId,
                        target.ParentId,
                        "Upgrade ready").ToJson()),
                cancellationToken);
        }
        logger.LogInformation(
            "AcquisitionUpgradeReplace: replacement for acquisition {Parent} is awaiting required Entity readiness via child {Child}.",
            target.ParentId,
            childId);
    }

    /// <summary>Records the upgrade attempt as failed: marks the child failed (so it stays visible) and releases the monitor's slot, counting it as barren.</summary>
    private async Task AbortAsync(Guid childId, string reason, CancellationToken cancellationToken) {
        logger.LogInformation("AcquisitionUpgradeReplace: not applying child {Child}: {Reason}", childId, reason);
        if (!await acquisitions.TryTransitionStatusAsync(
                childId,
                [AcquisitionStatus.Importing],
                AcquisitionStatus.Failed,
                reason,
                cancellationToken)) {
            return;
        }
        await monitors.ResolveUpgradeChildAsync(childId, succeeded: false, cancellationToken);
    }

    /// <summary>
    /// Commits rejection without touching the owned file or download. The existing failed-download
    /// handler owns durable cleanup, blocklisting, and selection of the next candidate after this commit.
    /// </summary>
    private async Task RejectDownloadedCandidateAsync(
        JobContext context,
        UpgradeReplaceTarget target,
        Guid childId,
        AtomicUpgradeCheckpoint checkpoint,
        string reason,
        CancellationToken cancellationToken) {
        if (!await checkpoints.TryClearAsync(childId, checkpoint, cancellationToken))
            throw new IOException("The rejected replacement preparation changed before cleanup.");
        logger.LogInformation("AcquisitionUpgradeReplace: rejecting inspected child {Child}: {Reason}", childId, reason);
        if (!await acquisitions.TryTransitionStatusAsync(
                childId,
                [AcquisitionStatus.Importing],
                AcquisitionStatus.Failed,
                reason,
                cancellationToken)) {
            return;
        }

        var selected = await acquisitions.GetSelectedReleaseAsync(childId, cancellationToken);
        await context.EnqueueIfNeededAsync(
            new EnqueueJobRequest(
                JobType.AcquisitionFailedHandle,
                PayloadJson: AcquisitionFailedPayload.Serialize(
                    childId,
                    BlocklistReason.NotAnUpgrade,
                    reason,
                    selected),
                TargetEntityId: childId.ToString(),
                TargetLabel: "Recover rejected upgrade",
                Origin: JobGraphOrigin.Interactive),
            cancellationToken);
    }

    /// <summary>
    /// Records a durable Upgraded event against the PARENT acquisition (the owned copy that was replaced),
    /// carrying the new quality code and an old→new summary in the message. Best-effort: a history hiccup
    /// must never undo the applied upgrade. The parent's title/kind/entity come from its search input.
    /// </summary>
    private async Task RecordUpgradedAsync(UpgradeReplaceTarget target, string newCode, string oldQuality, string newQuality, CancellationToken cancellationToken) {
        var input = await acquisitions.GetSearchInputAsync(target.ParentId, cancellationToken);
        if (input is null) {
            return;
        }

        await history.SafeAddAsync(logger, new AcquisitionHistoryEntry(
            target.ParentId,
            input.EntityId,
            input.Kind,
            AcquisitionHistoryEvent.Upgraded,
            input.Title,
            ReleaseTitle: target.ChildSelectedTitle,
            QualityCode: newCode,
            Message: $"Upgraded {oldQuality} → {newQuality}"),
            cancellationToken);
    }

}
