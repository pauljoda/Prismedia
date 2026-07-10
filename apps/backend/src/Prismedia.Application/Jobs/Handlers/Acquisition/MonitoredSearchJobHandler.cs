using Microsoft.Extensions.Logging;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Requests;
using Prismedia.Application.Settings;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers;

/// <summary>
/// One pass over every monitor whose periodic action is due. A still-missing acquisition monitor re-runs
/// the existing <see cref="JobType.AcquisitionSearch"/> — reusing the committed search → score →
/// (auto-pick) → download → import pipeline and its blocklist gate unchanged, so a wanted item is
/// fetched once an acceptable release appears. A container monitor runs a provider-backed child discovery
/// sync instead, surfacing missing works as wanted phantoms without downloading
/// anything on its own. The store reconciles fulfilled/orphaned monitors as part of listing the due set.
/// </summary>
public sealed class MonitoredSearchJobHandler(
    IMonitorStore monitors,
    IAcquisitionLifecycleStore acquisitions,
    SettingsService settings,
    RequestCommitService requests,
    ILogger<MonitoredSearchJobHandler> logger) : IJobHandler {
    public JobType Type => JobType.MonitoredSearch;

    public async Task HandleAsync(JobContext context, CancellationToken cancellationToken) {
        var config = await settings.GetMonitoredSearchSettingsAsync(cancellationToken);
        var due = await monitors.ListDueMonitorsAsync(config.IntervalMinutes, cancellationToken);
        if (due.Count == 0) {
            return;
        }

        logger.LogInformation("MonitoredSearch: processing {Count} due monitor(s).", due.Count);
        var processed = 0;
        foreach (var monitor in due) {
            cancellationToken.ThrowIfCancellationRequested();

            // Due is a snapshot. Revalidate the exact monitor before any request/search action so a row
            // claimed by destructive lifecycle work after the sweep is never acted on by this stale pass.
            if (!await monitors.IsActiveAsync(monitor.MonitorId, cancellationToken)) {
                continue;
            }

            // An imported structural unit that left gaps falls back to direct child acquisitions. The
            // Request registry and shared child traversal decide what those children are; this handler has
            // no season/episode, artist/album, or book/volume branch.
            if (monitor.MissingChildFallback && monitor.EntityId is { } parentEntityId) {
                var outcome = await requests.RequestMissingChildrenAsync(parentEntityId, cancellationToken);
                logger.LogInformation(
                    "MonitoredSearch: '{Title}' imported with {Missing} missing child item(s); {Covered} covered by child acquisitions.",
                    monitor.Title, outcome.Missing, outcome.Covered);

                // Keep the stable Entity monitor Active. Once child imports clear the wanted gaps, the next
                // sweep detaches the transient parent acquisition while preserving explicit monitor intent.
                await monitors.MarkSearchedAsync(monitor.MonitorId, cancellationToken);
                await context.ReportProgressAsync(++processed * 100 / due.Count, $"Filling gaps in {monitor.Title}", cancellationToken);
                continue;
            }

            // Entity-only intent: containers sync provider children, source-backed leaves remain active,
            // and fileless leaves request themselves. Acquisition-linked dues always take the branch below.
            if (monitor.AcquisitionId is null && monitor.EntityId is { } watchedEntityId) {
                var maintained = await requests.MaintainAsync(watchedEntityId, cancellationToken);
                if (!maintained) {
                    // False also means lifecycle contention (a child is being unmonitored or this Entity
                    // is deleting files). That is transient and must never rewrite durable parent intent.
                    // Keep the monitor Active and let the normal interval retry; permanent plugin loss is
                    // visible through eligibility/settings rather than inferred from one failed sweep.
                    logger.LogInformation(
                        "MonitoredSearch: Entity maintenance for monitor {MonitorId} did not run; keeping it Active for retry.",
                        monitor.MonitorId);
                }

                await monitors.MarkSearchedAsync(monitor.MonitorId, cancellationToken);
                await context.ReportProgressAsync(++processed * 100 / due.Count, $"Syncing {monitor.Title}", cancellationToken);
                continue;
            }

            if (monitor.AcquisitionId is not { } acquisitionId) {
                continue; // defensive: a non-container due always carries its acquisition
            }

            // An upgrade-due monitor searches on a fresh CHILD acquisition (claimed atomically), so the
            // imported parent and its on-disk file are never touched by the search or its grab. A still-missing
            // monitor re-searches its own acquisition. Either way the search job is deduped per target.
            var searchTarget = acquisitionId;
            if (monitor.IsUpgrade) {
                Guid? childId = null;
                var accepted = monitor.EntityId is { } upgradeEntityId
                    ? await monitors.ExecuteIfActiveEntityMutationAsync(
                        upgradeEntityId,
                        async leaseCancellationToken => childId = await monitors.CreateUpgradeChildAsync(
                            monitor.MonitorId,
                            leaseCancellationToken),
                        cancellationToken)
                    : (childId = await monitors.CreateUpgradeChildAsync(
                        monitor.MonitorId,
                        cancellationToken)) is not null;
                if (!accepted) {
                    continue;
                }
                if (childId is not { } id) {
                    continue; // the upgrade slot was already taken — skip this monitor this pass
                }

                searchTarget = id;
            }

            // The active monitor is explicit durable provenance for a retry. Publish Searching BEFORE
            // enqueue so enqueue failure leaves recoverable intent, while redelivery after review/manual
            // state cannot infer a new search on its own.
            var searchStatus = await acquisitions.GetStatusAsync(searchTarget, cancellationToken);
            if (searchStatus is null || !AcquisitionSearchJobHandler.CanScheduleSearch(searchStatus.Value)) {
                continue;
            }
            if (searchStatus != AcquisitionStatus.Searching
                && !await acquisitions.TryTransitionStatusAsync(
                    searchTarget,
                    [searchStatus.Value],
                    AcquisitionStatus.Searching,
                    null,
                    cancellationToken)) {
                continue;
            }

            await context.EnqueueIfNeededAsync(
                new EnqueueJobRequest(
                    JobType.AcquisitionSearch,
                    PayloadJson: AcquisitionJobPayload.Serialize(searchTarget),
                    TargetEntityId: searchTarget.ToString(),
                    TargetLabel: monitor.Title),
                cancellationToken);
            await monitors.MarkSearchedAsync(monitor.MonitorId, cancellationToken);
            await context.ReportProgressAsync(++processed * 100 / due.Count, $"Re-searching {monitor.Title}", cancellationToken);
        }
    }
}
