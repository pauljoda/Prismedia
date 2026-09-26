using System.Text.Json;

namespace Prismedia.Application.Jobs;

/// <summary>Execution policy carried by an auto-identify job.</summary>
/// <param name="AllowChildTarget">Whether this job explicitly targets a child entity.</param>
/// <param name="IgnoreOrganizedGate">Whether an already-organized explicit target may be refreshed.</param>
/// <param name="ExternalPeopleHoldingId">Connected holding requesting exact credits-only enrichment.</param>
/// <param name="ExternalPeopleFingerprint">Current identity/provider fingerprint for bounded retries.</param>
public sealed record AutoIdentifyJobPayload(
    bool AllowChildTarget = false,
    bool IgnoreOrganizedGate = false,
    Guid? ExternalPeopleHoldingId = null,
    string? ExternalPeopleFingerprint = null) {
    /// <summary>Whether this payload delegates to exact, credits-only external-library enrichment.</summary>
    public bool IsExternalPeopleEnrichment =>
        ExternalPeopleHoldingId is not null &&
        !string.IsNullOrWhiteSpace(ExternalPeopleFingerprint);

    /// <summary>Serializes this payload for durable queue storage.</summary>
    public string ToJson() => JsonSerializer.Serialize(this);

    /// <summary>Parses a queue payload, preserving root-only defaults for legacy or malformed rows.</summary>
    public static AutoIdentifyJobPayload Parse(string? payloadJson) {
        if (string.IsNullOrWhiteSpace(payloadJson)) {
            return new AutoIdentifyJobPayload();
        }

        try {
            return JsonSerializer.Deserialize<AutoIdentifyJobPayload>(payloadJson)
                ?? new AutoIdentifyJobPayload();
        } catch (JsonException) {
            return new AutoIdentifyJobPayload();
        }
    }
}
