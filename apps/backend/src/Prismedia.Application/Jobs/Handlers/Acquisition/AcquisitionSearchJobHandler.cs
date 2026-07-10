using Microsoft.Extensions.Logging;
using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers;

/// <summary>
/// Runs the indexer release search for one acquisition in the background. Indexer searches against
/// Prowlarr routinely take tens of seconds, so this work is durable and off the request path: it moves
/// the acquisition to <see cref="AcquisitionStatus.Searching"/>, queries indexers, persists scored
/// candidates, and leaves it <see cref="AcquisitionStatus.AwaitingSelection"/> for review.
/// The enqueuer publishes Searching first; that durable state distinguishes intentional work from an
/// at-least-once redelivery of an old job.
/// </summary>
public sealed class AcquisitionSearchJobHandler(
    IAcquisitionStore store,
    AcquisitionSearchRunner runner,
    IBookAcquisitionProfileStore profiles,
    AcquisitionQueueService queue,
    ILogger<AcquisitionSearchJobHandler> logger) : IJobHandler {
    public JobType Type => JobType.AcquisitionSearch;

    /// <summary>
    /// States from which an explicit API or monitor action may publish a fresh Searching intent. This is a
    /// scheduling policy only; the job handler itself consumes Searching exclusively.
    /// </summary>
    public static bool CanScheduleSearch(AcquisitionStatus status) => status is not (
        AcquisitionStatus.Queued
        or AcquisitionStatus.Downloading
        or AcquisitionStatus.Downloaded
        or AcquisitionStatus.Importing
        or AcquisitionStatus.Imported
        or AcquisitionStatus.Stopping);

    public async Task HandleAsync(JobContext context, CancellationToken cancellationToken) {
        var payload = AcquisitionJobPayload.Parse(context.Job.PayloadJson);

        var input = await store.GetSearchInputAsync(payload.AcquisitionId, cancellationToken);
        if (input is null) {
            logger.LogInformation("AcquisitionSearch: acquisition {Id} no longer exists; skipping.", payload.AcquisitionId);
            return;
        }

        // Searching is the durable job ticket. Pending means creation never published intent; review/manual
        // states mean this is an old redelivery after a prior search completed; Cancelled is user authority.
        var currentStatus = await store.GetStatusAsync(payload.AcquisitionId, cancellationToken);
        if (currentStatus != AcquisitionStatus.Searching) {
            logger.LogInformation(
                "AcquisitionSearch: acquisition {Id} has no Searching intent ({Status}); skipping stale work.",
                payload.AcquisitionId,
                currentStatus?.ToCode() ?? "missing");
            return;
        }
        await context.ReportProgressAsync(10, "Searching indexers", cancellationToken);

        try {
            // If this acquisition is an upgrade child, run an upgrade search against the parent's owned quality
            // so only strictly-better releases are accepted.
            var upgradeOwned = await store.GetUpgradeOwnedQualityAsync(payload.AcquisitionId, cancellationToken);
            var outcome = await runner.RunAsync(input, cancellationToken, upgradeOwned);
            var message = BuildMessage(outcome);
            if (!await store.TryCompleteSearchAsync(
                    payload.AcquisitionId,
                    outcome.Candidates,
                    message,
                    cancellationToken)) {
                logger.LogInformation(
                    "AcquisitionSearch: acquisition {Id} changed before search results completed; discarding stale results.",
                    payload.AcquisitionId);
                return;
            }
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
            if (!await store.TryTransitionStatusAsync(
                    payload.AcquisitionId,
                    [AcquisitionStatus.Searching],
                    AcquisitionStatus.Failed,
                    ex.Message,
                    CancellationToken.None)) {
                logger.LogInformation(
                    "AcquisitionSearch: acquisition {Id} changed while its search failed; preserving the newer lifecycle.",
                    payload.AcquisitionId);
                return;
            }
            throw;
        }
    }

    /// <summary>
    /// Auto-pick attempts are bounded so a broken client is not hammered with every candidate the
    /// search returned; three best-scored releases is plenty to skate past a duplicate or a dead link.
    /// </summary>
    private const int MaxAutoQueueAttempts = 3;

    /// <summary>
    /// Best-effort auto-pick, best-scored first with fallback: a top candidate the download client
    /// refuses (a duplicate add, a dead link) must not strand the acquisition when the next-best
    /// accepted release would work. When every attempt fails, the acquisition is put back to
    /// awaiting-selection — never left Failed while pickable candidates exist (QueueAsync marks
    /// Failed internally on an add error, so the status must be restored here).
    /// </summary>
    private async Task TryAutoQueueAsync(Guid acquisitionId, CancellationToken cancellationToken) {
        var detail = await store.GetAsync(acquisitionId, cancellationToken);
        var accepted = detail?.Candidates
            .Where(candidate => candidate.Accepted)
            .OrderByDescending(candidate => candidate.Score)
            .Take(MaxAutoQueueAttempts)
            .ToArray() ?? [];

        foreach (var candidate in accepted) {
            try {
                await queue.QueueAsync(
                    acquisitionId,
                    candidate.Id,
                    cancellationToken,
                    requiredStatus: AcquisitionStatus.AwaitingSelection);
                logger.LogInformation("AcquisitionSearch: auto-picked release {Candidate} for acquisition {Id}.", candidate.Id, acquisitionId);
                return;
            } catch (OperationCanceledException) {
                throw;
            } catch (Exception ex) {
                logger.LogWarning(ex, "AcquisitionSearch: auto-pick candidate {Candidate} failed for acquisition {Id}; trying the next accepted release.", candidate.Id, acquisitionId);
            }
        }

        if (accepted.Length > 0) {
            await store.TryTransitionStatusAsync(
                acquisitionId,
                [AcquisitionStatus.AwaitingSelection, AcquisitionStatus.Failed],
                AcquisitionStatus.AwaitingSelection,
                "Automatic download failed for the best releases; pick one manually.",
                cancellationToken);
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
