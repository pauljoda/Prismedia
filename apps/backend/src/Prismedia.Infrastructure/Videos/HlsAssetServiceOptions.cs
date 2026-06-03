namespace Prismedia.Infrastructure.Videos;

/// <summary>
/// Selects the ffmpeg video encoder profile used by virtual adaptive HLS generation.
/// </summary>
public enum HlsTranscoderProfile {
    /// <summary>Choose a native hardware encoder when Prismedia can do so conservatively, otherwise use software x264.</summary>
    Auto,

    /// <summary>Use ffmpeg's software <c>libx264</c> encoder.</summary>
    Software,

    /// <summary>Use Apple's native <c>h264_videotoolbox</c> encoder on macOS.</summary>
    VideoToolbox,

    /// <summary>Use Linux VA-API via <c>h264_vaapi</c>; intended for Intel and AMD render devices.</summary>
    Vaapi,

    /// <summary>Use NVIDIA's <c>h264_nvenc</c> encoder when the host and ffmpeg build expose it.</summary>
    Nvenc,

    /// <summary>Use Intel Quick Sync via <c>h264_qsv</c> when the host and ffmpeg build expose it.</summary>
    Qsv
}

/// <summary>
/// Helper methods for converting user-provided transcoder profile values into supported ffmpeg profiles.
/// </summary>
public static class HlsTranscoderProfiles {
    /// <summary>
    /// Parses a profile value while preserving a known-good fallback for unknown or empty input.
    /// </summary>
    /// <param name="value">Raw profile value from configuration, persisted settings, or API input.</param>
    /// <param name="fallback">Profile returned when <paramref name="value" /> is empty or unsupported.</param>
    /// <returns>A supported HLS transcoder profile.</returns>
    public static HlsTranscoderProfile ParseOrDefault(
        string? value,
        HlsTranscoderProfile fallback = HlsTranscoderProfile.Auto) =>
        Enum.TryParse<HlsTranscoderProfile>(value, ignoreCase: true, out var profile)
            ? profile
            : fallback;
}

/// <summary>
/// Runtime options for resolving and generating adaptive HLS playback assets.
/// </summary>
/// <param name="CacheRoot">Root directory for generated HLS packages.</param>
/// <param name="TranscoderProfile">Encoder profile used for new virtual HLS segments.</param>
/// <param name="FfmpegPath">Executable name or absolute path for ffmpeg.</param>
/// <param name="VaapiDevice">Linux render device used by the VA-API encoder profile.</param>
/// <param name="FfprobePath">Executable name or absolute path for ffprobe.</param>
public sealed record HlsAssetServiceOptions(
    string CacheRoot,
    HlsTranscoderProfile TranscoderProfile = HlsTranscoderProfile.Auto,
    string FfmpegPath = "ffmpeg",
    string VaapiDevice = "/dev/dri/renderD128",
    string FfprobePath = "ffprobe");
