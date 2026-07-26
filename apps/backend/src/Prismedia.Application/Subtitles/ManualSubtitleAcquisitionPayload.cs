using System.Text.Json;

namespace Prismedia.Application.Subtitles;

/// <summary>Durable input for one user-selected subtitle provider download.</summary>
public sealed record ManualSubtitleAcquisitionPayload(
    Guid VideoId,
    string Provider,
    string CandidateId) {
    /// <summary>Serializes this provider-owned candidate reference for durable execution.</summary>
    public string ToJson() => JsonSerializer.Serialize(this);

    /// <summary>Parses and validates a queued manual subtitle acquisition.</summary>
    public static ManualSubtitleAcquisitionPayload Parse(string payloadJson) =>
        JsonSerializer.Deserialize<ManualSubtitleAcquisitionPayload>(payloadJson)
        ?? throw new InvalidOperationException("Manual subtitle acquisition payload is missing or invalid.");
}
