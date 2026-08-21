using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Prismedia.Application.Backups;
using Prismedia.Application.Jobs.Handlers;
using Prismedia.Application.Settings;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs;

/// <summary>
/// Background service that periodically enqueues scan jobs for enabled library roots
/// based on the auto-scan interval in library settings.
/// </summary>
public sealed class JobScheduler(
    IServiceScopeFactory scopeFactory,
    ILogger<JobScheduler> logger,
    TimeProvider? timeProvider = null) : BackgroundService {
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan PluginUpdateInterval = TimeSpan.FromHours(6);
    private bool _pluginUpdateCheckQueuedOnStartup;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        logger.LogInformation("Job scheduler started.");

        while (!stoppingToken.IsCancellationRequested) {
            try {
                await ScheduleRecurringScansAsync(stoppingToken);
                await ScheduleRecurringCollectionRefreshAsync(stoppingToken);
                await ScheduleRecurringBackupsAsync(stoppingToken);
                await SchedulePluginUpdatesAsync(stoppingToken);
                await ScheduleAcquisitionMonitorAsync(stoppingToken);
                await RecoverDownloadedCompletionJobsAsync(stoppingToken);
                await RecoverStuckSearchesAsync(stoppingToken);
                await ScheduleRecycleBinCleanupAsync(stoppingToken);
                await ScheduleGridThumbnailSweepAsync(stoppingToken);
            } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                break;
            } catch (Exception ex) {
                logger.LogError(ex, "Scheduler tick failed.");
            }

            try {
                await Task.Delay(CheckInterval, stoppingToken);
            } catch (OperationCanceledException) {
                break;
            }
        }
    }

    internal async Task ScheduleRecurringScansAsync(CancellationToken cancellationToken) {
        await using var scope = scopeFactory.CreateAsyncScope();
        var settings = scope.ServiceProvider.GetRequiredService<SettingsService>();
        var settingsPersistence = scope.ServiceProvider.GetRequiredService<ISettingsPersistence>();
        var queue = scope.ServiceProvider.GetRequiredService<IJobQueueService>();

        var scanSettings = await settings.GetScanSettingsAsync(cancellationToken);
        if (!scanSettings.AutoScanEnabled || scanSettings.IntervalMinutes <= 0) {
            return;
        }

        var roots = await settings.ListLibraryRootsAsync(cancellationToken);
        var scanInterval = TimeSpan.FromMinutes(scanSettings.IntervalMinutes);
        var now = (timeProvider ?? TimeProvider.System).GetUtcNow();

        // Deep integrity scans carry the library-wide cleanups on their own, much rarer cadence.
        // The marker is written only when jobs were actually queued so a swallowed attempt
        // retries on the next tick instead of silently skipping a whole interval. An absent
        // marker (fresh install or first deploy of this cadence) initializes to now instead of
        // triggering an immediate library-wide sweep.
        var integrityInterval = TimeSpan.FromHours(Math.Max(1, scanSettings.IntegrityIntervalHours));
        var lastSweepAt = await ReadLastIntegritySweepAsync(settingsPersistence, cancellationToken);
        if (lastSweepAt is null) {
            await settingsPersistence.SaveSettingOverrideAsync(
                AppSettings.Scan.LastIntegritySweepAtKey,
                JsonSerializer.Serialize(now),
                cancellationToken);
        }
        var integrityDue = lastSweepAt is not null && now - lastSweepAt >= integrityInterval;

        var queued = 0;
        var deepQueued = 0;
        var dueRootCount = 0;

        foreach (var root in roots) {
            if (!root.Enabled) {
                continue;
            }

            var rootSelection = new LibraryScanSelection(
                Videos: root.ScanVideos,
                Images: root.ScanImages,
                Audio: root.ScanAudio,
                Books: root.ScanBooks,
                Comics: root.ScanBooks);
            if (rootSelection.IsEmpty) {
                continue;
            }

            if (integrityDue) {
                deepQueued += await LibraryScanJobs.QueueDeepScansForRootAsync(
                    queue,
                    root.Id,
                    root.Label,
                    rootSelection,
                    cancellationToken);
                dueRootCount++;
                await settings.MarkLibraryRootScanTriggeredAsync(root.Id, now, cancellationToken);
                continue;
            }

            // Age-based due check: a scan is due once its root has gone a full interval without
            // one, so a restart can no longer skip a cycle by missing the interval's first minute.
            if (root.LastScannedAt is not null && now - root.LastScannedAt < scanInterval) {
                continue;
            }

            queued += await LibraryScanJobs.QueueScansForRootAsync(
                queue,
                root.Id,
                root.Label,
                rootSelection,
                cancellationToken);
            dueRootCount++;
            await settings.MarkLibraryRootScanTriggeredAsync(root.Id, now, cancellationToken);
        }

        if (integrityDue && deepQueued > 0) {
            await settingsPersistence.SaveSettingOverrideAsync(
                AppSettings.Scan.LastIntegritySweepAtKey,
                JsonSerializer.Serialize(now),
                cancellationToken);
        }

        if (queued > 0 || deepQueued > 0) {
            logger.LogInformation(
                "Scheduled {Count} routine and {Deep} deep scan job(s) across {Roots} due root(s); scan execution is serialized.",
                queued,
                deepQueued,
                dueRootCount);
        }
    }

    private static async Task<DateTimeOffset?> ReadLastIntegritySweepAsync(
        ISettingsPersistence persistence,
        CancellationToken cancellationToken) {
        var overrides = await persistence.LoadSettingOverridesAsync(cancellationToken);
        if (!overrides.TryGetValue(AppSettings.Scan.LastIntegritySweepAtKey, out var json)) {
            return null;
        }

        try {
            return JsonSerializer.Deserialize<DateTimeOffset>(json);
        } catch (JsonException) {
            return null;
        }
    }

    internal async Task ScheduleRecurringBackupsAsync(CancellationToken cancellationToken) {
        await using var scope = scopeFactory.CreateAsyncScope();
        var backups = scope.ServiceProvider.GetRequiredService<IDatabaseBackupService>();
        if (!await backups.IsAutomaticBackupDueAsync(cancellationToken)) {
            return;
        }

        var queue = scope.ServiceProvider.GetRequiredService<IJobQueueService>();
        await queue.EnqueueAsync(
            new EnqueueJobRequest(
                JobType.DatabaseBackup,
                TargetLabel: "Daily database backup"),
            cancellationToken);
    }

    internal async Task ScheduleRecurringCollectionRefreshAsync(CancellationToken cancellationToken) {
        await using var scope = scopeFactory.CreateAsyncScope();
        var settings = scope.ServiceProvider.GetRequiredService<SettingsService>();
        var collectionSettings = await settings.GetCollectionRefreshSettingsAsync(cancellationToken);
        if (!collectionSettings.AutoRefreshEnabled) {
            return;
        }

        var now = (timeProvider ?? TimeProvider.System).GetUtcNow();
        var windowStart = GetWindowStart(now, TimeSpan.FromHours(1));
        if (now - windowStart >= CheckInterval) {
            return;
        }

        var queue = scope.ServiceProvider.GetRequiredService<IJobQueueService>();
        if (await queue.HasPendingAsync(JobType.RefreshCollection, null, cancellationToken)) {
            return;
        }

        await queue.EnqueueAsync(
            new EnqueueJobRequest(
                JobType.RefreshCollection,
                TargetLabel: "Hourly collection refresh"),
            cancellationToken);

        logger.LogInformation("Scheduled hourly collection refresh job.");
    }

    /// <summary>
    /// Enqueues an automatic plugin update check once when enabled for this worker run and then at
    /// six-hour UTC boundaries. Resetting the startup marker while disabled makes turning the setting
    /// back on take effect on the next scheduler tick instead of waiting for the next boundary.
    /// </summary>
    internal async Task SchedulePluginUpdatesAsync(CancellationToken cancellationToken) {
        await using var scope = scopeFactory.CreateAsyncScope();
        var settings = scope.ServiceProvider.GetRequiredService<SettingsService>();
        var pluginUpdates = await settings.GetPluginUpdateSettingsAsync(cancellationToken);
        if (!pluginUpdates.AutoUpdateEnabled) {
            _pluginUpdateCheckQueuedOnStartup = false;
            return;
        }

        if (_pluginUpdateCheckQueuedOnStartup) {
            var now = (timeProvider ?? TimeProvider.System).GetUtcNow();
            var windowStart = GetWindowStart(now, PluginUpdateInterval);
            if (now - windowStart >= CheckInterval) {
                return;
            }
        }

        var queue = scope.ServiceProvider.GetRequiredService<IJobQueueService>();
        if (!await queue.HasPendingAsync(JobType.UpdatePlugins, null, cancellationToken)) {
            await queue.EnqueueAsync(
                new EnqueueJobRequest(JobType.UpdatePlugins, TargetLabel: "Automatic plugin updates"),
                cancellationToken);
            logger.LogInformation("Scheduled automatic plugin update check.");
        }

        _pluginUpdateCheckQueuedOnStartup = true;
    }

    internal async Task ScheduleAcquisitionMonitorAsync(CancellationToken cancellationToken) {
        await using var scope = scopeFactory.CreateAsyncScope();
        var acquisitions = scope.ServiceProvider.GetRequiredService<Acquisition.IAcquisitionStore>();
        if (!await acquisitions.HasActiveTransfersAsync(cancellationToken)) {
            return;
        }

        var queue = scope.ServiceProvider.GetRequiredService<IJobQueueService>();
        if (await queue.HasPendingAsync(JobType.AcquisitionMonitor, null, cancellationToken)) {
            return;
        }

        await queue.EnqueueAsync(
            new EnqueueJobRequest(
                JobType.AcquisitionMonitor,
                TargetLabel: "Monitor acquisition downloads"),
            cancellationToken);
    }

    /// <summary>
    /// Recreates completion jobs for Downloaded acquisitions when a process stopped after publishing the
    /// durable status but before the queue insert committed. The queue's type+target guard makes every tick
    /// idempotent, including concurrent schedulers; unsupported ordinary kinds remain Downloaded for honest
    /// manual handling instead of generating a job that can never import them.
    /// </summary>
    internal async Task RecoverDownloadedCompletionJobsAsync(CancellationToken cancellationToken) {
        await using var scope = scopeFactory.CreateAsyncScope();
        var acquisitions = scope.ServiceProvider.GetRequiredService<Acquisition.IAcquisitionLifecycleStore>();
        var work = await acquisitions.ListDownloadedCompletionWorkAsync(cancellationToken);
        if (work.Count == 0) {
            return;
        }

        var queue = scope.ServiceProvider.GetRequiredService<IJobQueueService>();
        var importEngines = scope.ServiceProvider.GetRequiredService<IAcquisitionImportEngineFactory>();
        var completionService = scope.ServiceProvider.GetService<Acquisition.AcquisitionCompletionService>();
        foreach (var completion in work) {
            var jobType = Acquisition.AcquisitionCompletionService.CompletionJobType(
                completion.Kind,
                completion.IsUpgrade,
                completion.BookRendition);
            if (jobType == JobType.AcquisitionImport && importEngines.Find(completion.Kind) is null) {
                continue;
            }

            if (await queue.HasPendingAsync(jobType, completion.AcquisitionId.ToString(), cancellationToken)) {
                continue;
            }

            if (completionService is not null) {
                await completionService.ScheduleAsync(
                    completion.AcquisitionId,
                    cancellationToken,
                    JobGraphOrigin.Background);
            } else {
                // Keeps the scheduler independently testable and safe in minimal hosts. The production
                // container always registers AcquisitionCompletionService so graph signals are resumed.
                await queue.EnqueueAsync(
                    new EnqueueJobRequest(
                        jobType,
                        PayloadJson: Acquisition.AcquisitionJobPayload.Serialize(completion.AcquisitionId),
                        TargetEntityKind: completion.Kind.ToCode(),
                        TargetEntityId: completion.AcquisitionId.ToString(),
                        TargetLabel: completion.IsUpgrade
                            ? "Replace with reviewed release"
                            : "Import completed acquisition"),
                    cancellationToken);
            }
            logger.LogWarning(
                "Recovered missing {JobType} handoff for downloaded acquisition {AcquisitionId}.",
                jobType,
                completion.AcquisitionId);
        }
    }

    /// <summary>
    /// How long an acquisition may sit in Searching before it is treated as stuck. Generous enough for a
    /// slow multi-indexer pass, short enough that a search killed mid-flight (worker restart, cancelled
    /// job) doesn't read as searching forever.
    /// </summary>
    private static readonly TimeSpan StuckSearchTimeout = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Fails acquisitions stuck in Searching with no live search job. A search whose worker died mid-run
    /// keeps its Searching status forever (the exception path never ran), so the item looks busy while
    /// nothing is happening. Marking it Failed is safe even on a false positive: Failed is searchable, so
    /// a search job that is merely delayed re-enters Searching when it actually runs, and monitored items
    /// are re-searched by the monitored-search sweep on its own schedule.
    /// </summary>
    internal async Task RecoverStuckSearchesAsync(CancellationToken cancellationToken) {
        await using var scope = scopeFactory.CreateAsyncScope();
        var acquisitions = scope.ServiceProvider.GetRequiredService<Acquisition.IAcquisitionStore>();
        var stale = await acquisitions.ListStaleSearchingAsync(StuckSearchTimeout, cancellationToken);
        if (stale.Count == 0) {
            return;
        }

        var queue = scope.ServiceProvider.GetRequiredService<IJobQueueService>();
        foreach (var acquisitionId in stale) {
            if (await queue.HasPendingAsync(JobType.AcquisitionSearch, acquisitionId.ToString(), cancellationToken)) {
                continue;
            }

            if (await acquisitions.TryTransitionStatusAsync(
                    acquisitionId,
                    [AcquisitionStatus.Searching],
                    AcquisitionStatus.Failed,
                    "The search was interrupted or timed out; run the search again.",
                    cancellationToken)) {
                logger.LogWarning("Recovered acquisition {AcquisitionId} stuck in Searching with no live search job.", acquisitionId);
            }
        }
    }

    /// <summary>
    /// True once the startup grid-thumbnail sweep has been enqueued for this worker run;
    /// afterwards the sweep only recurs on its daily window.
    /// </summary>
    private bool _gridThumbnailSweepQueuedOnStartup;

    /// <summary>
    /// Enqueues the grid-thumbnail sweep once at worker startup (so existing libraries heal
    /// immediately after an upgrade) and daily thereafter. The sweep job itself no-ops
    /// quickly when every entity's grid variants are current.
    /// </summary>
    internal async Task ScheduleGridThumbnailSweepAsync(CancellationToken cancellationToken) {
        if (_gridThumbnailSweepQueuedOnStartup) {
            var now = (timeProvider ?? TimeProvider.System).GetUtcNow();
            var windowStart = GetWindowStart(now, TimeSpan.FromDays(1));
            if (now - windowStart >= CheckInterval) {
                return;
            }
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var queue = scope.ServiceProvider.GetRequiredService<IJobQueueService>();
        if (!await queue.HasPendingAsync(JobType.GridThumbnailSweep, null, cancellationToken)) {
            await queue.EnqueueAsync(
                new EnqueueJobRequest(JobType.GridThumbnailSweep, TargetLabel: "Grid thumbnail sweep"),
                cancellationToken);
            logger.LogInformation("Scheduled grid-thumbnail sweep.");
        }

        _gridThumbnailSweepQueuedOnStartup = true;
    }

    internal async Task ScheduleRecycleBinCleanupAsync(CancellationToken cancellationToken) {
        await using var scope = scopeFactory.CreateAsyncScope();
        var settings = scope.ServiceProvider.GetRequiredService<SettingsService>();
        var recycleBin = await settings.GetRecycleBinSettingsAsync(cancellationToken);
        if (recycleBin.Path is null) {
            return;
        }

        var now = (timeProvider ?? TimeProvider.System).GetUtcNow();
        var windowStart = GetWindowStart(now, TimeSpan.FromDays(1));
        if (now - windowStart >= CheckInterval) {
            return;
        }

        var queue = scope.ServiceProvider.GetRequiredService<IJobQueueService>();
        if (await queue.HasPendingAsync(JobType.RecycleBinCleanup, null, cancellationToken)) {
            return;
        }

        await queue.EnqueueAsync(
            new EnqueueJobRequest(JobType.RecycleBinCleanup, TargetLabel: "Daily recycle-bin cleanup"),
            cancellationToken);
    }

    private static DateTimeOffset GetWindowStart(DateTimeOffset now, TimeSpan interval) {
        var ticksIntoWindow = now.UtcTicks % interval.Ticks;
        return new DateTimeOffset(now.UtcDateTime.AddTicks(-ticksIntoWindow), TimeSpan.Zero);
    }
}
