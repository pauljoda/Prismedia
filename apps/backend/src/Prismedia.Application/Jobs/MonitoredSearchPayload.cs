using System.Text.Json;
using System.Text.Json.Serialization;

namespace Prismedia.Application.Jobs;

/// <summary>Identifies the single durable monitor selected by the background monitor drainer.</summary>
public sealed record MonitoredSearchPayload(
    [property: JsonPropertyName("monitorId")] Guid MonitorId) {
    /// <summary>Serializes the payload stored on a monitored-search job.</summary>
    public string ToJson() => JsonSerializer.Serialize(this);

    /// <summary>Parses a current monitored-search payload; legacy empty sweep payloads return false.</summary>
    public static bool TryParse(string? payloadJson, out MonitoredSearchPayload payload) {
        payload = default!;
        if (string.IsNullOrWhiteSpace(payloadJson) || payloadJson == "{}") {
            return false;
        }

        try {
            var parsed = JsonSerializer.Deserialize<MonitoredSearchPayload>(payloadJson);
            if (parsed is { MonitorId: var monitorId } && monitorId != Guid.Empty) {
                payload = parsed;
                return true;
            }
        } catch (JsonException) {
            // Invalid durable payloads are treated as legacy sweeps and remain safely bounded to one item.
        }

        return false;
    }
}
