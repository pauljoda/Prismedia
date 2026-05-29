namespace Prismedia.Infrastructure.Media.Processing;

/// <summary>
/// Builds the ffmpeg <c>-vf</c> filter chain that tone-maps an HDR or Dolby Vision source down to
/// SDR bt709. This is the single owner of the tone-mapping filter strings so that the streaming
/// (HLS) and still-frame (thumbnail) paths cannot drift apart in how they render HDR content.
/// Detection of <em>whether</em> a stream needs tone mapping lives in
/// <c>Prismedia.Application.Videos.VideoPlaybackRangePolicy</c>; this helper only chooses the
/// correct filter chain once a caller has decided tone mapping is required.
/// </summary>
internal static class FfmpegToneMapping {
    /// <summary>
    /// Builds the complete tone-mapping filter chain.
    /// </summary>
    /// <param name="colorTransfer">Source color transfer characteristic, used to preserve HLG vs PQ input parameters.</param>
    /// <param name="dvProfile">Dolby Vision profile when present.</param>
    /// <param name="dvBlSignalCompatibilityId">Dolby Vision base-layer signal compatibility id when present.</param>
    /// <param name="scaleFilter">The scale/transform segment to splice into the chain at its correct position.</param>
    /// <param name="trailingFormat">
    /// Optional pixel format to append after the HDR10/HLG chain (HLS appends <c>yuv420p</c>; the thumbnail
    /// path relies on its own output transform and passes null to preserve its exact filter).
    /// </param>
    /// <returns>The full ffmpeg filter chain string.</returns>
    public static string BuildFilter(
        string? colorTransfer,
        int? dvProfile,
        int? dvBlSignalCompatibilityId,
        string scaleFilter,
        string? trailingFormat = null) {
        var colorParameters = InputHdrColorParameters(colorTransfer);

        if (RequiresDolbyVisionToneMapping(dvProfile, dvBlSignalCompatibilityId)) {
            return $"{colorParameters},{scaleFilter},tonemapx=tonemap=bt2390:desat=0:peak=400:t=bt709:m=bt709:p=bt709:format=yuv420p";
        }

        var chain = $"{colorParameters},zscale=t=linear:npl=100,format=gbrpf32le,zscale=p=bt709,tonemap=tonemap=hable:desat=0:peak=100,zscale=t=bt709:m=bt709:p=bt709:out_range=tv,{scaleFilter}";
        return trailingFormat is null ? chain : $"{chain},format={trailingFormat}";
    }

    /// <summary>
    /// Decides whether a Dolby Vision source must use the Dolby-specific tone-mapping chain rather than
    /// the generic HDR10/HLG chain. Profile 8 sources with an HDR10-compatible base layer fall through
    /// to the HDR10 chain.
    /// </summary>
    /// <param name="dvProfile">Dolby Vision profile when present.</param>
    /// <param name="dvBlSignalCompatibilityId">Dolby Vision base-layer signal compatibility id when present.</param>
    /// <returns>True when the Dolby Vision tone-mapping chain is required.</returns>
    public static bool RequiresDolbyVisionToneMapping(int? dvProfile, int? dvBlSignalCompatibilityId) =>
        dvProfile is 5 || dvBlSignalCompatibilityId is 0;

    private static string InputHdrColorParameters(string? colorTransfer) {
        var transfer = string.Equals(colorTransfer, "arib-std-b67", StringComparison.OrdinalIgnoreCase)
            ? "arib-std-b67"
            : "smpte2084";
        return $"setparams=color_primaries=bt2020:color_trc={transfer}:colorspace=bt2020nc";
    }
}
