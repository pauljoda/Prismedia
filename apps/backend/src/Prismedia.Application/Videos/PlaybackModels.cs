using Prismedia.Domain.Entities;

namespace Prismedia.Application.Videos;

/// <summary>Application request for a native Prismedia video playback plan.</summary>
public sealed record VideoPlaybackPlanQuery {
    public int? AudioStreamIndex { get; init; }
    public bool? EnableDirectPlay { get; init; }
    public bool? EnableDirectStream { get; init; }
    public bool? EnableTranscoding { get; init; }
    public bool? EnableClientToneMapping { get; init; }
    public string? SessionId { get; init; }
    public IReadOnlyList<string>? SupportedVideoRangeTypes { get; init; }
    public string? AccessToken { get; init; }

    /// <summary>
    /// Client device profile describing which container/codec combinations the client can play
    /// directly. When present it drives the DirectPlay/Remux/Transcode decision; when null the
    /// server falls back to the container extension heuristic.
    /// </summary>
    public VideoPlaybackProfile? Profile { get; init; }
}

/// <summary>
/// Client capabilities the playback planner needs to choose a delivery method.
/// </summary>
/// <param name="MaxStreamingBitrate">Maximum bits per second the client will accept, or null when unbounded.</param>
/// <param name="DirectPlayProfiles">Container/codec combinations the client can play without transcoding.</param>
public sealed record VideoPlaybackProfile(
    int? MaxStreamingBitrate,
    IReadOnlyList<VideoDirectPlayProfile> DirectPlayProfiles);

/// <summary>
/// One directly playable container/codec combination advertised by a client.
/// </summary>
/// <param name="Type">Profile media type, such as Video, or null when unspecified.</param>
/// <param name="Container">Comma-separated containers the client accepts.</param>
/// <param name="VideoCodec">Comma-separated video codecs the client can decode, or null/empty for any.</param>
/// <param name="AudioCodec">Comma-separated audio codecs the client can decode, or null/empty for any.</param>
public sealed record VideoDirectPlayProfile(
    string? Type,
    string? Container,
    string? VideoCodec,
    string? AudioCodec);

/// <summary>
/// Application result containing the selected source for one playback session.
/// </summary>
public sealed record VideoPlaybackPlanResult(
    string SessionId,
    VideoPlaybackSourceResult Source);

/// <summary>
/// Application result describing one playable media source.
/// </summary>
public sealed record VideoPlaybackSourceResult(
    string Id,
    string? Container,
    double? DurationSeconds,
    VideoPlaybackMethod Method,
    string Url,
    bool SupportsTranscoding,
    IReadOnlyList<VideoPlaybackStreamResult> Streams,
    VideoTranscodingResult? Transcoding);

/// <summary>
/// Application result for one media stream.
/// </summary>
public sealed record VideoPlaybackStreamResult(
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
public sealed record VideoTranscodingResult(
    string Container,
    string VideoCodec,
    string AudioCodec,
    bool IsVideoDirect,
    bool IsAudioDirect);

/// <summary>
/// Application command for a native playback-session event.
/// </summary>
public sealed record VideoPlaybackSessionCommand {
    public Guid EntityId { get; init; }
    public string? SessionId { get; init; }
    public double? PositionSeconds { get; init; }
    public double? DurationSeconds { get; init; }
    public bool? Completed { get; init; }
    public double? ActivitySeconds { get; init; }
    public int? UtcOffsetMinutes { get; init; }
}
