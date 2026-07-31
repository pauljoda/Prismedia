using Prismedia.Domain.Entities;

namespace Prismedia.Contracts.Playback;

/// <summary>Stable route and query vocabulary for Prismedia video playback.</summary>
public static class VideoPlaybackProtocol {
    /// <summary>Native playback API route prefix.</summary>
    public const string RoutePrefix = "/api/playback";

    /// <summary>Selected source audio stream query key.</summary>
    public const string AudioStreamIndexQuery = "audioStreamIndex";

    /// <summary>Whether a remux should copy the selected audio stream.</summary>
    public const string CopyAudioQuery = "copyAudio";

    /// <summary>Stable HLS asset names shared by the plan and asset services.</summary>
    public static class Hls {
        /// <summary>Adaptive master playlist.</summary>
        public const string MasterPlaylist = "master.m3u8";

        /// <summary>Single-variant playlist retained for direct rendition access.</summary>
        public const string MainPlaylist = "main.m3u8";

        /// <summary>Public rendition playlist name.</summary>
        public const string StreamPlaylist = "stream.m3u8";

        /// <summary>Cache-local rendition playlist name.</summary>
        public const string IndexPlaylist = "index.m3u8";
    }

    /// <summary>Builds the video source route for an entity.</summary>
    public static string SourcePath(Guid entityId) =>
        $"{RoutePrefix}/videos/{entityId:D}/stream";

    /// <summary>Builds the HLS route for an entity and relative asset.</summary>
    public static string HlsPath(Guid entityId, string asset) =>
        $"{RoutePrefix}/videos/{entityId:D}/hls/{asset.TrimStart('/')}";

    /// <summary>Builds the trickplay playlist route for an entity and width.</summary>
    public static string TrickplayPlaylistPath(Guid entityId, int width) =>
        $"{RoutePrefix}/videos/{entityId:D}/trickplay/{width}/tiles.m3u8";
}

/// <summary>Client capabilities used to select a video delivery plan.</summary>
public sealed record VideoPlaybackPlanRequest {
    /// <summary>Preferred source audio stream index.</summary>
    public int? AudioStreamIndex { get; init; }

    /// <summary>Whether the original source file may be delivered unchanged.</summary>
    public bool? EnableDirectPlay { get; init; }

    /// <summary>Whether the source may be remuxed without re-encoding its video.</summary>
    public bool? EnableDirectStream { get; init; }

    /// <summary>Whether the server may transcode the source.</summary>
    public bool? EnableTranscoding { get; init; }

    /// <summary>Whether the client can locally tone-map malformed HDR samples.</summary>
    public bool? EnableClientToneMapping { get; init; }

    /// <summary>Existing playback session id to continue.</summary>
    public string? SessionId { get; init; }

    /// <summary>Dynamic-range codes the client can render directly.</summary>
    public IReadOnlyList<string>? SupportedVideoRangeTypes { get; init; }

    /// <summary>Container and codec combinations the client can decode.</summary>
    public VideoPlaybackProfileRequest? Profile { get; init; }
}

/// <summary>Client bitrate ceiling and directly playable media combinations.</summary>
public sealed record VideoPlaybackProfileRequest(
    int? MaxStreamingBitrate,
    IReadOnlyList<VideoDirectPlayProfileRequest>? DirectPlayProfiles);

/// <summary>One container and codec combination a client can play without video transcoding.</summary>
public sealed record VideoDirectPlayProfileRequest(
    string? Type,
    string? Container,
    string? VideoCodec,
    string? AudioCodec);

/// <summary>The selected source and assigned session for video playback.</summary>
public sealed record VideoPlaybackPlanResponse(
    string SessionId,
    VideoPlaybackSource Source);

/// <summary>One selected video source and the URL through which Prismedia will deliver it.</summary>
public sealed record VideoPlaybackSource(
    string Id,
    string? Container,
    double? DurationSeconds,
    VideoPlaybackMethod Method,
    string Url,
    bool SupportsTranscoding,
    IReadOnlyList<VideoPlaybackStream> Streams,
    VideoTranscodingInfo? Transcoding);

/// <summary>Probed metadata for one stream in the selected video source.</summary>
public sealed record VideoPlaybackStream(
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

/// <summary>Output codecs selected when Prismedia remuxes or transcodes a source.</summary>
public sealed record VideoTranscodingInfo(
    string Container,
    string VideoCodec,
    string AudioCodec,
    bool IsVideoDirect,
    bool IsAudioDirect);

/// <summary>Playback-session progress sent by video clients.</summary>
public sealed record VideoPlaybackSessionRequest(
    Guid EntityId,
    string? SessionId,
    double? PositionSeconds,
    double? DurationSeconds,
    bool? Completed);
