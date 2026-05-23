using System.Text.Json;
using System.Text.Json.Serialization;

namespace Prismedia.Application.Jobs;

/// <summary>
/// Payload carried by root-scoped scan jobs.
/// </summary>
/// <param name="RootId">Library root identifier to scan.</param>
public sealed record ScanRootPayload([property: JsonPropertyName("rootId")] Guid RootId) {
    /// <summary>
    /// Serializes the payload using the public scan-job wire shape.
    /// </summary>
    /// <returns>JSON payload accepted by scan job handlers.</returns>
    public string ToJson() => JsonSerializer.Serialize(this);

    /// <summary>
    /// Parses the current scan payload shape and the earlier scheduler shape
    /// used during the migration.
    /// </summary>
    /// <param name="payloadJson">Job payload JSON.</param>
    /// <param name="payload">Parsed payload when available.</param>
    /// <returns>True when the payload identifies one library root.</returns>
    public static bool TryParse(string? payloadJson, out ScanRootPayload payload) {
        payload = default!;

        if (string.IsNullOrWhiteSpace(payloadJson) || payloadJson == "{}") {
            return false;
        }

        try {
            using var doc = JsonDocument.Parse(payloadJson);
            var root = doc.RootElement;
            if (TryReadGuid(root, "rootId", out var rootId) ||
                TryReadGuid(root, "libraryRootId", out rootId)) {
                payload = new ScanRootPayload(rootId);
                return true;
            }
        } catch (JsonException) {
            return false;
        }

        return false;
    }

    private static bool TryReadGuid(JsonElement root, string propertyName, out Guid value) {
        value = default;
        return root.TryGetProperty(propertyName, out var prop) && prop.TryGetGuid(out value);
    }
}
