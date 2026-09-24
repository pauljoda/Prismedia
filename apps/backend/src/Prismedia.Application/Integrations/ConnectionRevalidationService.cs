using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Integrations;

/// <summary>Revalidates enabled connections whose negotiated authority was revoked by configuration or plugin changes.</summary>
public sealed class ConnectionRevalidationService(
    IServiceScopeFactory scopes,
    ILogger<ConnectionRevalidationService> logger) {
    #region Static Variables

    private const int BatchSize = 8;

    private const int Parallelism = 2;

    #endregion

    #region Variables

    private Guid? lastVisitedId;

    #endregion

    #region Actions - Revalidation

    /// <summary>
    /// Tests a bounded batch through the ordinary connection probe workflow. Each probe owns its own
    /// dependency scope so one connection failure cannot poison another connection's persistence unit.
    /// </summary>
    public async Task<ConnectionRevalidationResult> DrainBatchAsync(CancellationToken cancellationToken) {
        Guid[] pendingIds;
        var pendingCount = 0;
        await using (var scope = scopes.CreateAsyncScope()) {
            var candidates = (await scope.ServiceProvider.GetRequiredService<IIntegrationConnectionStore>()
                    .ListAsync(cancellationToken))
                .Where(item => item.Connection.State.Enabled
                    && item.Connection.State.Status == ConnectionStatus.Unverified)
                .OrderBy(item => item.Connection.State.Id)
                .Select(item => item.Connection.State.Id)
                .ToArray();
            pendingCount = candidates.Length;
            pendingIds = lastVisitedId is { } cursor
                ? candidates.Where(id => id.CompareTo(cursor) > 0)
                    .Concat(candidates.Where(id => id.CompareTo(cursor) <= 0))
                    .Take(BatchSize)
                    .ToArray()
                : candidates.Take(BatchSize).ToArray();
        }

        if (pendingIds.Length > 0) {
            lastVisitedId = pendingIds[^1];
        }

        var ready = 0;
        var failed = 0;
        var deferred = 0;
        await Parallel.ForEachAsync(
            pendingIds,
            new ParallelOptions { MaxDegreeOfParallelism = Parallelism, CancellationToken = cancellationToken },
            async (id, token) => {
                try {
                    await using var scope = scopes.CreateAsyncScope();
                    var store = scope.ServiceProvider.GetRequiredService<IIntegrationConnectionStore>();
                    var current = await store.FindAsync(id, token);
                    if (current is null || !current.Connection.State.Enabled
                        || current.Connection.State.Status != ConnectionStatus.Unverified) {
                        return;
                    }

                    var result = await scope.ServiceProvider.GetRequiredService<ConnectionService>()
                        .ProbeAsync(id, token);
                    if (result.Status == ConnectionStatus.Ready) {
                        Interlocked.Increment(ref ready);
                    } else {
                        Interlocked.Increment(ref failed);
                    }
                } catch (OperationCanceledException) when (token.IsCancellationRequested) {
                    throw;
                } catch (Exception error) {
                    Interlocked.Increment(ref deferred);
                    logger.LogWarning(error, "Automatic connection revalidation for {ConnectionId} will retry.", id);
                }
            });

        return new(ready, failed, deferred, pendingCount > pendingIds.Length);
    }

    #endregion
}
