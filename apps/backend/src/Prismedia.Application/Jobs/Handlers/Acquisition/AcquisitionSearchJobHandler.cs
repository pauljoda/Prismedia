using Microsoft.Extensions.Logging;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Settings;
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
[JobDefinition(JobType.AcquisitionSearch)]
public sealed class AcquisitionSearchJobHandler(
    IAcquisitionStore store,
    AcquisitionSearchRunner runner,
    IBookAcquisitionProfileStore profiles,
    AcquisitionQueueService queue,
    IDownloadClientConfigStore downloadClients,
    SettingsService settings,
    AcquisitionMissingChildFallback missingChildren,
    ILogger<AcquisitionSearchJobHandler> logger,
    IJobGraphService? graphs = null,
    IAcquisitionReleaseTimingService? releaseTiming = null) : IJobHandler {
    /// <summary>
    /// States from which an explicit API or monitor action may publish a fresh Searching intent. This is a
    /// scheduling policy only; the job handler itself consumes Searching exclusively.
    /// </summary>
    public static bool CanScheduleSearch(AcquisitionStatus status) => status is not (
        AcquisitionStatus.Queued
        or AcquisitionStatus.Downloading
        or AcquisitionStatus.WaitingForDownloadClient
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

        // Creation normally gates automatic work before it publishes the job. Recheck at execution so
        // metadata committed just after creation—and old queued work created before this policy existed—
        // can still stop before the first indexer request. Explicit Search again is user authority and
        // intentionally bypasses release timing.
        if (!payload.ManualReview && releaseTiming is not null) {
            var timing = await releaseTiming.EvaluateAsync(
                input.EntityId,
                input.ProfileId,
                input.Kind,
                cancellationToken);
            if (!timing.CanSearch) {
                if (!await store.TryTransitionStatusAsync(
                        payload.AcquisitionId,
                        [AcquisitionStatus.Searching],
                        AcquisitionStatus.WaitingForRelease,
                        timing.Message,
                        cancellationToken)) {
                    logger.LogInformation(
                        "AcquisitionSearch: acquisition {Id} changed before its release gate could be restored.",
                        payload.AcquisitionId);
                }
                return;
            }
        }
        await context.ReportProgressAsync(10, "Searching indexers", cancellationToken);

        try {
            // If this acquisition is an upgrade child, run an upgrade search against the parent's owned quality
            // so only strictly-better releases are accepted.
            var upgradeOwned = await store.GetUpgradeOwnedQualityAsync(payload.AcquisitionId, cancellationToken);
            var outcome = await runner.RunAsync(
                input,
                cancellationToken,
                upgradeOwned,
                payload.CustomQuery);
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

            var fallback = await missingChildren.TryStartAsync(input, outcome, context, cancellationToken);
            if (fallback is { Missing: > 0 }) {
                logger.LogInformation(
                    "AcquisitionSearch: whole-unit search for {Id} was barren; {Covered} of {Missing} missing children now have direct acquisitions.",
                    payload.AcquisitionId,
                    fallback.Value.Covered,
                    fallback.Value.Missing);
            }
            await context.ReportProgressAsync(100, "Search finished", cancellationToken);

            // A wanted-linked acquisition (created by a request commit) always auto-grabs its best
            // accepted release — the user asked for the item, not for a release-picking chore; the
            // release picker remains for the no-acceptable-release case. Ad-hoc acquisitions keep the
            // profile's explicit auto-pick opt-in.
            var autoGrab = !payload.ManualReview
                && (input.EntityId is not null
                    || await profiles.GetAutoPickAsync(input.ProfileId, input.Kind, cancellationToken));
            if (autoGrab && outcome.Candidates.Any(candidate => candidate.Accepted)) {
                await OpenReviewSignalAsync(context, payload.AcquisitionId, cancellationToken);
                await TryAutoQueueAsync(payload.AcquisitionId, input.Kind, cancellationToken);
            } else if (outcome.Candidates.Count > 0) {
                await OpenReviewSignalAsync(context, payload.AcquisitionId, cancellationToken);
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

    private async Task OpenReviewSignalAsync(
        JobContext context,
        Guid acquisitionId,
        CancellationToken cancellationToken) {
        if (graphs is null || context.Job.GraphId is not { } graphId) return;
        await graphs.OpenSignalAsync(
            graphId,
            AcquisitionGraphSignals.Review(acquisitionId),
            JobGraphSignalKind.DomainEvent,
            acquisitionId.ToString(),
            "Waiting for release review",
            cancellationToken);
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
    private async Task TryAutoQueueAsync(
        Guid acquisitionId,
        EntityKind kind,
        CancellationToken cancellationToken) {
        var detail = await store.GetAsync(acquisitionId, cancellationToken);
        var preferredProtocol = await AcquisitionProtocolPreference.ResolveAsync(downloadClients, settings, cancellationToken);
        var accepted = detail is null
            ? []
            : AcquisitionProtocolPreference.Order(
                    detail.Candidates.Where(candidate => candidate.Accepted),
                    preferredProtocol,
                    candidate => candidate.Protocol,
                    candidate => candidate.Score,
                    candidate => AcquisitionReleaseRanking.SwarmTieBreak(
                        kind,
                        candidate.Protocol,
                        candidate.Seeders,
                        candidate.Peers))
                .Take(MaxAutoQueueAttempts)
                .ToArray();

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
