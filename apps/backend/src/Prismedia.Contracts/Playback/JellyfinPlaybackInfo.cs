using System.Text.Json.Serialization;
using Prismedia.Contracts.Jellyfin;

namespace Prismedia.Contracts.Playback;

/// <summary>
/// Jellyfin-compatible playback negotiation request sent before a client opens media.
/// </summary>
public sealed record PlaybackInfoRequest {
    [JsonPropertyName("UserId")]
    [JsonConverter(typeof(JellyfinNullableGuidConverter))]
    public Guid? UserId { get; init; }

    [JsonPropertyName("StartTimeTicks")]
    public long? StartTimeTicks { get; init; }

    [JsonPropertyName("AudioStreamIndex")]
    public int? AudioStreamIndex { get; init; }

    [JsonPropertyName("SubtitleStreamIndex")]
    public int? SubtitleStreamIndex { get; init; }

    [JsonPropertyName("MaxStreamingBitrate")]
    public int? MaxStreamingBitrate { get; init; }

    [JsonPropertyName("EnableDirectPlay")]
    public bool? EnableDirectPlay { get; init; }

    [JsonPropertyName("EnableDirectStream")]
    public bool? EnableDirectStream { get; init; }

    [JsonPropertyName("EnableTranscoding")]
    public bool? EnableTranscoding { get; init; }

    /// <summary>
    /// Prismedia extension indicating that the client can locally tone-map source video that is
    /// unsafe to copy into the platform-native HDR renderer.
    /// </summary>
    [JsonPropertyName("EnableClientToneMapping")]
    public bool? EnableClientToneMapping { get; init; }

    [JsonPropertyName("MediaSourceId")]
    public string? MediaSourceId { get; init; }

    [JsonPropertyName("PlaySessionId")]
    public string? PlaySessionId { get; init; }

    [JsonPropertyName("SupportedVideoRangeTypes")]
    public IReadOnlyList<string>? SupportedVideoRangeTypes { get; init; }

    [JsonPropertyName("DeviceProfile")]
    public DeviceProfileRequest? DeviceProfile { get; init; }
}

/// <summary>
/// Jellyfin-compatible device profile. Only the fields used by playback negotiation are modeled;
/// unknown fields a client sends are ignored.
/// </summary>
public sealed record DeviceProfileRequest {
    [JsonPropertyName("MaxStreamingBitrate")]
    public int? MaxStreamingBitrate { get; init; }

    [JsonPropertyName("MaxStaticBitrate")]
    public int? MaxStaticBitrate { get; init; }

    [JsonPropertyName("DirectPlayProfiles")]
    public IReadOnlyList<DirectPlayProfileRequest>? DirectPlayProfiles { get; init; }
}

/// <summary>
/// One directly playable container/codec combination from a Jellyfin device profile.
/// </summary>
public sealed record DirectPlayProfileRequest {
    [JsonPropertyName("Type")]
    public string? Type { get; init; }

    [JsonPropertyName("Container")]
    public string? Container { get; init; }

    [JsonPropertyName("VideoCodec")]
    public string? VideoCodec { get; init; }

    [JsonPropertyName("AudioCodec")]
    public string? AudioCodec { get; init; }
}

/// <summary>
/// Jellyfin-compatible response describing playable sources and the assigned play session.
/// </summary>
public sealed record PlaybackInfoResponse(
    [property: JsonPropertyName("PlaySessionId")] string PlaySessionId,
    [property: JsonPropertyName("MediaSources")] IReadOnlyList<MediaSourceInfo> MediaSources,
    [property: JsonPropertyName("ErrorCode")] string? ErrorCode = null);

/// <summary>
/// Source-file description modeled after Jellyfin's MediaSourceInfo shape.
/// </summary>
public sealed record MediaSourceInfo(
    [property: JsonPropertyName("Id")] string Id,
    [property: JsonPropertyName("Path")] string Path,
    [property: JsonPropertyName("Protocol")] string Protocol,
    [property: JsonPropertyName("Container")] string? Container,
    [property: JsonPropertyName("Size")] long? Size,
    [property: JsonPropertyName("Name")] string? Name,
    [property: JsonPropertyName("RunTimeTicks")] long? RunTimeTicks,
    [property: JsonPropertyName("SupportsDirectPlay")] bool SupportsDirectPlay,
    [property: JsonPropertyName("SupportsDirectStream")] bool SupportsDirectStream,
    [property: JsonPropertyName("SupportsTranscoding")] bool SupportsTranscoding,
    [property: JsonPropertyName("TranscodingUrl")] string? TranscodingUrl,
    [property: JsonPropertyName("TranscodingSubProtocol")] string? TranscodingSubProtocol,
    [property: JsonPropertyName("TranscodingContainer")] string? TranscodingContainer,
    [property: JsonPropertyName("MediaStreams")] IReadOnlyList<MediaStreamInfo> MediaStreams,
    [property: JsonPropertyName("TranscodingInfo")] TranscodingInfo? TranscodingInfo);

/// <summary>
/// Stream metadata for video, audio, subtitle, chapter, and attachment streams.
/// </summary>
public sealed record MediaStreamInfo(
    [property: JsonPropertyName("Index")] int Index,
    [property: JsonPropertyName("Type")] string Type,
    [property: JsonPropertyName("Codec")] string? Codec,
    [property: JsonPropertyName("Language")] string? Language,
    [property: JsonPropertyName("DisplayTitle")] string? DisplayTitle,
    [property: JsonPropertyName("Width")] int? Width,
    [property: JsonPropertyName("Height")] int? Height,
    [property: JsonPropertyName("AverageFrameRate")] double? AverageFrameRate,
    [property: JsonPropertyName("BitRate")] int? BitRate,
    [property: JsonPropertyName("SampleRate")] int? SampleRate,
    [property: JsonPropertyName("Channels")] int? Channels,
    [property: JsonPropertyName("IsDefault")] bool IsDefault = false,
    [property: JsonPropertyName("IsForced")] bool IsForced = false,
    [property: JsonPropertyName("VideoRange")] string? VideoRange = null,
    [property: JsonPropertyName("VideoRangeType")] string? VideoRangeType = null,
    [property: JsonPropertyName("PixelFormat")] string? PixelFormat = null,
    [property: JsonPropertyName("BitDepth")] int? BitDepth = null,
    [property: JsonPropertyName("ColorRange")] string? ColorRange = null,
    [property: JsonPropertyName("ColorSpace")] string? ColorSpace = null,
    [property: JsonPropertyName("ColorTransfer")] string? ColorTransfer = null,
    [property: JsonPropertyName("ColorPrimaries")] string? ColorPrimaries = null,
    [property: JsonPropertyName("DvProfile")] int? DvProfile = null,
    [property: JsonPropertyName("DvLevel")] int? DvLevel = null,
    [property: JsonPropertyName("RpuPresentFlag")] bool? RpuPresentFlag = null,
    [property: JsonPropertyName("ElPresentFlag")] bool? ElPresentFlag = null,
    [property: JsonPropertyName("BlPresentFlag")] bool? BlPresentFlag = null,
    [property: JsonPropertyName("DvBlSignalCompatibilityId")] int? DvBlSignalCompatibilityId = null,
    [property: JsonPropertyName("Hdr10PlusPresentFlag")] bool Hdr10PlusPresentFlag = false);

/// <summary>
/// Summary of the server-side media transformation selected during negotiation.
/// </summary>
public sealed record TranscodingInfo(
    [property: JsonPropertyName("Container")] string Container,
    [property: JsonPropertyName("VideoCodec")] string VideoCodec,
    [property: JsonPropertyName("AudioCodec")] string AudioCodec,
    [property: JsonPropertyName("Protocol")] string Protocol,
    [property: JsonPropertyName("IsVideoDirect")] bool IsVideoDirect,
    [property: JsonPropertyName("IsAudioDirect")] bool IsAudioDirect);
