using Microsoft.Extensions.Logging;
using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers;

/// <summary>
/// Applies a fully-downloaded upgrade child to the single-file owned copy it upgrades — a book, or a movie
/// or single TV episode: re-confirms the downloaded release is still a strict improvement (in the parent
/// kind's quality vocabulary), atomically swaps the better file in for the owned one (keeping the original as
/// a backup), updates the parent's owned quality, refreshes the entity via a re-scan, cleans up the torrent,
/// and releases the monitor's upgrade slot. The owned file is the only library file mutated, and only after
/// every gate passes; any failure leaves the owned copy untouched and surfaces the download for manual
/// handling.
/// </summary>
public sealed class AcquisitionUpgradeReplaceJobHandler(
    IAcquisitionStore acquisitions,
    IMonitorStore monitors,
    IBookAcquisitionProfileStore profiles,
    IOwnedFileReplacer replacer,
    IDownloadClientConfigStore downloadClients,
    IDownloadClientFactory clients,
    IAcquisitionHistoryStore history,
    ILogger<AcquisitionUpgradeReplaceJobHandler> logger) : IJobHandler {
    public JobType Type => JobType.AcquisitionUpgradeReplace;

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

        // Revalidate and execute the complete swap while holding the direct Active Entity monitor. Managed
        // Delete files and unmonitor contend on the same monitor/Entity boundary, so a stale queued replace
        // job either completes before their preflight is revalidated or performs no filesystem mutation.
        var accepted = await monitors.ExecuteIfActiveEntityMutationAsync(
            parentEntityId,
            async leaseCancellationToken => {
                var current = await acquisitions.GetUpgradeReplaceTargetAsync(
                    childId,
                    leaseCancellationToken);
                if (current is null
                    || current.ParentId != target.ParentId
                    || current.ParentEntityId != parentEntityId) {
                    return;
                }
                if (!await acquisitions.TryTransitionStatusAsync(
                        childId,
                        [AcquisitionStatus.Downloaded, AcquisitionStatus.Importing],
                        AcquisitionStatus.Importing,
                        "Applying downloaded upgrade.",
                        leaseCancellationToken)) {
                    return;
                }
                await HandleClaimedAsync(context, current, childId, leaseCancellationToken);
            },
            cancellationToken);
        if (!accepted) {
            logger.LogInformation(
                "AcquisitionUpgradeReplace: {Child} lost its active Entity lifecycle lease; skipping.",
                childId);
        }
    }

    /// <summary>Validates and applies an upgrade after the stable Entity lifecycle has been leased.</summary>
    private async Task HandleClaimedAsync(
        JobContext context,
        UpgradeReplaceTarget target,
        Guid childId,
        CancellationToken cancellationToken) {

        if (string.IsNullOrWhiteSpace(target.ParentFinalSourcePath) || string.IsNullOrWhiteSpace(target.ChildContentPath)) {
            await AbortAsync(childId, "The owned book location or the upgrade download path is unknown.", cancellationToken);
            return;
        }

        // The downloaded child must carry the release it grabbed; without it we cannot judge the upgrade.
        if (string.IsNullOrWhiteSpace(target.ChildSelectedTitle)) {
            await AbortAsync(childId, "The upgrade release information is missing.", cancellationToken);
            return;
        }

        // The parent kind decides which quality vocabulary the dominance check and the owned-quality update
        // speak. A movie or single episode compares ladder positions; every other kind uses the book path.
        if (MediaQualityLadder.IsUpgradeCapableKind(target.ParentKind)) {
            await HandleMediaAsync(context, target, childId, cancellationToken);
        } else {
            await HandleBookAsync(context, target, childId, cancellationToken);
        }
    }

    /// <summary>The book replace path: source/format Pareto dominance, an in-place same-extension book-file swap, and a book re-scan.</summary>
    private async Task HandleBookAsync(JobContext context, UpgradeReplaceTarget target, Guid childId, CancellationToken cancellationToken) {
        // Last gate before touching the file: re-confirm the downloaded release still strictly beats the
        // parent's CURRENT owned quality (it may have changed since the search). The format axis is re-checked
        // against the actual file inside the replacer; here we guard overall dominance from the release title.
        var candidate = BookFormatDetection.DetectQuality(target.ChildSelectedTitle!);
        if (!candidate.StrictlyDominates(target.ParentOwnedQuality)) {
            await AbortAsync(childId, "The downloaded release is no longer an upgrade over the current copy.", cancellationToken);
            return;
        }

        var result = await replacer.ReplaceAsync(target.ParentFinalSourcePath!, target.ChildContentPath!, target.ParentOwnedQuality.Format, cancellationToken, EntityKind.Book);
        if (!result.Succeeded) {
            await AbortAsync(childId, result.FailureReason ?? "The upgrade could not be applied.", cancellationToken);
            return;
        }

        // The parent now owns the better file. Record its new owned quality FIRST (source from the release
        // title, format from the installed file): this is the load-bearing bookkeeping for the upgrade loop, so
        // doing it before the best-effort cleanup means a crash mid-completion leaves the loop seeing the
        // upgrade rather than retrying it. Then refresh metadata via a re-scan, clean up the torrent, release
        // the upgrade slot (counting the attempt), and remove the now-consumed child acquisition.
        var newOwned = new BookQualityRank(BookFormatDetection.DetectSource(target.ChildSelectedTitle!), result.NewFormat);
        await acquisitions.UpdateOwnedQualityAsync(target.ParentId, newOwned, cancellationToken);
        await RecordUpgradedAsync(
            target,
            newCode: $"{newOwned.Source.ToCode()}/{newOwned.Format.ToCode()}",
            oldQuality: $"{target.ParentOwnedQuality.Source.ToCode()}/{target.ParentOwnedQuality.Format.ToCode()}",
            newQuality: $"{newOwned.Source.ToCode()}/{newOwned.Format.ToCode()}",
            cancellationToken);
        await FinishAsync(context, target, childId, JobType.ScanBook, "Upgraded book scan", cancellationToken);
    }

    /// <summary>The movie/single-episode replace path: ladder-position (or same-quality revision) dominance, an in-place same-extension video-file swap, and a library re-scan.</summary>
    private async Task HandleMediaAsync(JobContext context, UpgradeReplaceTarget target, Guid childId, CancellationToken cancellationToken) {
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
        if (!higherQuality && !sameQualityBetterRevision && !sameQualityBetterFormatScore) {
            await AbortAsync(childId, "The downloaded release is no longer an upgrade over the current copy.", cancellationToken);
            return;
        }

        // Video has no book format tier; the replacer's format-tier guard is a pass-through for this kind. The
        // same-extension rule still holds (an mkv → mp4 swap is refused, for entity/progress continuity).
        var result = await replacer.ReplaceAsync(target.ParentFinalSourcePath!, target.ChildContentPath!, BookFormatTier.Unknown, cancellationToken, target.ParentKind);
        if (!result.Succeeded) {
            await AbortAsync(childId, result.FailureReason ?? "The upgrade could not be applied.", cancellationToken);
            return;
        }

        // Record the parent's new owned ladder code, revision, AND custom-format score (all from the child
        // release) FIRST — the load-bearing bookkeeping — before the best-effort cleanup, mirroring the book
        // path. Advancing the revision and format score alongside the code is what lets a same-quality proper
        // or format-score upgrade settle instead of re-firing.
        await acquisitions.UpdateOwnedMediaQualityAsync(target.ParentId, candidateCode, candidateRevision, candidateFormatScore, cancellationToken);
        await RecordUpgradedAsync(
            target,
            newCode: candidateCode,
            oldQuality: string.IsNullOrWhiteSpace(target.ParentOwnedMediaQuality) ? VideoQuality.Unknown.ToCode() : target.ParentOwnedMediaQuality!,
            newQuality: candidateCode,
            cancellationToken);
        await FinishAsync(context, target, childId, JobType.ScanLibrary, "Upgraded video scan", cancellationToken);
    }

    /// <summary>Shared post-swap completion: release the monitor's slot (counting the attempt), re-scan, clean up the torrent, and remove the consumed child.</summary>
    private async Task FinishAsync(JobContext context, UpgradeReplaceTarget target, Guid childId, JobType scanJob, string scanLabel, CancellationToken cancellationToken) {
        await monitors.ResolveUpgradeChildAsync(childId, succeeded: true, cancellationToken);
        await context.EnqueueIfNeededAsync(new EnqueueJobRequest(scanJob, TargetLabel: scanLabel), cancellationToken);
        await RemoveTorrentAsync(target, cancellationToken);
        await acquisitions.DeleteAsync(childId, cancellationToken);
        logger.LogInformation("AcquisitionUpgradeReplace: upgraded acquisition {Parent} via child {Child}.", target.ParentId, childId);
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

    private async Task RemoveTorrentAsync(UpgradeReplaceTarget target, CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(target.ChildClientItemId)) {
            return;
        }

        var client = target.ChildDownloadClientConfigId is { } id
            ? await downloadClients.GetAsync(id, cancellationToken) ?? await downloadClients.GetDefaultAsync(cancellationToken)
            : await downloadClients.GetDefaultAsync(cancellationToken);
        if (client is null) {
            return;
        }

        try {
            var connection = new DownloadClientConnection(client.Id, client.Kind, client.BaseUrl, client.Username, client.Password, client.Category, client.ApiKey);
            await clients.Get(client.Kind).RemoveAsync(connection, target.ChildClientItemId, deleteData: true, cancellationToken);
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            // The book is already upgraded; a torrent-cleanup failure must not undo that.
            logger.LogWarning(ex, "AcquisitionUpgradeReplace: failed to remove the upgrade torrent for parent {Parent}.", target.ParentId);
        }
    }
}
