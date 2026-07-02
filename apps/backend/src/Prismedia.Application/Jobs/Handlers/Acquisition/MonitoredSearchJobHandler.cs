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
/// fetched once an acceptable release appears. A container monitor (an author/artist watched for new
/// works) runs a discovery sync instead, surfacing missing works as wanted phantoms without downloading
/// anything on its own. The store reconciles fulfilled/orphaned monitors as part of listing the due set.
/// </summary>
public sealed class MonitoredSearchJobHandler(
    IMonitorStore monitors,
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

            // A container monitor is due for a discovery sync: re-resolve the author/artist from its
            // provider and surface missing works as wanted phantoms. A failed sync (entity deleted,
            // provider gone) pauses the monitor rather than retrying forever.
            if (monitor.EntityId is { } watchedEntityId) {
                var synced = await requests.SyncContainerAsync(watchedEntityId, cancellationToken);
                if (!synced) {
                    logger.LogInformation("MonitoredSearch: container sync for monitor {MonitorId} could not resolve; pausing.", monitor.MonitorId);
                    await monitors.SetStatusAsync(monitor.MonitorId, MonitorStatus.Paused, cancellationToken);
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
                var childId = await monitors.CreateUpgradeChildAsync(monitor.MonitorId, cancellationToken);
                if (childId is not { } id) {
                    continue; // the upgrade slot was already taken — skip this monitor this pass
                }

                searchTarget = id;
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
