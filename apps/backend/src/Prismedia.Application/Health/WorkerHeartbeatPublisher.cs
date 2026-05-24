using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Prismedia.Application.Health;

/// <summary>
/// Hosted service that periodically publishes a heartbeat for the worker process.
/// </summary>
public sealed class WorkerHeartbeatPublisher(
    IWorkerHeartbeatStore heartbeatStore,
    WorkerRuntimeIdentity workerIdentity,
    ILogger<WorkerHeartbeatPublisher> logger) : BackgroundService {
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Publishes a worker heartbeat until the host shuts down.
    /// </summary>
    /// <param name="stoppingToken">Token triggered when the host is shutting down.</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        while (!stoppingToken.IsCancellationRequested) {
            try {
                await heartbeatStore.WriteAsync(workerIdentity.WorkerId, DateTimeOffset.UtcNow, stoppingToken);
            } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                throw;
            } catch (Exception ex) {
                logger.LogWarning(ex, "Failed to publish worker heartbeat.");
            }

            await Task.Delay(HeartbeatInterval, stoppingToken);
        }
    }
}
