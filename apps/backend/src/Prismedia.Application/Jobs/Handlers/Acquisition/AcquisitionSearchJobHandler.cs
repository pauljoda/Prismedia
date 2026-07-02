using Microsoft.Extensions.Logging;
using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers;

/// <summary>
/// Runs the indexer release search for one acquisition in the background. Indexer searches against
/// Prowlarr routinely take tens of seconds, so this work is durable and off the request path: it moves
/// the acquisition to <see cref="AcquisitionStatus.Searching"/>, queries indexers, persists scored
/// candidates, and leaves it <see cref="AcquisitionStatus.AwaitingSelection"/> for review.
/// </summary>
public sealed class AcquisitionSearchJobHandler(
    IAcquisitionStore store,
    AcquisitionSearchRunner runner,
    IBookAcquisitionProfileStore profiles,
    AcquisitionQueueService queue,
    ILogger<AcquisitionSearchJobHandler> logger) : IJobHandler {
    public JobType Type => JobType.AcquisitionSearch;

    /// <summary>
    /// A search may only mutate an acquisition that is genuinely still seeking a release. A queued or
    /// in-flight grab, an imported book, or a cancelled request must be left alone — otherwise a stale
    /// monitor-enqueued search that ran after the state changed would reset the status, replace candidates,
    /// and (with auto-pick) delete and re-grab the live torrent. This is the execution-time counterpart to
    /// the monitor's enqueue-time gate, closing the queue-latency window between them.
    /// </summary>
    public static bool IsSearchable(AcquisitionStatus status) => status is not (
        AcquisitionStatus.Queued
        or AcquisitionStatus.Downloading
        or AcquisitionStatus.Downloaded
        or AcquisitionStatus.Importing
        or AcquisitionStatus.Imported
        or AcquisitionStatus.Cancelled);

    public async Task HandleAsync(JobContext context, CancellationToken cancellationToken) {
        var payload = AcquisitionJobPayload.Parse(context.Job.PayloadJson);

        var input = await store.GetSearchInputAsync(payload.AcquisitionId, cancellationToken);
        if (input is null) {
            logger.LogInformation("AcquisitionSearch: acquisition {Id} no longer exists; skipping.", payload.AcquisitionId);
            return;
        }

        // Re-check at execution time: the acquisition may have been queued/imported/cancelled since this
        // search was enqueued (e.g. a monitor sweep that then waited behind other work).
        var currentStatus = await store.GetStatusAsync(payload.AcquisitionId, cancellationToken);
        if (currentStatus is { } status && !IsSearchable(status)) {
            logger.LogInformation("AcquisitionSearch: acquisition {Id} is {Status}; skipping a now-stale search.", payload.AcquisitionId, status.ToCode());
            return;
        }

        await store.SetStatusAsync(payload.AcquisitionId, AcquisitionStatus.Searching, null, cancellationToken);
        await context.ReportProgressAsync(10, "Searching indexers", cancellationToken);

        try {
            // If this acquisition is an upgrade child, run an upgrade search against the parent's owned quality
            // so only strictly-better releases are accepted.
            var upgradeOwned = await store.GetUpgradeOwnedQualityAsync(payload.AcquisitionId, cancellationToken);
            var outcome = await runner.RunAsync(input, cancellationToken, upgradeOwned);
            await store.ReplaceCandidatesAsync(payload.AcquisitionId, outcome.Candidates, cancellationToken);

            var message = BuildMessage(outcome);
            await store.SetStatusAsync(payload.AcquisitionId, AcquisitionStatus.AwaitingSelection, message, cancellationToken);
            await context.ReportProgressAsync(100, "Search finished", cancellationToken);

            // A wanted-linked acquisition (created by a request commit) always auto-grabs its best
            // accepted release — the user asked for the item, not for a release-picking chore; the
            // release picker remains for the no-acceptable-release case. Ad-hoc acquisitions keep the
            // profile's explicit auto-pick opt-in.
            var autoGrab = input.EntityId is not null || await profiles.GetAutoPickAsync(input.ProfileId, input.Kind, cancellationToken);
            if (autoGrab && outcome.Candidates.Any(candidate => candidate.Accepted)) {
                await TryAutoQueueAsync(payload.AcquisitionId, cancellationToken);
            }
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            logger.LogWarning(ex, "AcquisitionSearch: failed for acquisition {Id}", payload.AcquisitionId);
            await store.SetStatusAsync(payload.AcquisitionId, AcquisitionStatus.Failed, ex.Message, CancellationToken.None);
            throw;
        }
    }

    /// <summary>
    /// Best-effort auto-pick: queues the highest-scored accepted candidate. Failures (e.g. no download client
    /// configured) are swallowed so the acquisition simply stays awaiting manual selection.
    /// </summary>
    private async Task TryAutoQueueAsync(Guid acquisitionId, CancellationToken cancellationToken) {
        var detail = await store.GetAsync(acquisitionId, cancellationToken);
        var top = detail?.Candidates
            .Where(candidate => candidate.Accepted)
            .OrderByDescending(candidate => candidate.Score)
            .FirstOrDefault();
        if (top is null) {
            return;
        }

        try {
            await queue.QueueAsync(acquisitionId, top.Id, cancellationToken);
            logger.LogInformation("AcquisitionSearch: auto-picked release {Candidate} for acquisition {Id}.", top.Id, acquisitionId);
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            logger.LogWarning(ex, "AcquisitionSearch: auto-pick failed for acquisition {Id}; awaiting manual selection.", acquisitionId);
        }
    }

    private static string? BuildMessage(AcquisitionSearchOutcome outcome) {
        var accepted = outcome.Candidates.Count(candidate => candidate.Accepted);
        var summary = $"{accepted} acceptable of {outcome.Candidates.Count} release(s).";
        if (outcome.Errors.Count == 0) {
            return summary;
        }

        var failed = string.Join(", ", outcome.Errors.Select(error => error.IndexerName));
        return $"{summary} {outcome.Errors.Count} indexer(s) failed: {failed}.";
    }
}
