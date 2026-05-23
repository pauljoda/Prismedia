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
    public static VideoPlaybackRange Classify(VideoSourceStream? stream) {
        if (stream is null) {
            return VideoPlaybackRange.Sdr;
        }

        if (stream.DvProfile is not null || stream.RpuPresentFlag == true) {
            return VideoPlaybackRange.Dovi;
        }

        if (stream.Hdr10PlusPresentFlag) {
            return VideoPlaybackRange.Hdr10Plus;
        }

        if (Comparer.Equals(stream.ColorTransfer, "arib-std-b67")) {
            return VideoPlaybackRange.Hlg;
        }

        if (Comparer.Equals(stream.ColorTransfer, "smpte2084") ||
            Comparer.Equals(stream.ColorPrimaries, "bt2020")) {
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
