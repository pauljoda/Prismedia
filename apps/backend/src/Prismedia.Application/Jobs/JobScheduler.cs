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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        logger.LogInformation("Job scheduler started.");

        while (!stoppingToken.IsCancellationRequested) {
            try {
                await ScheduleRecurringScansAsync(stoppingToken);
                await ScheduleRecurringCollectionRefreshAsync(stoppingToken);
                await ScheduleRecurringBackupsAsync(stoppingToken);
                await ScheduleAcquisitionMonitorAsync(stoppingToken);
                await ScheduleMonitoredSearchAsync(stoppingToken);
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
        var queue = scope.ServiceProvider.GetRequiredService<IJobQueueService>();

        var scanSettings = await settings.GetScanSettingsAsync(cancellationToken);
        if (!scanSettings.AutoScanEnabled || scanSettings.IntervalMinutes <= 0) {
            return;
        }

        var roots = await settings.ListLibraryRootsAsync(cancellationToken);
        var scanInterval = TimeSpan.FromMinutes(scanSettings.IntervalMinutes);
        var now = (timeProvider ?? TimeProvider.System).GetUtcNow();
        var windowStart = GetWindowStart(now, scanInterval);
        if (now - windowStart >= CheckInterval) {
            return;
        }

        // Collect the kinds that have at least one enabled root due for a scan. Each scan job is a
        // per-kind singleton that covers every enabled root of that kind, so a single aggregate scan
        // per due kind replaces the previous per-root fan-out.
        var scanVideos = false;
        var scanImages = false;
        var scanAudio = false;
        var scanBooks = false;
        var dueRootIds = new List<Guid>();

        foreach (var root in roots) {
            if (!root.Enabled) {
                continue;
            }

            if (!LibraryScanJobs.ScanJobTypesFor(
                root.ScanVideos,
                root.ScanImages,
                root.ScanAudio,
                root.ScanBooks).Any()) {
                continue;
            }

            if (root.LastScannedAt is not null && root.LastScannedAt >= windowStart) {
                continue;
            }

            scanVideos |= root.ScanVideos;
            scanImages |= root.ScanImages;
            scanAudio |= root.ScanAudio;
            scanBooks |= root.ScanBooks;
            dueRootIds.Add(root.Id);
        }

        if (dueRootIds.Count == 0) {
            return;
        }

        var queued = await LibraryScanJobs.QueueScansForKindsAsync(
            queue, scanVideos, scanImages, scanAudio, scanBooks, cancellationToken);

        foreach (var rootId in dueRootIds) {
            await settings.MarkLibraryRootScanTriggeredAsync(rootId, now, cancellationToken);
        }

        if (queued > 0) {
            logger.LogInformation("Scheduled {Count} aggregate scan job(s) across {Roots} due root(s).", queued, dueRootIds.Count);
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
                TargetLabel: "Daily database backup",
            Priority: -100),
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
                TargetLabel: "Hourly collection refresh",
                Priority: 0),
            cancellationToken);

        logger.LogInformation("Scheduled hourly collection refresh job.");
    }

    internal async Task ScheduleMonitoredSearchAsync(CancellationToken cancellationToken) {
        await using var scope = scopeFactory.CreateAsyncScope();
        var settings = scope.ServiceProvider.GetRequiredService<SettingsService>();
        var config = await settings.GetMonitoredSearchSettingsAsync(cancellationToken);
        if (!config.Enabled || config.IntervalMinutes <= 0) {
            return;
        }

        // Window-gated so the sweep enqueues at most once per interval (in the tick that opens the window).
        var now = (timeProvider ?? TimeProvider.System).GetUtcNow();
        var windowStart = GetWindowStart(now, TimeSpan.FromMinutes(config.IntervalMinutes));
        if (now - windowStart >= CheckInterval) {
            return;
        }

        var monitors = scope.ServiceProvider.GetRequiredService<Acquisition.IMonitorStore>();
        if (!await monitors.HasActiveMonitorsAsync(cancellationToken)) {
            return;
        }

        var queue = scope.ServiceProvider.GetRequiredService<IJobQueueService>();
        if (await queue.HasPendingAsync(JobType.MonitoredSearch, null, cancellationToken)) {
            return;
        }

        await queue.EnqueueAsync(
            new EnqueueJobRequest(JobType.MonitoredSearch, TargetLabel: "Re-search monitored items"),
            cancellationToken);

        logger.LogInformation("Scheduled monitored-search sweep.");
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
            new EnqueueJobRequest(JobType.AcquisitionMonitor, TargetLabel: "Monitor acquisition downloads"),
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
        foreach (var completion in work) {
            if (!completion.IsUpgrade && importEngines.Find(completion.Kind) is null) {
                continue;
            }

            var jobType = completion.IsUpgrade
                ? JobType.AcquisitionUpgradeReplace
                : JobType.AcquisitionImport;
            var targetId = completion.AcquisitionId.ToString();
            if (await queue.HasPendingAsync(jobType, targetId, cancellationToken)) {
                continue;
            }

            await queue.EnqueueAsync(
                new EnqueueJobRequest(
                    jobType,
                    PayloadJson: Acquisition.AcquisitionJobPayload.Serialize(completion.AcquisitionId),
                    TargetEntityId: targetId,
                    TargetLabel: completion.IsUpgrade ? "Replace with upgrade" : "Import completed download"),
                cancellationToken);
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
                new EnqueueJobRequest(JobType.GridThumbnailSweep, TargetLabel: "Grid thumbnail sweep", Priority: -50),
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
            new EnqueueJobRequest(JobType.RecycleBinCleanup, TargetLabel: "Daily recycle-bin cleanup", Priority: -50),
            cancellationToken);
    }

    private static DateTimeOffset GetWindowStart(DateTimeOffset now, TimeSpan interval) {
        var ticksIntoWindow = now.UtcTicks % interval.Ticks;
        return new DateTimeOffset(now.UtcDateTime.AddTicks(-ticksIntoWindow), TimeSpan.Zero);
    }
}
