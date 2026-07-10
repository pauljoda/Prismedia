using Microsoft.Extensions.Logging;
using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers;

/// <summary>
/// Recovers from a failed download. Blocklists the release that failed (so neither this recovery pass nor
/// any future search re-grabs it), then — when the profile has auto-redownload enabled — grabs the
/// next-best accepted candidate that is not itself blocklisted. With auto-redownload off, or when no
/// alternative remains, the acquisition is left <see cref="AcquisitionStatus.Failed"/> for manual retry.
/// </summary>
public sealed class AcquisitionFailedHandleJobHandler(
    IAcquisitionStore acquisitions,
    IAcquisitionBlocklistStore blocklist,
    IBookAcquisitionProfileStore profiles,
    IAcquisitionQueueService queueService,
    IAcquisitionHistoryStore history,
    ILogger<AcquisitionFailedHandleJobHandler> logger) : IJobHandler {
    public JobType Type => JobType.AcquisitionFailedHandle;

    public async Task HandleAsync(JobContext context, CancellationToken cancellationToken) {
        var payload = AcquisitionFailedPayload.Parse(context.Job.PayloadJson);
        var acquisitionId = payload.AcquisitionId;
        var selected = payload.Selected;
        var failureMessage = payload.Message ?? "Download failed.";

        // The queued job is only evidence about the exact release/status snapshot that produced it. Claim
        // that snapshot before history, blocklisting, or requeue side effects. A user cancellation or newer
        // selected release that wins first makes this stale job a no-op.
        if (!await acquisitions.TryClaimFailedRecoveryAsync(
                acquisitionId,
                [AcquisitionStatus.Queued, AcquisitionStatus.Downloading, AcquisitionStatus.Failed],
                selected,
                failureMessage,
                cancellationToken)) {
            logger.LogInformation(
                "AcquisitionFailedHandle: acquisition {AcquisitionId} changed before recovery was claimed; skipping stale work.",
                acquisitionId);
            return;
        }

        // The search input backs every history event here (title/kind/entity). Read it once up front so both
        // the no-snapshot early return and the blocklist path can record against it.
        var input = await acquisitions.GetSearchInputAsync(acquisitionId, cancellationToken);
        await RecordFailedAsync(acquisitionId, input, AcquisitionHistoryEvent.DownloadFailed, selected?.Title, selected?.IndexerName, failureMessage, cancellationToken);

        if (selected is null) {
            // No snapshot of what was downloading (e.g. a manually-uploaded torrent), so there is nothing
            // specific to blocklist and re-grabbing blindly could loop on the same bad release. Leave it failed.
            logger.LogDebug("AcquisitionFailedHandle: no selected-release snapshot for {AcquisitionId}; leaving failed.", acquisitionId);
            await KeepFailedIfOwnedAsync(acquisitionId, failureMessage, cancellationToken);
            return;
        }

        await blocklist.AddAsync(
            new BlocklistAddRequest(selected.Identity, payload.Reason, selected.Title, selected.IndexerName, selected.InfoHash, acquisitionId, payload.Message),
            cancellationToken);
        await RecordFailedAsync(acquisitionId, input, AcquisitionHistoryEvent.Blocklisted, selected.Title, selected.IndexerName, $"Blocklisted ({payload.Reason.ToCode()}).", cancellationToken);

        if (!await profiles.GetAutoRedownloadAsync(input?.ProfileId, input?.Kind ?? EntityKind.Book, cancellationToken)) {
            // Release blocklisted, but the profile leaves recovery to the user. This handler owns the
            // terminal Failed transition (the monitor only enqueues), so record it here.
            await KeepFailedIfOwnedAsync(acquisitionId, failureMessage, cancellationToken);
            return;
        }

        var blocklisted = await blocklist.GetIdentitiesAsync(cancellationToken);
        var candidates = await acquisitions.ListAcceptedCandidatesAsync(acquisitionId, cancellationToken);
        var next = candidates.FirstOrDefault(candidate => !blocklisted.Contains(candidate.Identity));
        if (next is null) {
            await KeepFailedIfOwnedAsync(
                acquisitionId,
                "Download failed and no alternative release is available.",
                cancellationToken);
            return;
        }

        try {
            await queueService.QueueAsync(
                acquisitionId,
                next.CandidateId,
                cancellationToken,
                requiredStatus: AcquisitionStatus.Failed);
            logger.LogInformation("AcquisitionFailedHandle: blocklisted the failed release and re-queued the next-best candidate for {AcquisitionId}.", acquisitionId);
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            await KeepFailedIfOwnedAsync(
                acquisitionId,
                $"Auto-redownload failed: {ex.Message}",
                cancellationToken);
        }
    }

    private Task<bool> KeepFailedIfOwnedAsync(
        Guid acquisitionId,
        string message,
        CancellationToken cancellationToken) =>
        acquisitions.TryTransitionStatusAsync(
            acquisitionId,
            [AcquisitionStatus.Failed],
            AcquisitionStatus.Failed,
            message,
            cancellationToken);

    /// <summary>
    /// Records a durable failure event (DownloadFailed or Blocklisted) against the acquisition. Best-effort:
    /// a history hiccup must never break failure recovery. Title/kind/entity come from the acquisition's
    /// search input when it still exists; otherwise the release title stands in and the kind defaults to book.
    /// </summary>
    private Task RecordFailedAsync(
        Guid acquisitionId, AcquisitionSearchInput? input, AcquisitionHistoryEvent kind,
        string? releaseTitle, string? indexerName, string message, CancellationToken cancellationToken) =>
        history.SafeAddAsync(logger, new AcquisitionHistoryEntry(
            acquisitionId,
            input?.EntityId,
            input?.Kind ?? EntityKind.Book,
            kind,
            input?.Title ?? releaseTitle ?? "(removed acquisition)",
            releaseTitle,
            indexerName,
            Message: message),
            cancellationToken);
}
