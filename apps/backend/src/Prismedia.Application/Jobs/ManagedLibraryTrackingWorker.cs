using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Prismedia.Application.Integrations;

namespace Prismedia.Application.Jobs;

/// <summary>Recovers due tracking work from durable intent even after queue history has been removed.</summary>
public sealed class ManagedLibraryTrackingWorker(IServiceScopeFactory scopes, ILogger<ManagedLibraryTrackingWorker> logger) : BackgroundService {
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        try {
            while (await timer.WaitForNextTickAsync(stoppingToken)) {
                try {
                    await using var scope = scopes.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<IManagedTrackingStore>().QueueDueAsync(stoppingToken);
                } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception error) { logger.LogWarning(error, "Could not publish due connected-library reconciliation."); }
            }
        } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
