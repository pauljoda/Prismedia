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
            // Deduped per acquisition: a search already queued/running for this item is not stacked.
            await context.EnqueueIfNeededAsync(
                new EnqueueJobRequest(
                    JobType.AcquisitionSearch,
                    PayloadJson: AcquisitionJobPayload.Serialize(monitor.AcquisitionId),
                    TargetEntityId: monitor.AcquisitionId.ToString(),
                    TargetLabel: monitor.Title),
                cancellationToken);
            await monitors.MarkSearchedAsync(monitor.MonitorId, cancellationToken);
            await context.ReportProgressAsync(++processed * 100 / due.Count, $"Re-searching {monitor.Title}", cancellationToken);
        }
    }
}
