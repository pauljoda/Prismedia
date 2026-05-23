using System.Text.Json.Serialization;

namespace Prismedia.Contracts.Playback;

/// <summary>
/// Jellyfin-compatible playback event payload for start, progress, ping, and stop calls.
/// </summary>
public sealed record PlaybackSessionRequest {
    [JsonPropertyName("ItemId")]
    public Guid ItemId { get; init; }

    [JsonPropertyName("MediaSourceId")]
    public string? MediaSourceId { get; init; }

    [JsonPropertyName("PlaySessionId")]
    public string? PlaySessionId { get; init; }

    [JsonPropertyName("PositionTicks")]
    public long? PositionTicks { get; init; }

    [JsonPropertyName("IsPaused")]
    public bool? IsPaused { get; init; }

    [JsonPropertyName("IsMuted")]
    public bool? IsMuted { get; init; }
}

/// <summary>
/// Jellyfin-compatible user item state returned when marking media played or unplayed.
/// </summary>
public sealed record UserItemData(
    [property: JsonPropertyName("Played")] bool Played,
    [property: JsonPropertyName("PlayCount")] int? PlayCount = null,
    [property: JsonPropertyName("PlaybackPositionTicks")] long? PlaybackPositionTicks = null);
