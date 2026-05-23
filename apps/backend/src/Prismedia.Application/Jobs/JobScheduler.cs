using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Prismedia.Application.Settings;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs;

/// <summary>
/// Background service that periodically enqueues scan jobs for enabled library roots
/// based on the auto-scan interval in library settings.
/// </summary>
public sealed class JobScheduler(
    IServiceScopeFactory scopeFactory,
    ILogger<JobScheduler> logger) : BackgroundService {
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

    private async Task ScheduleRecurringScansAsync(CancellationToken cancellationToken) {
        await using var scope = scopeFactory.CreateAsyncScope();
        var settings = scope.ServiceProvider.GetRequiredService<SettingsService>();
        var queue = scope.ServiceProvider.GetRequiredService<IJobQueueService>();

        var config = await settings.GetLibraryConfigAsync(cancellationToken);
        if (!config.Settings.AutoScanEnabled || config.Settings.ScanIntervalMinutes <= 0) {
            return;
        }

        var scanInterval = TimeSpan.FromMinutes(config.Settings.ScanIntervalMinutes);
        var now = DateTimeOffset.UtcNow;

        foreach (var root in config.Roots) {
            if (!root.Enabled) {
                continue;
            }

            var lastScanned = root.LastScannedAt;
            if (lastScanned is not null && now - lastScanned < scanInterval) {
                continue;
            }

            var rootId = root.Id.ToString();

            if (root.ScanVideos) {
                await EnqueueScanIfNeeded(queue, JobType.ScanLibrary, rootId, root.Label, cancellationToken);
            }

            if (root.ScanImages) {
                await EnqueueScanIfNeeded(queue, JobType.ScanGallery, rootId, root.Label, cancellationToken);
            }

            if (root.ScanAudio) {
                await EnqueueScanIfNeeded(queue, JobType.ScanAudio, rootId, root.Label, cancellationToken);
            }

            if (root.ScanBooks) {
                await EnqueueScanIfNeeded(queue, JobType.ScanBook, rootId, root.Label, cancellationToken);
            }
        }
    }

    private async Task EnqueueScanIfNeeded(
        IJobQueueService queue,
        JobType type,
        string rootId,
        string label,
        CancellationToken cancellationToken) {
        if (await queue.HasPendingAsync(type, rootId, cancellationToken)) {
            return;
        }

        var request = new EnqueueJobRequest(
            Type: type,
            PayloadJson: new ScanRootPayload(Guid.Parse(rootId)).ToJson(),
            TargetEntityKind: "library-root",
            TargetEntityId: rootId,
            TargetLabel: label);

        await queue.EnqueueAsync(request, cancellationToken);
        logger.LogInformation("Scheduled {JobType} for root '{Label}'.", type.ToCode(), label);
    }
}
