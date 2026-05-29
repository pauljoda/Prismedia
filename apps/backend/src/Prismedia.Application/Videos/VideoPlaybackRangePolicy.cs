namespace Prismedia.Application.Videos;

/// <summary>
/// Classifies video dynamic range metadata and decides whether browser direct playback is safe.
/// </summary>
public static class VideoPlaybackRangePolicy {
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// Derives the Jellyfin-style video range and range type from probed stream metadata.
    /// </summary>
    /// <param name="stream">Primary video stream metadata, or null when unavailable.</param>
    /// <returns>Classified dynamic range values for playback negotiation and HLS planning.</returns>
    public static VideoPlaybackRange Classify(VideoSourceStream? stream) =>
        stream is null
            ? VideoPlaybackRange.Sdr
            : Classify(
                stream.ColorTransfer,
                stream.ColorPrimaries,
                stream.DvProfile,
                stream.RpuPresentFlag,
                stream.Hdr10PlusPresentFlag);

    /// <summary>
    /// Derives the Jellyfin-style video range and range type from raw HDR metadata fields.
    /// This field-based overload is the single source of truth for HDR detection so that
    /// different probe/source record shapes cannot diverge in how they classify a stream.
    /// </summary>
    /// <param name="colorTransfer">Stream color transfer characteristic (e.g. smpte2084, arib-std-b67).</param>
    /// <param name="colorPrimaries">Stream color primaries (e.g. bt2020).</param>
    /// <param name="dvProfile">Dolby Vision profile when present.</param>
    /// <param name="rpuPresentFlag">Whether a Dolby Vision RPU is present.</param>
    /// <param name="hdr10PlusPresentFlag">Whether HDR10+ dynamic metadata is present.</param>
    /// <returns>Classified dynamic range values for playback negotiation and HLS planning.</returns>
    public static VideoPlaybackRange Classify(
        string? colorTransfer,
        string? colorPrimaries,
        int? dvProfile,
        bool? rpuPresentFlag,
        bool hdr10PlusPresentFlag) {
        if (dvProfile is not null || rpuPresentFlag == true) {
            return VideoPlaybackRange.Dovi;
        }

        if (hdr10PlusPresentFlag) {
            return VideoPlaybackRange.Hdr10Plus;
        }

        if (Comparer.Equals(colorTransfer, "arib-std-b67")) {
            return VideoPlaybackRange.Hlg;
        }

        if (Comparer.Equals(colorTransfer, "smpte2084") ||
            Comparer.Equals(colorPrimaries, "bt2020")) {
            return VideoPlaybackRange.Hdr10;
        }

        return VideoPlaybackRange.Sdr;
    }

    /// <summary>
    /// Returns true when the range is SDR or the client explicitly advertised support for the HDR range type.
    /// </summary>
    /// <param name="range">Classified source dynamic range.</param>
    /// <param name="supportedVideoRangeTypes">Client-advertised range type names.</param>
    /// <returns>Whether direct playback is allowed for the classified source.</returns>
    public static bool AllowsDirectPlayback(
        VideoPlaybackRange range,
        IReadOnlyCollection<string>? supportedVideoRangeTypes) {
        if (range.VideoRangeType == "SDR") {
            return true;
        }

        return supportedVideoRangeTypes?.Any(value =>
            Comparer.Equals(value, range.VideoRangeType) ||
            Comparer.Equals(value, range.VideoRange)) == true;
    }
}

/// <summary>
/// Jellyfin-style video dynamic range classification.
/// </summary>
/// <param name="VideoRange">Broad range family, such as SDR or HDR.</param>
/// <param name="VideoRangeType">Specific range type, such as HDR10, HLG, HDR10Plus, or DOVI.</param>
public sealed record VideoPlaybackRange(string VideoRange, string VideoRangeType) {
    /// <summary>Standard dynamic range video.</summary>
    public static readonly VideoPlaybackRange Sdr = new("SDR", "SDR");

    /// <summary>HDR10 video using PQ transfer metadata.</summary>
    public static readonly VideoPlaybackRange Hdr10 = new("HDR", "HDR10");

    /// <summary>Hybrid Log-Gamma HDR video.</summary>
    public static readonly VideoPlaybackRange Hlg = new("HDR", "HLG");

    /// <summary>HDR10+ video with dynamic metadata.</summary>
    public static readonly VideoPlaybackRange Hdr10Plus = new("HDR", "HDR10Plus");

    /// <summary>Dolby Vision video.</summary>
    public static readonly VideoPlaybackRange Dovi = new("HDR", "DOVI");
}
