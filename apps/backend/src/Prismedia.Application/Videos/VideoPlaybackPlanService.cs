using Prismedia.Application.Settings;
using Prismedia.Contracts.Media;
using Prismedia.Contracts.Playback;
using Prismedia.Contracts.Security;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Videos;

/// <summary>
/// Native Prismedia playback planner. Resolves the source file via
/// <see cref="IVideoSourceService"/>, registers a transcode session, and builds the playback
/// plan (selected audio stream, delivery URL, stream metadata). All work is
/// orchestration; the heavy lifting (source resolution, ffmpeg sessions, settings access) is
/// delegated to ports and the settings use-case service.
/// </summary>
public sealed class VideoPlaybackPlanService : IVideoPlaybackPlanService {
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
    private readonly IRemuxTimelinePreparationService? _remuxTimelines;

    public VideoPlaybackPlanService(
        IVideoSourceService sources,
        ITranscodeSessionService transcodes,
        SettingsService? settings = null,
        IRemuxTimelinePreparationService? remuxTimelines = null) {
        _sources = sources;
        _transcodes = transcodes;
        _settings = settings;
        _remuxTimelines = remuxTimelines;
    }

    /// <summary>
    /// Builds a playback response for one media item and client request, or null when no
    /// source can be located.
    /// </summary>
    public async Task<VideoPlaybackPlanResult?> CreatePlanAsync(
        Guid entityId,
        VideoPlaybackPlanQuery? request,
        CancellationToken cancellationToken) {
        var source = await _sources.GetSourceAsync(entityId, cancellationToken);
        if (source is null) {
            return null;
        }

        var sessionId = string.IsNullOrWhiteSpace(request?.SessionId)
            ? Guid.NewGuid().ToString("N")
            : request.SessionId!;
        var transcodingRequested = request?.EnableTranscoding != false;
        var supportsTranscoding = transcodingRequested && source.DurationSeconds is > 0;
        var mediaSourceId = (source.MediaSourceId ?? entityId).ToString("N");
        var videoStream = PrimaryVideoStream(source);
        var videoRange = VideoPlaybackRangePolicy.Classify(videoStream);

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
            directPlayAllowed: request?.EnableDirectPlay != false && request?.AudioStreamIndex is null,
            directStreamAllowed: request?.EnableDirectStream != false,
            transcodingAllowed: supportsTranscoding,
            clientToneMappingAllowed: request?.EnableClientToneMapping == true);

        // A DirectPlay verdict serves the raw file; a Remux verdict serves a stream-copy fMP4 HLS
        // so a client that can decode the codecs but not the container avoids an expensive re-encode;
        // anything else is a full transcode.
        var supportsDirectPlayback = decision.Method == VideoPlaybackMethod.Direct;
        var serveTranscode = supportsTranscoding && !supportsDirectPlayback;
        var isRemux = serveTranscode && decision.Method == VideoPlaybackMethod.Remux;

        if (!supportsDirectPlayback && !serveTranscode) {
            return null;
        }

        // Strict native HLS players keep the duration from the first manifest they load. Returning
        // ffmpeg's short, growing EVENT playlist and replacing it at the same URL later therefore
        // strands first playback at that initial frontier. Make the exact timeline part of plan
        // preparation so the very first manifest is VOD; a failed probe uses the established full
        // transcode path instead of advertising an incomplete remux timeline.
        if (isRemux &&
            _remuxTimelines is not null &&
            !await _remuxTimelines.PrepareAsync(source, cancellationToken)) {
            decision = new VideoPlaybackDecision(VideoPlaybackMethod.Transcode);
            isRemux = false;
        }

        if (serveTranscode) {
            _transcodes.Register(sessionId, entityId);
        }

        var url = AddAccessToken(VideoPlaybackProtocol.SourcePath(entityId), request?.AccessToken);
        VideoTranscodingResult? transcoding = null;
        if (serveTranscode) {
            if (isRemux) {
                url = BuildRemuxUrl(
                    entityId,
                    selectedAudioStream?.StreamIndex,
                    decision.CopyAudio,
                    request?.AccessToken);
                // AAC remains the safe remux baseline. When negotiation proves the client accepts the
                // selected source codec, preserve that stream without re-encoding; otherwise retain its
                // channel count while converting it to AAC.
                var audioCopied = decision.CopyAudio ||
                    string.Equals(selectedAudioStream?.Codec, MediaCodecs.Aac, StringComparison.OrdinalIgnoreCase);
                transcoding = new VideoTranscodingResult(
                    MediaContainers.Mp4,
                    source.VideoCodec ?? videoStream?.Codec ?? MediaCodecs.Hevc,
                    audioCopied ? selectedAudioStream?.Codec ?? MediaCodecs.Aac : MediaCodecs.Aac,
                    IsVideoDirect: true,
                    IsAudioDirect: audioCopied);
            } else {
                url = BuildTranscodingUrl(entityId, selectedAudioStream?.StreamIndex, request?.AccessToken);
                transcoding = new VideoTranscodingResult(
                    MediaContainers.Ts,
                    MediaCodecs.H264,
                    MediaCodecs.Aac,
                    IsVideoDirect: false,
                    IsAudioDirect: false);
            }
        }

        var sourceInfo = new VideoPlaybackSourceResult(
            mediaSourceId,
            source.Container ?? ContainerFromPath(source.Path),
            source.DurationSeconds,
            decision.Method,
            url,
            supportsTranscoding,
            BuildStreams(source, selectedAudioStream?.StreamIndex),
            transcoding);

        return new VideoPlaybackPlanResult(sessionId, sourceInfo);
    }

    private static string BuildTranscodingUrl(
        Guid entityId,
        int? audioStreamIndex,
        string? accessToken) {
        var url = VideoPlaybackProtocol.HlsPath(entityId, VideoPlaybackProtocol.Hls.MasterPlaylist);
        if (audioStreamIndex is not null) {
            url = AppendQuery(url, VideoPlaybackProtocol.AudioStreamIndexQuery, audioStreamIndex.Value.ToString());
        }

        return AddAccessToken(url, accessToken);
    }

    private static string BuildRemuxUrl(
        Guid entityId,
        int? audioStreamIndex,
        bool copyAudio,
        string? accessToken) {
        var url = VideoPlaybackProtocol.HlsPath(
            entityId,
            $"v/remux/{VideoPlaybackProtocol.Hls.StreamPlaylist}");
        if (audioStreamIndex is not null) {
            url = AppendQuery(url, VideoPlaybackProtocol.AudioStreamIndexQuery, audioStreamIndex.Value.ToString());
        }
        if (copyAudio) {
            url = AppendQuery(url, VideoPlaybackProtocol.CopyAudioQuery, bool.TrueString.ToLowerInvariant());
        }

        return AddAccessToken(url, accessToken);
    }

    private static string AddAccessToken(string url, string? accessToken) =>
        string.IsNullOrWhiteSpace(accessToken)
            ? url
            : AppendQuery(url, ApiAuthenticationProtocol.AccessTokenQuery, accessToken);

    private static string AppendQuery(string url, string key, string value) {
        var separator = url.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return $"{url}{separator}{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}";
    }

    private static VideoSourceStream? SelectAudioStream(
        VideoSourceFile source,
        int? requestedIndex,
        string? preferredLanguages) {
        var audioStreams = source.Streams?
            .Where(stream => stream.Type.Equals(StreamKind.Audio.ToCode(), StringComparison.OrdinalIgnoreCase))
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

    private static IReadOnlyList<VideoPlaybackStreamResult> BuildStreams(
        VideoSourceFile source,
        int? selectedAudioStreamIndex) {
        if (source.Streams is { Count: > 0 }) {
            return source.Streams
                .OrderBy(stream => stream.StreamIndex)
                .Select(stream => {
                    var range = stream.Type.Equals(StreamKind.Video.ToCode(), StringComparison.OrdinalIgnoreCase)
                        ? VideoPlaybackRangePolicy.Classify(stream)
                        : null;
                    return new VideoPlaybackStreamResult(
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

        var videoStream = new VideoPlaybackStreamResult(
            0,
            StreamKind.Video.ToCode(),
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

        var audioStream = new VideoPlaybackStreamResult(
            1,
            StreamKind.Audio.ToCode(),
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
            .Where(stream => stream.Type.Equals(StreamKind.Video.ToCode(), StringComparison.OrdinalIgnoreCase))
            .OrderBy(stream => stream.StreamIndex)
            .FirstOrDefault();

    private static string StreamDisplayTitle(VideoSourceStream stream) {
        if (!string.IsNullOrWhiteSpace(stream.Title)) {
            return stream.Title!;
        }

        if (stream.Type.Equals(StreamKind.Audio.ToCode(), StringComparison.OrdinalIgnoreCase)) {
            var language = string.IsNullOrWhiteSpace(stream.Language) ? "Audio" : stream.Language!.ToUpperInvariant();
            var channels = stream.Channels is > 0 ? $" · {stream.Channels}ch" : "";
            return $"{language}{channels}";
        }

        return stream.Type;
    }

    private static bool StreamIsSelected(VideoSourceStream stream, int? selectedAudioStreamIndex) {
        if (!stream.Type.Equals(StreamKind.Audio.ToCode(), StringComparison.OrdinalIgnoreCase) ||
            selectedAudioStreamIndex is null) {
            return stream.IsDefault;
        }

        return stream.StreamIndex == selectedAudioStreamIndex.Value;
    }

    private static string? CodecFromContentType(string contentType) =>
        contentType.Equals(MediaContentTypes.VideoMp4, StringComparison.OrdinalIgnoreCase) ? MediaCodecs.H264 : null;

    private static string? ContainerFromPath(string path) {
        var extension = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        return string.IsNullOrWhiteSpace(extension) ? null : extension;
    }

}
