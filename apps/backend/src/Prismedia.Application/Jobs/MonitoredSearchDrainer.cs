using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Settings;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs;

/// <summary>Outcome of one non-overlapping monitored-search drain attempt.</summary>
public enum MonitoredSearchDrainState {
    /// <summary>Automatic monitored searches are disabled.</summary>
    Disabled,
    /// <summary>A monitor/search job is already active, so no new work was published.</summary>
    Busy,
    /// <summary>No monitor is currently due.</summary>
    Idle,
    /// <summary>Exactly one due monitor was published to the durable queue.</summary>
    Queued
}

/// <summary>
/// Selects one due monitor at a time and publishes its durable job only when the preceding monitored
/// search has finished. The serialization covers search work only: downloads and playback are never
/// admitted through this drainer and remain independently concurrent.
/// </summary>
public sealed class MonitoredSearchDrainer(
    IMonitorStore monitors,
    SettingsService settings,
    IJobQueueService jobs,
    ILogger<MonitoredSearchDrainer> logger) {
    /// <summary>Queues at most one due monitor, or reports why no work was queued.</summary>
    public async Task<MonitoredSearchDrainState> DrainNextAsync(CancellationToken cancellationToken) {
        var config = await settings.GetMonitoredSearchSettingsAsync(cancellationToken);
        if (!config.Enabled || config.IntervalMinutes <= 0) {
            return MonitoredSearchDrainState.Disabled;
        }

        // Manual searches also get precedence over recurring background searches. This is deliberately
        // search-scoped: once a search has handed work to a download client the next monitor may be checked,
        // while transfers and every playback stream continue with their normal independent concurrency.
        if (await jobs.HasPendingAsync(JobType.MonitoredSearch, null, cancellationToken)
            || await jobs.HasPendingAsync(JobType.AcquisitionSearch, null, cancellationToken)) {
            return MonitoredSearchDrainState.Busy;
        }

        var next = (await monitors.ListDueMonitorsAsync(config.IntervalMinutes, cancellationToken)).FirstOrDefault();
        if (next is null) {
            return MonitoredSearchDrainState.Idle;
        }

        var queued = await jobs.EnqueueAsync(
            new EnqueueJobRequest(
                JobType.MonitoredSearch,
                PayloadJson: new MonitoredSearchPayload(next.MonitorId).ToJson(),
                TargetLabel: next.Title,
                Origin: JobGraphOrigin.Background,
                GraphRootEntityKind: next.Kind.ToCode(),
                GraphRootEntityId: next.EntityId?.ToString()),
            cancellationToken);
        if (!MonitoredSearchPayload.TryParse(queued.PayloadJson, out var queuedPayload)
            || queuedPayload.MonitorId != next.MonitorId) {
            // A concurrent worker won the queue-wide singleton race. Its monitor owns the slot and this
            // candidate remains due; never stamp work that was not actually made durable.
            return MonitoredSearchDrainState.Busy;
        }

        // Stamp at durable publication, not completion. A poisoned monitor therefore backs off after its
        // retries are exhausted instead of monopolizing the single-file drainer forever; job redelivery
        // still re-resolves this exact monitor through ListImmediateForMonitorAsync.
        await monitors.MarkSearchedAsync(next.MonitorId, cancellationToken);
        logger.LogInformation(
            "Queued the next due monitor {MonitorId} for '{Title}'; recurring monitor searches remain serialized.",
            next.MonitorId,
            next.Title);
        return MonitoredSearchDrainState.Queued;
    }
}

/// <summary>
/// Continuously advances due monitor work independently of recurring scheduler windows. Idle passes are
/// cheap and infrequent; a newly queued or active search is observed before another monitor is selected.
/// </summary>
internal sealed class MonitoredSearchWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<MonitoredSearchWorker> logger) : BackgroundService {
    private static readonly TimeSpan QueuedDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan BusyDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan IdleDelay = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        logger.LogInformation("Sequential monitored-search worker started.");
        while (!stoppingToken.IsCancellationRequested) {
            MonitoredSearchDrainState state;
            try {
                await using var scope = scopeFactory.CreateAsyncScope();
                state = await scope.ServiceProvider
                    .GetRequiredService<MonitoredSearchDrainer>()
                    .DrainNextAsync(stoppingToken);
            } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                break;
            } catch (Exception exception) {
                logger.LogError(exception, "Sequential monitored-search pass failed.");
                await DelayAsync(IdleDelay, stoppingToken);
                continue;
            }

            await DelayAsync(state switch {
                MonitoredSearchDrainState.Queued => QueuedDelay,
                MonitoredSearchDrainState.Busy => BusyDelay,
                _ => IdleDelay
            }, stoppingToken);
        }
    }

    private static async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) {
        try {
            await Task.Delay(delay, cancellationToken);
        } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            // Normal hosted-service shutdown.
        }
    }
}
