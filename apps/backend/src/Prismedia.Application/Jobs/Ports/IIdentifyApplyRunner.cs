using System.Text.Json;
using Prismedia.Contracts.Plugins;

namespace Prismedia.Application.Jobs.Ports;

/// <summary>Runs a reviewed identify proposal after its durable review signal is closed.</summary>
public interface IIdentifyApplyRunner {
    /// <summary>Applies the reviewed proposal when the queue item still belongs to this graph.</summary>
    Task RunAsync(
        IdentifyApplyPayload payload,
        Guid graphId,
        bool isFinalAttempt,
        CancellationToken cancellationToken);
}

/// <summary>Durable payload for applying one reviewed identify queue proposal.</summary>
public sealed record IdentifyApplyPayload(
    Guid EntityId,
    ApplyIdentifyQueueItemRequest Request) {
    public string ToJson() => JsonSerializer.Serialize(this);

    public static IdentifyApplyPayload Parse(string payloadJson) =>
        JsonSerializer.Deserialize<IdentifyApplyPayload>(payloadJson)
            ?? throw new InvalidOperationException("Identify apply payload is missing or invalid.");
}
