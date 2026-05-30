using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Prismedia.Application.Settings;

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
            } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                break;
            } catch (Exception ex) {
                logger.LogError(ex, "Scheduler tick failed.");
            }

            await Task.Delay(CheckInterval, stoppingToken);
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

            var queued = await LibraryScanJobs.QueueRootScansAsync(
                queue,
                root.Id,
                root.Label,
                root.ScanVideos,
                root.ScanImages,
                root.ScanAudio,
                root.ScanBooks,
                cancellationToken);

            await settings.MarkLibraryRootScanTriggeredAsync(root.Id, now, cancellationToken);

            if (queued > 0) {
                logger.LogInformation("Scheduled {Count} scan job(s) for root '{Label}'.", queued, root.Label);
            }
        }
    }

    private static DateTimeOffset GetWindowStart(DateTimeOffset now, TimeSpan interval) {
        var ticksIntoWindow = now.UtcTicks % interval.Ticks;
        return new DateTimeOffset(now.UtcDateTime.AddTicks(-ticksIntoWindow), TimeSpan.Zero);
    }
}
