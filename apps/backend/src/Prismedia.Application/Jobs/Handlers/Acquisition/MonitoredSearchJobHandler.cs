using Microsoft.Extensions.Logging;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Settings;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers;

/// <summary>
/// One pass over every monitor whose periodic re-search is due. For each, it re-runs the existing
/// <see cref="JobType.AcquisitionSearch"/> against the wanted acquisition — reusing the committed
/// search → score → (auto-pick) → download → import pipeline and its blocklist gate unchanged, so a
/// wanted book is fetched once an acceptable release appears, with no new grab path. The store reconciles
/// fulfilled/orphaned monitors as part of listing the due set, so satisfied wants stop re-searching.
/// </summary>
public sealed class MonitoredSearchJobHandler(
    IMonitorStore monitors,
    SettingsService settings,
    ILogger<MonitoredSearchJobHandler> logger) : IJobHandler {
    public JobType Type => JobType.MonitoredSearch;

    public async Task HandleAsync(JobContext context, CancellationToken cancellationToken) {
        var config = await settings.GetMonitoredSearchSettingsAsync(cancellationToken);
        var due = await monitors.ListDueMonitorsAsync(config.IntervalMinutes, cancellationToken);
        if (due.Count == 0) {
            return;
        }

        logger.LogInformation("MonitoredSearch: re-searching {Count} due monitor(s).", due.Count);
        var processed = 0;
        foreach (var monitor in due) {
            cancellationToken.ThrowIfCancellationRequested();

            // An upgrade-due monitor searches on a fresh CHILD acquisition (claimed atomically), so the
            // imported parent and its on-disk file are never touched by the search or its grab. A still-missing
            // monitor re-searches its own acquisition. Either way the search job is deduped per target.
            var searchTarget = monitor.AcquisitionId;
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
