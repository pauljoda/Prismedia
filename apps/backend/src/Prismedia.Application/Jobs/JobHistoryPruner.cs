using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Prismedia.Application.Jobs;

/// <summary>
/// Background service that periodically deletes old completed and cancelled job runs
/// to prevent the job_runs table from growing indefinitely.
/// </summary>
public sealed class JobHistoryPruner(
    IServiceScopeFactory scopeFactory,
    ILogger<JobHistoryPruner> logger) : BackgroundService {
    private static readonly TimeSpan PruneInterval = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan Retention = TimeSpan.FromDays(7);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        logger.LogInformation("Job history pruner started (retention: {Days} days).", Retention.TotalDays);

        while (!stoppingToken.IsCancellationRequested) {
            try {
                await using var scope = scopeFactory.CreateAsyncScope();
                var queue = scope.ServiceProvider.GetRequiredService<IJobQueueService>();
                var pruned = await queue.PruneHistoryAsync(Retention, stoppingToken);

                if (pruned > 0) {
                    logger.LogInformation("Pruned {Count} old job runs.", pruned);
                }
            } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                break;
            } catch (Exception ex) {
                logger.LogError(ex, "Job history prune failed.");
            }

            await Task.Delay(PruneInterval, stoppingToken);
        }
    }
}
