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
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            logger.LogWarning(ex, "AcquisitionSearch: failed for acquisition {Id}", payload.AcquisitionId);
            await store.SetStatusAsync(payload.AcquisitionId, AcquisitionStatus.Failed, ex.Message, CancellationToken.None);
            throw;
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
