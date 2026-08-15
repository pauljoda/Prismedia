using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Prismedia.Application.Entities;

namespace Prismedia.Application.Jobs;

/// <summary>
/// Periodic safety net for the trigger-maintained Entity availability projection. The first pass is
/// delayed so deployment startup and migrations retain priority; ordinary mutations remain current
/// immediately through database triggers.
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
            } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                break;
            } catch (Exception exception) {
                logger.LogError(exception, "Entity availability reconciliation failed.");
            }

            try {
                await Task.Delay(ReconcileInterval, stoppingToken);
            } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                break;
            }
        }
    }
}
