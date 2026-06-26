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

    public async Task HandleAsync(JobContext context, CancellationToken cancellationToken) {
        var payload = AcquisitionJobPayload.Parse(context.Job.PayloadJson);

        var input = await store.GetSearchInputAsync(payload.AcquisitionId, cancellationToken);
        if (input is null) {
            logger.LogInformation("AcquisitionSearch: acquisition {Id} no longer exists; skipping.", payload.AcquisitionId);
            return;
        }

        await store.SetStatusAsync(payload.AcquisitionId, AcquisitionStatus.Searching, null, cancellationToken);
        await context.ReportProgressAsync(10, "Searching indexers", cancellationToken);

        try {
            var outcome = await runner.RunAsync(input, cancellationToken);
            await store.ReplaceCandidatesAsync(payload.AcquisitionId, outcome.Candidates, cancellationToken);

            var message = BuildMessage(outcome);
            await store.SetStatusAsync(payload.AcquisitionId, AcquisitionStatus.AwaitingSelection, message, cancellationToken);
            await context.ReportProgressAsync(100, "Search finished", cancellationToken);

            // When the default profile opts into auto-pick and the search found an acceptable release,
            // queue the top-ranked one immediately rather than waiting for manual selection.
            if (outcome.Candidates.Any(candidate => candidate.Accepted)
                && await profiles.GetDefaultAutoPickAsync(cancellationToken)) {
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
