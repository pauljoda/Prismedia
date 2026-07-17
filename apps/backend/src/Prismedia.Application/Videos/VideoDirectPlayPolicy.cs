namespace Prismedia.Application.Videos;

/// <summary>
/// How the server should deliver a source file to one client for a single playback session.
/// </summary>
public enum VideoPlaybackMethod {
    /// <summary>The client reads the original source file unchanged.</summary>
    DirectPlay,

    /// <summary>The video stream is copied without re-encoding; only the container (and optionally
    /// the audio stream) is changed to a form the client accepts.</summary>
    Remux,

    /// <summary>The video stream is re-encoded, optionally with an HDR-to-SDR tone map.</summary>
    Transcode,
}

/// <summary>
/// The negotiated delivery strategy for one playback session, including the details a remux needs.
/// </summary>
/// <param name="Method">Selected delivery method.</param>
/// <param name="CopyAudio">When remuxing, whether the source audio can be copied rather than transcoded.</param>
/// <param name="RemuxContainer">When remuxing, the target container the client accepts (for example <c>mp4</c> or <c>ts</c>).</param>
public sealed record VideoPlaybackDecision(
    VideoPlaybackMethod Method,
    bool CopyAudio = false,
    string? RemuxContainer = null);

/// <summary>
/// Decides DirectPlay vs. Remux vs. Transcode for a source file and a client.
/// <para>
/// When the client sends a Jellyfin-style device profile (its list of directly playable
/// container/codec combinations), the decision honors that profile: a capable client receives the
/// original file (DirectPlay) or a stream-copy remux instead of an unnecessary, expensive
/// re-encode. When no profile is supplied the policy falls back to the historical container
/// extension heuristic so existing first-party clients keep working unchanged.
/// </para>
/// </summary>
public static class VideoDirectPlayPolicy {
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    // HLS containers the remux pipeline can package a copied video stream into, in preference order.
    private static readonly string[] RemuxContainerPreference = ["mp4", "ts"];

    /// <summary>
    /// Selects the cheapest delivery method a client can accept for the given source.
    /// </summary>
    /// <param name="source">Resolved source file with probed container, codec, and bitrate metadata.</param>
    /// <param name="selectedAudioCodec">Codec of the audio stream chosen for this session, or null when the source has no audio.</param>
    /// <param name="range">Classified dynamic range of the primary video stream.</param>
    /// <param name="profile">Client device profile, or null when the client did not advertise one.</param>
    /// <param name="supportedVideoRangeTypes">Range types (HDR10, DOVI, ...) the client advertised it can render.</param>
    /// <param name="directPlayAllowed">Whether the client permits DirectPlay for this request.</param>
    /// <param name="directStreamAllowed">Whether the client permits DirectStream (remux) for this request.</param>
    /// <param name="transcodingAllowed">Whether the client permits transcoding for this request.</param>
    /// <returns>The negotiated delivery decision.</returns>
    public static VideoPlaybackDecision Decide(
        VideoSourceFile source,
        string? selectedAudioCodec,
        VideoPlaybackRange range,
        ClientPlaybackProfile? profile,
        IReadOnlyCollection<string>? supportedVideoRangeTypes,
        bool directPlayAllowed,
        bool directStreamAllowed,
        bool transcodingAllowed) {
        var rangeAllowed = VideoPlaybackRangePolicy.AllowsDirectPlayback(range, supportedVideoRangeTypes);
        var primaryVideoStream = PrimaryVideoStream(source);

        // HDR10, HLG, HDR10+, and Dolby Vision all require at least 10-bit samples. A stream with
        // HDR signaling over 8/9-bit video is internally contradictory; preserving that signaling
        // through DirectPlay or Remux causes standards-compliant displays to render it incorrectly.
        // Normalize it through the existing HDR-to-SDR transcode path for every client.
        if (VideoPlaybackRangePolicy.RequiresToneMappingForInvalidBitDepth(range, primaryVideoStream?.BitDepth)) {
            return new VideoPlaybackDecision(VideoPlaybackMethod.Transcode);
        }

        // Without a device profile we cannot reason about the client's codec support, so fall back
        // to the extension-based heuristic: only containers a browser plays natively are eligible.
        var directPlayProfiles = profile?.DirectPlayProfiles;
        if (directPlayProfiles is not { Count: > 0 }) {
            return directPlayAllowed && rangeAllowed && source.DirectPlayable
                ? new VideoPlaybackDecision(VideoPlaybackMethod.DirectPlay)
                : new VideoPlaybackDecision(VideoPlaybackMethod.Transcode);
        }

        var bitrateOk = profile!.MaxStreamingBitrate is not { } max || source.BitRate is not { } bitrate || bitrate <= max;
        var sourceContainer = NormalizeContainer(source.Container, source.Path);

        var videoProfiles = directPlayProfiles
            .Where(candidate => candidate.Type is null || Comparer.Equals(candidate.Type, "Video"))
            .ToList();

        // DirectPlay: a profile must accept the source's container, video codec, and audio codec
        // together, the client must be able to render the dynamic range, and the bitrate must fit.
        if (directPlayAllowed && rangeAllowed && bitrateOk && videoProfiles.Any(candidate =>
                ContainerMatches(candidate.Container, sourceContainer) &&
                CodecMatches(candidate.VideoCodec, source.VideoCodec) &&
                AudioMatches(candidate.AudioCodec, selectedAudioCodec))) {
            return new VideoPlaybackDecision(VideoPlaybackMethod.DirectPlay);
        }

        // Remux: the client can decode the video codec but needs a different container (and possibly
        // a transcoded audio track). The video is copied unchanged, so for SDR and standard HDR
        // (HDR10/HLG/HDR10+) the dynamic range is irrelevant — the client renders whatever range the
        // stream carries. The exception is a Dolby Vision stream with no client-renderable base layer
        // (Profile 5, or base-layer signal compatibility id 0): its base layer is not a conformant
        // HDR/SDR signal, so a decoder without Dolby Vision processing (every browser) shows a
        // magenta/green cast. Such a stream may only be copied to a client that advertised it can
        // render Dolby Vision; otherwise it must fall through to a tone-mapped transcode.
        var copyableRange = !RequiresDolbyVisionToneMapping(source) || rangeAllowed;
        if (directStreamAllowed && bitrateOk && copyableRange) {
            foreach (var container in RemuxContainerPreference) {
                var match = videoProfiles.FirstOrDefault(candidate =>
                    ContainerMatches(candidate.Container, container) &&
                    CodecMatches(candidate.VideoCodec, source.VideoCodec));
                if (match is not null) {
                    return new VideoPlaybackDecision(
                        VideoPlaybackMethod.Remux,
                        CopyAudio: AudioMatches(match.AudioCodec, selectedAudioCodec),
                        RemuxContainer: container);
                }
            }
        }

        return new VideoPlaybackDecision(VideoPlaybackMethod.Transcode);
    }

    // True when the source's primary video stream is a Dolby Vision layer that cannot be handed to a
    // non-Dolby-Vision decoder by a plain stream copy (Profile 5 / base-layer compatibility id 0).
    private static bool RequiresDolbyVisionToneMapping(VideoSourceFile source) {
        var stream = PrimaryVideoStream(source);
        return VideoPlaybackRangePolicy.RequiresDolbyVisionToneMapping(
            stream?.DvProfile,
            stream?.DvBlSignalCompatibilityId);
    }

    private static VideoSourceStream? PrimaryVideoStream(VideoSourceFile source) =>
        source.Streams?
            .Where(candidate => candidate.Type.Equals("Video", StringComparison.OrdinalIgnoreCase))
            .OrderBy(candidate => candidate.StreamIndex)
            .FirstOrDefault();

    private static bool ContainerMatches(string? profileContainers, string sourceContainer) =>
        Tokens(profileContainers).Any(token => Comparer.Equals(NormalizeContainerToken(token), sourceContainer));

    private static bool CodecMatches(string? profileCodecs, string? sourceCodec) {
        if (string.IsNullOrWhiteSpace(sourceCodec)) {
            return true;
        }

        // An empty codec list in a Jellyfin DirectPlayProfile means "any codec in this container".
        var tokens = Tokens(profileCodecs).ToList();
        if (tokens.Count == 0) {
            return true;
        }

        var aliases = CodecAliases(sourceCodec);
        return tokens.Any(token => aliases.Contains(NormalizeCodecToken(token)));
    }

    private static bool AudioMatches(string? profileCodecs, string? selectedAudioCodec) =>
        selectedAudioCodec is null || CodecMatches(profileCodecs, selectedAudioCodec);

    private static IEnumerable<string> Tokens(string? commaList) =>
        string.IsNullOrWhiteSpace(commaList)
            ? []
            : commaList.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static string NormalizeContainer(string? container, string path) {
        if (!string.IsNullOrWhiteSpace(container)) {
            // ffprobe reports comma lists such as "mov,mp4,m4a,3gp"; the first token identifies the family.
            var first = container.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(first)) {
                return NormalizeContainerToken(first);
            }
        }

        var extension = System.IO.Path.GetExtension(path).TrimStart('.');
        return NormalizeContainerToken(extension);
    }

    private static string NormalizeContainerToken(string token) {
        var value = token.Trim().ToLowerInvariant();
        return value switch {
            "matroska" or "mkv" or "x-matroska" => "mkv",
            "mp4" or "m4v" or "mov" or "m4a" or "qt" => "mp4",
            "ts" or "mpegts" or "m2ts" or "mts" => "ts",
            _ => value,
        };
    }

    private static string NormalizeCodecToken(string token) {
        var value = token.Trim().ToLowerInvariant();
        return value switch {
            "h265" or "hevc" or "h.265" => "hevc",
            "h264" or "avc" or "h.264" => "h264",
            "ec-3" or "eac3" or "e-ac-3" => "eac3",
            "ac-3" or "ac3" => "ac3",
            _ => value,
        };
    }

    private static HashSet<string> CodecAliases(string codec) =>
        new(StringComparer.OrdinalIgnoreCase) { NormalizeCodecToken(codec) };
}
