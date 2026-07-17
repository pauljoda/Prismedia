using System.Text.Json.Serialization;

namespace Prismedia.Application.Videos;

/// <summary>
/// Application request for Jellyfin-compatible playback negotiation.
/// </summary>
public sealed record PlaybackInfoQuery {
    public Guid? UserId { get; init; }
    public long? StartTimeTicks { get; init; }
    public int? AudioStreamIndex { get; init; }
    public int? SubtitleStreamIndex { get; init; }
    public int? MaxStreamingBitrate { get; init; }
    public bool? EnableDirectPlay { get; init; }
    public bool? EnableDirectStream { get; init; }
    public bool? EnableTranscoding { get; init; }
    public bool? EnableClientToneMapping { get; init; }
    public string? MediaSourceId { get; init; }
    public string? PlaySessionId { get; init; }
    public IReadOnlyList<string>? SupportedVideoRangeTypes { get; init; }
    public string? AccessToken { get; init; }

    /// <summary>
    /// Client device profile describing which container/codec combinations the client can play
    /// directly. When present it drives the DirectPlay/Remux/Transcode decision; when null the
    /// server falls back to the container extension heuristic.
    /// </summary>
    public ClientPlaybackProfile? Profile { get; init; }
}

/// <summary>
/// Subset of a Jellyfin device profile that the playback negotiator needs to choose a delivery
/// method: the directly playable container/codec combinations and the client bitrate ceiling.
/// </summary>
/// <param name="MaxStreamingBitrate">Maximum bits per second the client will accept, or null when unbounded.</param>
/// <param name="DirectPlayProfiles">Container/codec combinations the client can play without transcoding.</param>
public sealed record ClientPlaybackProfile(
    int? MaxStreamingBitrate,
    IReadOnlyList<ClientDirectPlayProfile> DirectPlayProfiles);

/// <summary>
/// One directly playable container/codec combination advertised by a client.
/// </summary>
/// <param name="Type">Profile media type, such as Video, or null when unspecified.</param>
/// <param name="Container">Comma-separated containers the client accepts (Jellyfin convention).</param>
/// <param name="VideoCodec">Comma-separated video codecs the client can decode, or null/empty for any.</param>
/// <param name="AudioCodec">Comma-separated audio codecs the client can decode, or null/empty for any.</param>
public sealed record ClientDirectPlayProfile(
    string? Type,
    string? Container,
    string? VideoCodec,
    string? AudioCodec);

/// <summary>
/// Application result describing playable media sources for one playback session.
/// </summary>
public sealed record PlaybackInfoResult(
    string PlaySessionId,
    IReadOnlyList<MediaSourceInfoResult> MediaSources,
    string? ErrorCode = null);

/// <summary>
/// Application result describing one playable media source.
/// </summary>
public sealed record MediaSourceInfoResult(
    string Id,
    string Path,
    string Protocol,
    string? Container,
    long? Size,
    string? Name,
    long? RunTimeTicks,
    bool SupportsDirectPlay,
    bool SupportsDirectStream,
    bool SupportsTranscoding,
    string? TranscodingUrl,
    string? TranscodingSubProtocol,
    string? TranscodingContainer,
    IReadOnlyList<MediaStreamInfoResult> MediaStreams,
    TranscodingInfoResult? TranscodingInfo);

/// <summary>
/// Application result for one media stream.
/// </summary>
public sealed record MediaStreamInfoResult(
    int Index,
    string Type,
    string? Codec,
    string? Language,
    string? DisplayTitle,
    int? Width,
    int? Height,
    double? AverageFrameRate,
    int? BitRate,
    int? SampleRate,
    int? Channels,
    bool IsDefault = false,
    bool IsForced = false,
    string? VideoRange = null,
    string? VideoRangeType = null,
    string? PixelFormat = null,
    int? BitDepth = null,
    string? ColorRange = null,
    string? ColorSpace = null,
    string? ColorTransfer = null,
    string? ColorPrimaries = null,
    int? DvProfile = null,
    int? DvLevel = null,
    bool? RpuPresentFlag = null,
    bool? ElPresentFlag = null,
    bool? BlPresentFlag = null,
    int? DvBlSignalCompatibilityId = null,
    bool Hdr10PlusPresentFlag = false);

/// <summary>
/// Application result describing a selected transcoding strategy.
/// </summary>
public sealed record TranscodingInfoResult(
    string Container,
    string VideoCodec,
    string AudioCodec,
    string Protocol,
    bool IsVideoDirect,
    bool IsAudioDirect);

/// <summary>
/// Application command for Jellyfin-compatible playback session events.
/// </summary>
public sealed record PlaybackSessionCommand {
    public Guid ItemId { get; init; }
    public string? MediaSourceId { get; init; }
    public string? PlaySessionId { get; init; }
    public long? PositionTicks { get; init; }
    public bool? IsPaused { get; init; }
    public bool? IsMuted { get; init; }
}

/// <summary>
/// Application result for playback state returned by mark-played calls.
/// </summary>
public sealed record UserItemDataResult(
    bool Played,
    int? PlayCount = null,
    long? PlaybackPositionTicks = null);
