using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Prismedia.Application.Integrations;

namespace Prismedia.Application.Jobs;

/// <summary>Recovers durable unverified connection state after plugin updates and process restarts.</summary>
public sealed class ConnectionRevalidationWorker(
    ConnectionRevalidationService revalidation,
    ILogger<ConnectionRevalidationWorker> logger) : BackgroundService {
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        while (!stoppingToken.IsCancellationRequested) {
            var delay = TimeSpan.FromSeconds(30);
            try {
                var result = await revalidation.DrainBatchAsync(stoppingToken);
                if (result.Ready + result.Failed + result.Deferred > 0) {
                    logger.LogInformation(
                        "Automatic connection revalidation completed: {Ready} ready, {Failed} unavailable, {Deferred} deferred.",
                        result.Ready,
                        result.Failed,
                        result.Deferred);
                }
                if (result.MayHaveMore && result.Ready + result.Failed > 0)
                    delay = TimeSpan.FromSeconds(1);
            } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                break;
            } catch (Exception error) {
                logger.LogWarning(error, "Automatic connection revalidation pass failed; it will retry.");
            }

            try {
                await Task.Delay(delay, stoppingToken);
            } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                break;
            }
        }
    }
}
