using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Prismedia.Application.Entities;

namespace Prismedia.Application.Jobs;

/// <summary>
/// Background sweep keeping generated-asset file rows truthful: rows whose file vanished from the
/// cache volume are removed, so the request path can trust the database instead of checking the
/// filesystem per card. One pass shortly after startup clears historical drift (including a wiped
/// cache volume), then a daily cadence catches anything new.
/// </summary>
public sealed class EntityAssetRowSweepWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<EntityAssetRowSweepWorker> logger) : BackgroundService {
    private static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan SweepInterval = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        try {
            await Task.Delay(InitialDelay, stoppingToken);
        } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
            return;
        }

        while (!stoppingToken.IsCancellationRequested) {
            try {
                await using var scope = scopeFactory.CreateAsyncScope();
                var sweeper = scope.ServiceProvider.GetRequiredService<IEntityAssetRowSweeper>();
                await sweeper.SweepAsync(stoppingToken);
            } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                break;
            } catch (Exception exception) {
                logger.LogError(exception, "Entity asset row sweep failed.");
            }

            try {
                await Task.Delay(SweepInterval, stoppingToken);
            } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                break;
            }
        }
    }
}
