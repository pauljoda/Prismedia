using System.Text.Json;
using Microsoft.Extensions.Logging;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers;

/// <summary>
/// Identifies multiple entities via a provider plugin, storing each result in the durable identify queue.
/// </summary>
public sealed class BulkIdentifyJobHandler(
    IBulkIdentifyProvider provider,
    AutoIdentifyConcurrencyGate gate,
    ILogger<BulkIdentifyJobHandler> logger,
    TimeSpan? identifyTimeout = null) : IJobHandler {
    private static readonly TimeSpan DefaultIdentifyTimeout = TimeSpan.FromSeconds(90);
    private readonly TimeSpan _identifyTimeout = identifyTimeout ?? DefaultIdentifyTimeout;

    public JobType Type => JobType.BulkIdentify;

    public async Task HandleAsync(JobContext context, CancellationToken cancellationToken) {
        var payload = BulkIdentifyPayload.Parse(context.Job.PayloadJson);
        var count = payload.EntityIds.Count;
        logger.LogInformation("BulkIdentify: starting {Count} entities with provider {Provider}", count, payload.Provider);

        using var lease = gate.TryEnter()
            ?? throw new JobRetryLaterException("Bulk identify provider slot busy.", TimeSpan.FromSeconds(5));

        for (var i = 0; i < count; i++) {
            cancellationToken.ThrowIfCancellationRequested();
            var entityId = payload.EntityIds[i];

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_identifyTimeout);
            try {
                await provider.SearchAndQueueAsync(entityId, payload.Provider, payload.Query, payload.HideNsfw, timeout.Token);
            } catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
                throw new JobRetryLaterException(
                    $"Bulk identify timed out after {_identifyTimeout.TotalSeconds:0} seconds.",
                    TimeSpan.FromMinutes(1));
            } catch (Exception ex) {
                logger.LogWarning(ex, "BulkIdentify: failed to identify entity {EntityId}", entityId);
            }

            await context.ReportProgressAsync((i + 1) * 100 / count, $"Identified {i + 1}/{count}", cancellationToken);
        }

        logger.LogInformation("BulkIdentify: completed {Count} entities", count);
    }
}

public sealed record BulkIdentifyPayload(
    IReadOnlyList<Guid> EntityIds,
    string Provider,
    IdentifyQuery? Query,
    bool HideNsfw) {
    public string ToJson() => JsonSerializer.Serialize(this);

    public static BulkIdentifyPayload Parse(string payloadJson) =>
        JsonSerializer.Deserialize<BulkIdentifyPayload>(payloadJson)
            ?? throw new InvalidOperationException("BulkIdentify payload is missing or invalid.");
}
