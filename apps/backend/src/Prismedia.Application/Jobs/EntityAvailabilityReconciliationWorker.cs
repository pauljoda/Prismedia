using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Prismedia.Application.Entities;

namespace Prismedia.Application.Jobs;

/// <summary>
/// Periodic safety net for the trigger-maintained Entity read projections (availability and
/// rollups). The first pass is delayed so deployment startup and migrations retain priority;
/// ordinary mutations remain current immediately through database triggers. Repairs are logged
/// as warnings so persistent drift is visible instead of silently patched.
/// </summary>
public sealed class EntityAvailabilityReconciliationWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<EntityAvailabilityReconciliationWorker> logger) : BackgroundService {
    private static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ReconcileInterval = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        try {
            await Task.Delay(InitialDelay, stoppingToken);
        } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
            return;
        }

        while (!stoppingToken.IsCancellationRequested) {
            try {
                await using var scope = scopeFactory.CreateAsyncScope();
                var reconciler = scope.ServiceProvider.GetRequiredService<IEntityAvailabilityReconciler>();
                var repaired = await reconciler.ReconcileAsync(stoppingToken);
                if (repaired > 0) {
                    logger.LogWarning(
                        "Repaired {Count} drifted Entity availability snapshot(s).",
                        repaired);
                }

                var rollups = scope.ServiceProvider.GetRequiredService<IEntityRollupReconciler>();
                var repairedRollups = await rollups.ReconcileAsync(stoppingToken);
                if (repairedRollups > 0) {
                    logger.LogWarning(
                        "Repaired {Count} drifted Entity rollup row(s).",
                        repairedRollups);
                }
            } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                break;
            } catch (Exception exception) {
                logger.LogError(exception, "Entity projection reconciliation failed.");
            }

            try {
                await Task.Delay(ReconcileInterval, stoppingToken);
            } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                break;
            }
        }
    }
}
