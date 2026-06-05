using Prismedia.Application.Settings;
using Prismedia.Contracts.Media;

namespace Prismedia.Application.Videos;

/// <summary>
/// Clean-room Jellyfin-shaped playback negotiator. Resolves the source file via
/// <see cref="IVideoSourceService"/>, registers a transcode session, and builds the playback
/// info response (selected audio stream, transcoding URL, stream metadata). All work is
/// orchestration; the heavy lifting (source resolution, ffmpeg sessions, settings access) is
/// delegated to ports and the settings use-case service.
/// </summary>
public sealed class PlaybackInfoService : IPlaybackInfoService {
    private static readonly IReadOnlyDictionary<string, string[]> LanguageAliases = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase) {
        ["en"] = ["en", "eng", "english"],
        ["es"] = ["es", "spa", "spn", "spanish", "espanol"],
        ["fr"] = ["fr", "fre", "fra", "french", "francais"],
        ["de"] = ["de", "ger", "deu", "german", "deutsch"],
        ["it"] = ["it", "ita", "italian", "italiano"],
        ["ja"] = ["ja", "jpn", "japanese"],
        ["ko"] = ["ko", "kor", "korean"],
        ["pt"] = ["pt", "por", "portuguese", "portugues"],
        ["zh"] = ["zh", "chi", "zho", "chinese", "mandarin"],
    };

    private readonly IVideoSourceService _sources;
    private readonly ITranscodeSessionService _transcodes;
    private readonly SettingsService? _settings;

    public PlaybackInfoService(
        IVideoSourceService sources,
        ITranscodeSessionService transcodes,
        SettingsService? settings = null) {
        _sources = sources;
        _transcodes = transcodes;
        _settings = settings;
    }

    /// <summary>
    /// Builds a playback response for one media item and client request, or null when no
    /// source can be located.
    /// </summary>
    public async Task<PlaybackInfoResult?> GetPlaybackInfoAsync(
        Guid itemId,
        PlaybackInfoQuery? request,
        CancellationToken cancellationToken) {
        var source = await _sources.GetSourceAsync(itemId, cancellationToken);
        if (source is null) {
            return null;
        }

        var playSessionId = string.IsNullOrWhiteSpace(request?.PlaySessionId)
            ? Guid.NewGuid().ToString("N")
            : request.PlaySessionId!;
        var transcodingAllowed = request?.EnableTranscoding != false;
        var mediaSourceId = (source.MediaSourceId ?? itemId).ToString("N");
        var videoStream = PrimaryVideoStream(source);
        var videoRange = VideoPlaybackRangePolicy.Classify(videoStream);

        if (transcodingAllowed) {
            _transcodes.Register(playSessionId, itemId);
        }

        var fileInfo = new FileInfo(source.Path);
        var preferredAudioLanguages = _settings is null
            ? null
            : string.Join(",", (await _settings.GetPlaybackSettingsAsync(cancellationToken)).AudioPreferredLanguages);
        var selectedAudioStream = SelectAudioStream(source, request?.AudioStreamIndex, preferredAudioLanguages);
        var decision = VideoDirectPlayPolicy.Decide(
            source,
            selectedAudioStream?.Codec,
            videoRange,
            request?.Profile,
            request?.SupportedVideoRangeTypes,
            directPlayAllowed: request?.EnableDirectPlay != false,
            directStreamAllowed: request?.EnableDirectStream != false,
            transcodingAllowed: transcodingAllowed);

        // A DirectPlay verdict serves the raw file; a Remux verdict serves a stream-copy fMP4 HLS
        // (video copied, audio to AAC) so a client that can decode the codec but not the container
        // avoids an expensive re-encode; anything else is a full transcode.
        var supportsDirectPlayback = decision.Method == VideoPlaybackMethod.DirectPlay;
        var serveTranscode = transcodingAllowed && !supportsDirectPlayback;
        var isRemux = serveTranscode && decision.Method == VideoPlaybackMethod.Remux;

        string? transcodingUrl = null;
        string? transcodingSubProtocol = null;
        string? transcodingContainer = null;
        TranscodingInfoResult? transcodingInfo = null;
        if (serveTranscode) {
            transcodingSubProtocol = "hls";
            if (isRemux) {
                transcodingUrl = BuildRemuxUrl(itemId, mediaSourceId, playSessionId, selectedAudioStream?.StreamIndex, request?.AccessToken);
                transcodingContainer = "mp4";
                // The remux copies AAC audio (preserving its channel layout) and transcodes anything else
                // to stereo AAC, so advertise direct audio only when the selected track is AAC.
                var audioCopied = string.Equals(selectedAudioStream?.Codec, "aac", StringComparison.OrdinalIgnoreCase);
                transcodingInfo = new TranscodingInfoResult(
                    "mp4",
                    source.VideoCodec ?? videoStream?.Codec ?? "hevc",
                    "aac",
                    "hls",
                    IsVideoDirect: true,
                    IsAudioDirect: audioCopied);
            } else {
                transcodingUrl = BuildTranscodingUrl(itemId, mediaSourceId, playSessionId, selectedAudioStream?.StreamIndex, request?.AccessToken);
                transcodingContainer = "ts";
                transcodingInfo = new TranscodingInfoResult("ts", "h264", "aac", "hls", IsVideoDirect: false, IsAudioDirect: false);
            }
        }

        var sourceInfo = new MediaSourceInfoResult(
            mediaSourceId,
            source.Path,
            "File",
            source.Container ?? ContainerFromPath(source.Path),
            fileInfo.Exists ? fileInfo.Length : null,
            Path.GetFileName(source.Path),
            ToTicks(source.DurationSeconds),
            supportsDirectPlayback,
            supportsDirectPlayback,
            transcodingAllowed,
            transcodingUrl,
            transcodingSubProtocol,
            transcodingContainer,
            BuildStreams(source, selectedAudioStream?.StreamIndex),
            transcodingInfo);

        return new PlaybackInfoResult(playSessionId, [sourceInfo]);
    }

    private static string BuildTranscodingUrl(
        Guid itemId,
        string mediaSourceId,
        string playSessionId,
        int? audioStreamIndex,
        string? accessToken) {
        var url = $"/Videos/{itemId:D}/master.m3u8?MediaSourceId={mediaSourceId}&PlaySessionId={playSessionId}";
        if (audioStreamIndex is not null) {
            url = $"{url}&AudioStreamIndex={audioStreamIndex.Value}";
        }

        return string.IsNullOrWhiteSpace(accessToken)
            ? url
            : $"{url}&ApiKey={Uri.EscapeDataString(accessToken)}";
    }

    private static string BuildRemuxUrl(
        Guid itemId,
        string mediaSourceId,
        string playSessionId,
        int? audioStreamIndex,
        string? accessToken) {
        var url = $"/Videos/{itemId:D}/hls/remux/stream.m3u8?MediaSourceId={mediaSourceId}&PlaySessionId={playSessionId}";
        if (audioStreamIndex is not null) {
            url = $"{url}&AudioStreamIndex={audioStreamIndex.Value}";
        }

        return string.IsNullOrWhiteSpace(accessToken)
            ? url
            : $"{url}&ApiKey={Uri.EscapeDataString(accessToken)}";
    }

    private static VideoSourceStream? SelectAudioStream(
        VideoSourceFile source,
        int? requestedIndex,
        string? preferredLanguages) {
        var audioStreams = source.Streams?
            .Where(stream => stream.Type.Equals("Audio", StringComparison.OrdinalIgnoreCase))
            .OrderBy(stream => stream.StreamIndex)
            .ToList() ?? [];
        if (audioStreams.Count == 0) {
            return null;
        }

        return audioStreams.FirstOrDefault(stream => stream.StreamIndex == requestedIndex) ??
            SelectPreferredAudioStream(audioStreams, preferredLanguages) ??
            audioStreams.FirstOrDefault(stream => stream.IsDefault) ??
            audioStreams[0];
    }

    private static VideoSourceStream? SelectPreferredAudioStream(
        IReadOnlyList<VideoSourceStream> audioStreams,
        string? preferredLanguages) {
        var preferences = ParseLanguagePreferences(preferredLanguages);
        if (preferences.Count == 0) {
            return null;
        }

        foreach (var preference in preferences) {
            var match = audioStreams.FirstOrDefault(stream =>
                AudioStreamLanguageCandidates(stream).Contains(preference));
            if (match is not null) {
                return match;
            }
        }

        return null;
    }

    private static IReadOnlyList<string> ParseLanguagePreferences(string? preferredLanguages) {
        if (string.IsNullOrWhiteSpace(preferredLanguages)) {
            return [];
        }

        return preferredLanguages
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeLanguageToken)
            .Where(token => token.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static HashSet<string> AudioStreamLanguageCandidates(VideoSourceStream stream) {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddLanguageCandidate(candidates, stream.Language);
        AddLanguageCandidate(candidates, stream.Title);
        AddBestGuessLanguageCandidates(candidates, stream.Title);
        return candidates;
    }

    private static void AddLanguageCandidate(ISet<string> candidates, string? value) {
        var normalized = NormalizeLanguageToken(value);
        if (normalized.Length > 0) {
            candidates.Add(normalized);
        }
    }

    private static void AddBestGuessLanguageCandidates(ISet<string> candidates, string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return;
        }

        var text = value.ToLowerInvariant();
        foreach (var (language, aliases) in LanguageAliases) {
            if (aliases.Any(alias => text.Contains(alias, StringComparison.OrdinalIgnoreCase))) {
                candidates.Add(language);
            }
        }
    }

    private static string NormalizeLanguageToken(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return string.Empty;
        }

        var token = value.Trim().Replace('_', '-').ToLowerInvariant();
        if (token.Contains('-', StringComparison.Ordinal)) {
            token = token.Split('-', StringSplitOptions.RemoveEmptyEntries)[0];
        }

        return LanguageAliases.FirstOrDefault(pair =>
            pair.Key.Equals(token, StringComparison.OrdinalIgnoreCase) ||
            pair.Value.Any(alias => alias.Equals(token, StringComparison.OrdinalIgnoreCase))).Key ?? token;
    }

    private static IReadOnlyList<MediaStreamInfoResult> BuildStreams(
        VideoSourceFile source,
        int? selectedAudioStreamIndex) {
        if (source.Streams is { Count: > 0 }) {
            return source.Streams
                .OrderBy(stream => stream.StreamIndex)
                .Select(stream => {
                    var range = stream.Type.Equals("Video", StringComparison.OrdinalIgnoreCase)
                        ? VideoPlaybackRangePolicy.Classify(stream)
                        : null;
                    return new MediaStreamInfoResult(
                        stream.StreamIndex,
                        stream.Type,
                        stream.Codec,
                        stream.Language,
                        StreamDisplayTitle(stream),
                        stream.Width,
                        stream.Height,
                        stream.FrameRate,
                        stream.BitRate,
                        stream.SampleRate,
                        stream.Channels,
                        IsDefault: StreamIsSelected(stream, selectedAudioStreamIndex),
                        IsForced: stream.IsForced,
                        VideoRange: range?.VideoRange,
                        VideoRangeType: range?.VideoRangeType,
                        PixelFormat: stream.PixelFormat,
                        BitDepth: stream.BitDepth,
                        ColorRange: stream.ColorRange,
                        ColorSpace: stream.ColorSpace,
                        ColorTransfer: stream.ColorTransfer,
                        ColorPrimaries: stream.ColorPrimaries,
                        DvProfile: stream.DvProfile,
                        DvLevel: stream.DvLevel,
                        RpuPresentFlag: stream.RpuPresentFlag,
                        ElPresentFlag: stream.ElPresentFlag,
                        BlPresentFlag: stream.BlPresentFlag,
                        DvBlSignalCompatibilityId: stream.DvBlSignalCompatibilityId,
                        Hdr10PlusPresentFlag: stream.Hdr10PlusPresentFlag);
                })
                .ToList();
        }

        var videoStream = new MediaStreamInfoResult(
            0,
            "Video",
            source.VideoCodec ?? CodecFromContentType(source.ContentType),
            null,
            "Video",
            source.Width,
            source.Height,
            source.FrameRate,
            source.BitRate,
            null,
            null,
            IsDefault: true);

        if (source.AudioCodec is null && source.SampleRate is null && source.Channels is null) {
            return [videoStream];
        }

        var audioStream = new MediaStreamInfoResult(
            1,
            "Audio",
            source.AudioCodec,
            null,
            "Audio",
            null,
            null,
            null,
            null,
            source.SampleRate,
            source.Channels,
            IsDefault: true);

        return [videoStream, audioStream];
    }

    private static VideoSourceStream? PrimaryVideoStream(VideoSourceFile source) =>
        source.Streams?
            .Where(stream => stream.Type.Equals("Video", StringComparison.OrdinalIgnoreCase))
            .OrderBy(stream => stream.StreamIndex)
            .FirstOrDefault();

    private static string StreamDisplayTitle(VideoSourceStream stream) {
        if (!string.IsNullOrWhiteSpace(stream.Title)) {
            return stream.Title!;
        }

        if (stream.Type.Equals("Audio", StringComparison.OrdinalIgnoreCase)) {
            var language = string.IsNullOrWhiteSpace(stream.Language) ? "Audio" : stream.Language!.ToUpperInvariant();
            var channels = stream.Channels is > 0 ? $" · {stream.Channels}ch" : "";
            return $"{language}{channels}";
        }

        return stream.Type;
    }

    private static bool StreamIsSelected(VideoSourceStream stream, int? selectedAudioStreamIndex) {
        if (!stream.Type.Equals("Audio", StringComparison.OrdinalIgnoreCase) ||
            selectedAudioStreamIndex is null) {
            return stream.IsDefault;
        }

        return stream.StreamIndex == selectedAudioStreamIndex.Value;
    }

    private static string? CodecFromContentType(string contentType) =>
        contentType.Equals(MediaContentTypes.VideoMp4, StringComparison.OrdinalIgnoreCase) ? "h264" : null;

    private static string? ContainerFromPath(string path) {
        var extension = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        return string.IsNullOrWhiteSpace(extension) ? null : extension;
    }

    private static long? ToTicks(double? seconds) =>
        seconds is > 0 ? (long)Math.Round(seconds.Value * TimeSpan.TicksPerSecond) : null;
}
