using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Prismedia.Application.Integrations;

namespace Prismedia.Application.Jobs;

/// <summary>Runs the bounded daily retention sweep for completed integration artifact staging.</summary>
public sealed class IntegrationArtifactCleanupWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    ILogger<IntegrationArtifactCleanupWorker> logger) : BackgroundService {
    private static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan SweepInterval = TimeSpan.FromHours(24);
    private static readonly TimeSpan Retention = TimeSpan.FromHours(24);
    private static readonly TimeSpan SweepBudget = TimeSpan.FromMinutes(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        try { await Task.Delay(InitialDelay, stoppingToken); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }

        while (!stoppingToken.IsCancellationRequested) {
            try {
                await using var scope = scopeFactory.CreateAsyncScope();
                var maintenance = scope.ServiceProvider.GetRequiredService<IIntegrationArtifactStagingMaintenance>();
                using var budget = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                budget.CancelAfter(SweepBudget);
                await maintenance.SweepAsync(clock.GetUtcNow() - Retention, budget.Token);
            } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                break;
            } catch (OperationCanceledException) {
                logger.LogWarning("Integration artifact staging sweep reached its time budget.");
            } catch (Exception exception) {
                logger.LogError(exception, "Integration artifact staging sweep failed.");
            }

            try { await Task.Delay(SweepInterval, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
