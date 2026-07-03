using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Prismedia.Application.Backups;
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
                await ScheduleRecycleBinCleanupAsync(stoppingToken);
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
