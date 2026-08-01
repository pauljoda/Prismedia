using System.Text.Json;
using Microsoft.Extensions.Logging;
using Prismedia.Application.Plugins;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers;

/// <summary>
/// Compatibility shim for legacy batch identify jobs that may still be queued across an upgrade.
/// Explodes the old batch payload into one identify-search request per entity (the current model)
/// and completes; new bulk requests never create this job type.
/// </summary>
[JobDefinition(JobType.BulkIdentify)]
public sealed class BulkIdentifyJobHandler(
    IIdentifyQueueService queue,
    ILogger<BulkIdentifyJobHandler> logger) : IJobHandler {
    public async Task HandleAsync(JobContext context, CancellationToken cancellationToken) {
        var payload = BulkIdentifyPayload.Parse(context.Job.PayloadJson);
        logger.LogInformation(
            "BulkIdentify: converting legacy batch of {Count} entities into per-entity identify-search jobs",
            payload.EntityIds.Count);

        var response = await queue.RequestSearchBatchAsync(
            payload.EntityIds,
            new IdentifyQueueSearchRequest(payload.Provider, payload.Query),
            payload.HideNsfw,
            cancellationToken);

        await context.ReportProgressAsync(
            100,
            $"Requeued {response.Enqueued}/{response.Requested} as identify-search jobs",
            cancellationToken);
    }
}

/// <summary>
/// Legacy batch identify payload, retained so historical job rows still parse.
/// </summary>
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
