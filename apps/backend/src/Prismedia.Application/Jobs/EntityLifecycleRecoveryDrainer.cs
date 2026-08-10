using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Entities;

namespace Prismedia.Application.Jobs;

/// <summary>Result of one bounded lifecycle-recovery pass.</summary>
public readonly record struct EntityLifecycleRecoveryResult(int Attempted, int Resolved, int Failed);

/// <summary>
/// Resumes crash-interrupted Entity deletion and unmonitor claims serially. Each candidate is isolated:
/// a path that still needs operator attention is reported and the remaining claims continue.
/// </summary>
public sealed class EntityLifecycleRecoveryDrainer(
    IEntityLifecycleRecoveryStore recovery,
    IMediaEntityDeletionService deletion,
    IEntityUnmonitorService unmonitor,
    IAcquisitionTeardownRecovery acquisitionRecovery,
    ILogger<EntityLifecycleRecoveryDrainer> logger) {
    /// <summary>Processes at most <paramref name="limit"/> durable claims, strictly one at a time.</summary>
    public async Task<EntityLifecycleRecoveryResult> DrainBatchAsync(
        int limit,
        CancellationToken cancellationToken) {
        var batch = await recovery.ListAsync(limit, cancellationToken);
        var resolved = 0;
        var failed = 0;

        foreach (var entityId in batch.DeletingEntityIds) {
            var result = await deletion.DeleteAsync(entityId, deleteFiles: true, cancellationToken);
            if (result.Deleted || result.FailureKind == MediaEntityDeleteFailureKind.NotFound) {
                resolved++;
            } else {
                failed++;
                logger.LogWarning(
                    "Lifecycle recovery could not resume delete-files claim {EntityId}: {Reason}",
                    entityId,
                    result.Message ?? "unknown conflict");
            }
        }

        foreach (var monitorId in batch.OrphanedDeletingMonitorIds) {
            if (await recovery.CompleteOrphanedDeletionAsync(monitorId, cancellationToken)) {
                resolved++;
            } else {
                failed++;
                logger.LogWarning(
                    "Lifecycle recovery could not complete orphaned delete-files monitor {MonitorId}",
                    monitorId);
            }
        }

        foreach (var monitorId in batch.StoppingMonitorIds) {
            var result = await unmonitor.StopAsync(monitorId, cancellationToken);
            if (result.Stopped || !result.Found) {
                resolved++;
            } else {
                failed++;
                logger.LogWarning(
                    "Lifecycle recovery could not resume stopping monitor {MonitorId}: {Reason}",
                    monitorId,
                    result.Message ?? "unknown conflict");
            }
        }

        foreach (var acquisitionId in batch.OrphanedStoppingAcquisitionIds) {
            try {
                if (await acquisitionRecovery.CompleteOrphanedEntityRemovalAsync(acquisitionId, cancellationToken)) {
                    resolved++;
                } else {
                    failed++;
                    logger.LogWarning(
                        "Lifecycle recovery could not complete orphaned stopping acquisition {AcquisitionId}",
                        acquisitionId);
                }
            } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                throw;
            } catch (Exception exception) {
                failed++;
                logger.LogWarning(
                    exception,
                    "Lifecycle recovery failed to complete orphaned stopping acquisition {AcquisitionId}",
                    acquisitionId);
            }
        }

        return new EntityLifecycleRecoveryResult(batch.Count, resolved, failed);
    }
}

/// <summary>
/// Continuously drains already-durable lifecycle work independently of scan and acquisition timers.
/// A blocked batch backs off, while successful batches continue immediately until the backlog is empty.
/// </summary>
internal sealed class EntityLifecycleRecoveryWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<EntityLifecycleRecoveryWorker> logger) : BackgroundService {
    private const int BatchSize = 32;
    private static readonly TimeSpan IdleDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan BlockedDelay = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        logger.LogInformation("Entity lifecycle recovery worker started.");
        while (!stoppingToken.IsCancellationRequested) {
            EntityLifecycleRecoveryResult result;
            try {
                await using var scope = scopeFactory.CreateAsyncScope();
                var drainer = scope.ServiceProvider.GetRequiredService<EntityLifecycleRecoveryDrainer>();
                result = await drainer.DrainBatchAsync(BatchSize, stoppingToken);
            } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                break;
            } catch (Exception exception) {
                logger.LogError(exception, "Entity lifecycle recovery pass failed.");
                await DelayAsync(BlockedDelay, stoppingToken);
                continue;
            }

            if (result.Attempted == 0) {
                await DelayAsync(IdleDelay, stoppingToken);
            } else if (result.Resolved == 0) {
                await DelayAsync(BlockedDelay, stoppingToken);
            }
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
