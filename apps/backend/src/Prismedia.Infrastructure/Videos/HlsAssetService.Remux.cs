using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Prismedia.Application.Videos;
using Prismedia.Contracts.Playback;
using Prismedia.Contracts.Media;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Processes;

namespace Prismedia.Infrastructure.Videos;

/// <summary>
/// Stream-copy (remux) HLS path for <see cref="HlsAssetService"/>. When a client can decode the
/// source video codec but not its container (for example a browser that hardware-decodes HEVC but
/// cannot demux MKV), the video is copied — not re-encoded — into an fMP4 HLS stream. Audio is also
/// copied when the negotiated client profile accepts its codec; otherwise it uses the AAC baseline.
/// Copying is near free (tens of seconds for a whole movie versus a slow,
/// CPU/GPU-bound transcode), so the client hardware-decodes the original stream and playback is
/// smooth with negligible server load, matching how other media servers serve HEVC to browsers.
/// </summary>
public sealed partial class HlsAssetService {
    // Stream-copy input pacing (see RemuxArguments). The copy reads at RemuxReadRate× realtime. A pure
    // copy is light on CPU at any rate (it is I/O- and audio-transcode-bound, ~1 core), so we pace it
    // high: high enough that the linear copy reaches a deep resume/seek position within a few seconds
    // (a 28-minute episode copies end-to-end in ~30s), but bounded so it does not saturate disk I/O on a
    // shared box. This is the trade for keeping a single linear copy instead of a per-seek restart, which
    // is unreliable for open-GOP HEVC (ffmpeg's input seek lands on a different keyframe phase than the
    // copy's segment boundaries, so seeked segments would not align with the VOD playlist). The first
    // RemuxInitialBurstSeconds are read flat out so the player gets an immediate buffer at startup.
    private const int RemuxReadRate = 60;
    private const int RemuxInitialBurstSeconds = 30;
    private const int TranscodedAudioSampleRate = 48000;

    // One whole-file remux generation per (item, audio track); the ffmpeg job runs to completion in
    // the background and the served files (init.mp4, seg_*.m4s, index.m3u8) appear as it progresses.
    private static readonly ConcurrentDictionary<string, RemuxGeneration> RemuxGenerations = new();
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> RemuxStartLocks = new();

    // A tracked stream-copy job: the running task, its kill switch, the owning entity, and when it
    // started — enough for the reaper to cancel it when the viewer leaves or it overruns its lifetime.
    private sealed record RemuxGeneration(
        Task Task,
        CancellationTokenSource Cancellation,
        Guid EntityId,
        DateTimeOffset StartedAtUtc);

    // Last time any asset of a remux key was requested. The reaper treats an actively-fetched copy as
    // live on the strength of this alone (no session ping required), so a playing client's in-progress
    // copy is never cancelled mid-stream — which previously orphaned the job and forced a restart.
    private static readonly ConcurrentDictionary<string, DateTimeOffset> RemuxLastRequestedUtc = new();

    // Background keyframe-probe + VOD-playlist build per (item, audio track). The remux media playlist
    // must NOT block the request thread on a whole-file ffprobe keyframe walk: on a long 4K source that
    // walk runs tens of seconds and exceeds the client's manifest-load timeout, so the manifest never
    // returns. Instead we serve ffmpeg's growing event playlist immediately and compute the precise VOD
    // playlist here, off the request thread, swapping it in (index.vod.m3u8) on a later poll.
    private static readonly ConcurrentDictionary<string, Task> RemuxVodComputations = new();

    // How long a cold first request waits for ffmpeg to write its first event playlist before giving up
    // and letting the client retry — keeps the manifest response well inside the client timeout even if
    // ffmpeg stalls, so the synchronous-probe hang can never recur.
    private static readonly TimeSpan EventPlaylistWaitBudget = TimeSpan.FromSeconds(8);

    private async Task<HlsAsset?> TryGetRemuxAssetAsync(
        Guid id,
        VideoSourceFile source,
        string audioCacheKey,
        int? audioStreamIndex,
        bool copyAudio,
        string assetName,
        CancellationToken cancellationToken) {
        if (_processes is null) {
            return null;
        }

        var fileName = assetName switch {
            VideoPlaybackProtocol.Hls.StreamPlaylist or VideoPlaybackProtocol.Hls.IndexPlaylist => VideoPlaybackProtocol.Hls.IndexPlaylist,
            "init.mp4" => "init.mp4",
            _ when assetName.StartsWith("seg_", StringComparison.OrdinalIgnoreCase) &&
                assetName.EndsWith(".m4s", StringComparison.OrdinalIgnoreCase) => assetName,
            _ => null,
        };
        if (fileName is null) {
            return null;
        }

        // Resolve the transcoder options once, here on the request thread, and thread them through both
        // the background remux generation and the keyframe-probed playlist. Resolving reads settings from
        // the scoped DbContext; the background generation task outlives the request, so if it (or the
        // playlist probe running concurrently) re-resolved, two operations would hit the same DbContext at
        // once ("a second operation was started on this context") and the disposed context after the
        // request ends. Pre-resolving keeps every DbContext read on the request thread, in sequence.
        var options = await ResolveTranscoderOptionsAsync(cancellationToken);

        // Mark this remux as actively fetched so the reaper keeps it alive while playback is ongoing.
        RemuxLastRequestedUtc[$"{id}/{audioCacheKey}"] = DateTimeOffset.UtcNow;

        var remuxDir = VirtualPath(id, "remux", audioCacheKey);
        await EnsureRemuxStartedAsync(
            id,
            source,
            audioCacheKey,
            audioStreamIndex,
            copyAudio,
            remuxDir,
            options,
            cancellationToken);

        // The playlist is served as ffmpeg's growing event playlist immediately, while the complete VOD
        // manifest (full seekable timeline) is computed off the request thread and swapped in once ready.
        // The init/segment files are produced by the background remux and waited on as the player asks.
        if (fileName.Equals("index.m3u8", StringComparison.OrdinalIgnoreCase)) {
            return await GetRemuxPlaylistAssetAsync(
                id,
                source,
                audioCacheKey,
                audioStreamIndex,
                copyAudio,
                remuxDir,
                options,
                cancellationToken);
        }

        var filePath = Path.Combine(remuxDir, fileName);
        if (!await WaitForRemuxFileAsync(id, audioCacheKey, filePath, cancellationToken)) {
            return null;
        }

        return new HlsAsset(filePath, MediaContentTypes.VideoMp4, "public, max-age=31536000, immutable");
    }

    /// <summary>
    /// Resolves the remux media playlist, returning the complete <c>#EXT-X-PLAYLIST-TYPE:VOD</c> manifest
    /// once it has been built and ffmpeg's growing <c>EVENT</c> playlist until then.
    /// </summary>
    /// <remarks>
    /// The full VOD manifest needs the source's keyframe timestamps, which come from a whole-file ffprobe
    /// packet walk — tens of seconds on a long 4K source. Running that on the request thread blocked the
    /// manifest past the client's manifest-load timeout, so the manifest never returned and the player
    /// reported a network timeout. Instead the keyframe probe + VOD build run off the request thread
    /// (<see cref="ComputeRemuxVodAsync"/>); the request returns ffmpeg's growing <c>EVENT</c> playlist
    /// immediately (seekable only to the copied frontier, with <c>no-cache</c> so the player re-polls),
    /// and the precise VOD playlist — emitted with <c>#EXT-X-ENDLIST</c> so the whole duration is
    /// seekable — is served from <c>index.vod.m3u8</c> on a later poll once the background build lands it.
    /// </remarks>
    private async Task<HlsAsset?> GetRemuxPlaylistAssetAsync(
        Guid id,
        VideoSourceFile source,
        string audioCacheKey,
        int? audioStreamIndex,
        bool copyAudio,
        string remuxDir,
        HlsAssetServiceOptions options,
        CancellationToken cancellationToken) {
        var vodPath = Path.Combine(remuxDir, "index.vod.m3u8");
        if (File.Exists(vodPath) && new FileInfo(vodPath).Length > 0) {
            return await WriteRemuxServedPlaylistAsync(
                vodPath,
                remuxDir,
                audioStreamIndex,
                copyAudio,
                CacheControlForExtension(".m3u8"),
                cancellationToken);
        }

        // Fast path: a previous exact walk is cached durably, so the whole seekable timeline is available
        // immediately even though the segment cache was evicted.
        if (source.DurationSeconds is > 0 &&
            TryReadDurableKeyframes(id, source) is { Count: > 1 } cachedKeyframes) {
            return await WriteRemuxVodAsync(
                remuxDir,
                cachedKeyframes,
                source.DurationSeconds.Value,
                audioStreamIndex,
                copyAudio,
                cancellationToken);
        }

        // Cold: kick the whole-file keyframe probe + VOD build onto a background task (deduped per remux
        // key; it persists the durable keyframe cache so the next play is instant) and serve the growing
        // event playlist now, bounded so a stalled ffmpeg can never re-introduce the manifest hang. There
        // is deliberately no cheap alternative — only the full walk knows where each segment really starts
        // presenting, and guessing that from a sample is what broke WebKit playback before.
        EnsureRemuxVodComputationStarted(
            id,
            source,
            audioCacheKey,
            audioStreamIndex,
            copyAudio,
            remuxDir,
            options);

        var legacyPath = Path.Combine(remuxDir, "index.m3u8");
        if (!await WaitForRemuxFileAsync(id, audioCacheKey, legacyPath, cancellationToken, EventPlaylistWaitBudget)) {
            return null;
        }

        // Served as ffmpeg wrote it. Its EXTINF values are keyframe-dated, so on an open-GOP source they
        // are slightly wrong until the exact VOD playlist replaces them — but a single measured lead cannot
        // fix that, because leads are a per-boundary fact and vary within one file. The playlist is
        // transient and carries no independence claim, and the walk that replaces it is cached durably.
        return await WriteRemuxServedPlaylistAsync(
            legacyPath,
            remuxDir,
            audioStreamIndex,
            copyAudio,
            "no-cache",
            cancellationToken);
    }

    /// <summary>Writes the remux VOD playlist atomically and returns it as a cacheable asset.</summary>
    private async Task<HlsAsset> WriteRemuxVodAsync(
        string remuxDir,
        IReadOnlyList<RemuxKeyframe> keyframes,
        double totalDuration,
        int? audioStreamIndex,
        bool copyAudio,
        CancellationToken cancellationToken) {
        Directory.CreateDirectory(remuxDir);
        var vodPath = Path.Combine(remuxDir, "index.vod.m3u8");
        var tempPath = vodPath + "." + Path.GetRandomFileName();
        await File.WriteAllTextAsync(
            tempPath,
            BuildRemuxVodPlaylist(
                BuildRemuxSegmentDurations(keyframes, totalDuration),
                audioStreamIndex,
                copyAudio,
                SegmentsAreIndependent(keyframes)),
            cancellationToken);
        File.Move(tempPath, vodPath, overwrite: true);
        return new HlsAsset(vodPath, MediaContentTypes.HlsPlaylist, CacheControlForExtension(".m3u8"));
    }

    private static async Task<HlsAsset> WriteRemuxServedPlaylistAsync(
        string sourcePath,
        string remuxDir,
        int? audioStreamIndex,
        bool copyAudio,
        string cacheControl,
        CancellationToken cancellationToken) {
        var servedPath = Path.Combine(remuxDir, "index.served.m3u8");
        var playlist = await File.ReadAllTextAsync(sourcePath, cancellationToken);
        var rewritten = RewriteRemuxPlaylistUris(playlist, audioStreamIndex, copyAudio);
        await File.WriteAllTextAsync(servedPath, rewritten, cancellationToken);
        return new HlsAsset(servedPath, MediaContentTypes.HlsPlaylist, cacheControl);
    }

    /// <summary>
    /// Ensures the background keyframe-probe + VOD-playlist build for a remux is running, at most one per
    /// <c>{id}/{audioCacheKey}</c>. Concurrent manifest requests collapse onto the single in-flight build.
    /// </summary>
    private void EnsureRemuxVodComputationStarted(
        Guid id,
        VideoSourceFile source,
        string audioCacheKey,
        int? audioStreamIndex,
        bool copyAudio,
        string remuxDir,
        HlsAssetServiceOptions options) {
        var key = $"{id}/{audioCacheKey}";
        if (RemuxVodComputations.ContainsKey(key)) {
            return;
        }

        RemuxVodComputations.GetOrAdd(
            key,
            _ => ComputeRemuxVodAsync(
                id,
                source,
                audioCacheKey,
                audioStreamIndex,
                copyAudio,
                remuxDir,
                options,
                key));
    }

    /// <summary>
    /// Probes the source keyframes and writes the precise VOD playlist (<c>index.vod.m3u8</c>) off the
    /// request thread, so a slow whole-file probe never blocks the manifest response.
    /// </summary>
    private async Task ComputeRemuxVodAsync(
        Guid id,
        VideoSourceFile source,
        string audioCacheKey,
        int? audioStreamIndex,
        bool copyAudio,
        string remuxDir,
        HlsAssetServiceOptions options,
        string key) {
        try {
            // CRITICAL: never use the request's CancellationToken — a request abort must not kill the
            // probe (that was the original bug). Link to the remux generation's lifetime so an explicit
            // Stop or the reaper can still cancel it; otherwise run uncancelled.
            var token = RemuxGenerations.TryGetValue(key, out var generation)
                ? generation.Cancellation.Token
                : CancellationToken.None;

            // Probes (and persists the durable keyframe cache) then writes the VOD playlist.
            var keyframes = await ComputeRemuxKeyframesAsync(
                id,
                source,
                options,
                lowPriority: true,
                token);
            if (keyframes is null || keyframes.Count == 0) {
                _logger?.LogWarning(
                    "Remux keyframe probe produced no segments for {VideoId}; keeping the event playlist.",
                    id);
                return;
            }

            await WriteRemuxVodAsync(
                remuxDir,
                keyframes,
                source.DurationSeconds!.Value,
                audioStreamIndex,
                copyAudio,
                token);
        } catch (OperationCanceledException) {
            // Playback stopped; the next play recomputes from scratch.
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "Background remux VOD computation failed for {VideoId}.", id);
        } finally {
            RemuxVodComputations.TryRemove(key, out _);
        }
    }

    private async Task EnsureRemuxStartedAsync(
        Guid id,
        VideoSourceFile source,
        string audioCacheKey,
        int? audioStreamIndex,
        bool copyAudio,
        string remuxDir,
        HlsAssetServiceOptions options,
        CancellationToken cancellationToken) {
        var key = $"{id}/{audioCacheKey}";
        if (RemuxGenerations.ContainsKey(key)) {
            return;
        }

        var startLock = RemuxStartLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await startLock.WaitAsync(cancellationToken);
        try {
            if (RemuxGenerations.ContainsKey(key)) {
                return;
            }

            var indexPath = Path.Combine(remuxDir, "index.m3u8");
            if (File.Exists(indexPath) &&
                (await File.ReadAllTextAsync(indexPath, cancellationToken)).Contains("#EXT-X-ENDLIST", StringComparison.Ordinal)) {
                // A previous run completed this remux; reuse the cached fMP4 HLS as-is.
                RemuxGenerations[key] = new RemuxGeneration(Task.CompletedTask, new CancellationTokenSource(), id, DateTimeOffset.UtcNow);
                return;
            }

            var cancellation = new CancellationTokenSource();
            RemuxGenerations[key] = new RemuxGeneration(
                GenerateRemuxAsync(
                    id,
                    source,
                    audioStreamIndex,
                    copyAudio,
                    remuxDir,
                    key,
                    options,
                    cancellation.Token),
                cancellation,
                id,
                DateTimeOffset.UtcNow);
        } finally {
            startLock.Release();
        }
    }

    private async Task GenerateRemuxAsync(
        Guid id,
        VideoSourceFile source,
        int? audioStreamIndex,
        bool copyAudio,
        string remuxDir,
        string key,
        HlsAssetServiceOptions options,
        CancellationToken cancellationToken) {
        try {
            // Do NOT wipe the directory. If a prior copy was interrupted (reaper, navigation, a fault),
            // the already-produced segments must survive: the client is actively reading them, and the
            // VOD playlist lists them as available. Deleting here was the root of the 404 storm — every
            // restart pulled the segments out from under hls.js, which then errored and fell back to a
            // full transcode. ffmpeg re-copies over the existing files (atomically, via the temp_file
            // flag); the copy is deterministic so re-produced segments are byte-identical.
            Directory.CreateDirectory(remuxDir);
            var arguments = RemuxArguments(source, audioStreamIndex, copyAudio, remuxDir);
            var result = await _processes!.RunAsync(options.FfmpegPath, arguments, environment: null, cancellationToken);
            if (result.ExitCode != 0) {
                _logger?.LogWarning(
                    "Remux generation failed for {VideoId}: {Error}",
                    id,
                    result.StandardError);
                // Let the next request retry from scratch rather than serving a half-written remux.
                RemuxGenerations.TryRemove(key, out _);
            }
        } catch (OperationCanceledException) {
            // The reaper or an explicit stop cancelled this copy; drop the entry so a later request
            // regenerates from scratch rather than waiting on a partial, abandoned remux.
            RemuxGenerations.TryRemove(key, out _);
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "Remux generation errored for {VideoId}.", id);
            RemuxGenerations.TryRemove(key, out _);
        }
    }

    private IReadOnlyList<string> RemuxArguments(
        VideoSourceFile source,
        int? audioStreamIndex,
        bool copyAudio,
        string remuxDir) {
        var arguments = new List<string>
        {
            "-hide_banner",
            "-y",
            "-loglevel",
            "error",
            "-nostats",
            // A stream copy is I/O- and audio-encode-bound (~1 core); cap threads so it never competes
            // for the whole box with a concurrent transcode or the API/worker.
            "-threads",
            "2",
            // Pace the stream copy instead of writing the whole file to disk as fast as the drive allows.
            // An unthrottled copy of a long 4K source pins every core for the burst it takes to copy the
            // entire timeline up front; bounded input reads avoid this while keeping generation
            // (-readrate). We read the first RemuxInitialBurstSeconds as fast as possible so playback has an
            // immediate buffer, then cap at RemuxReadRate× realtime — far above playback speed (so the copy
            // always well ahead) but far below "race the whole file", keeping CPU bounded.
            "-readrate_initial_burst",
            RemuxInitialBurstSeconds.ToString(CultureInfo.InvariantCulture),
            "-readrate",
            RemuxReadRate.ToString(CultureInfo.InvariantCulture),
            // Preserve packet timestamps for both copied streams, then move the shared input timeline
            // to zero as one unit. Their relative offset survives, so delayed audio retains its leading
            // silence without decoding and re-encoding an already browser-safe AAC track.
            "-copyts",
            "-start_at_zero",
            "-i",
            source.Path,
            "-map",
            "0:v:0",
            "-map",
            audioStreamIndex is null ? "0:a:0?" : $"0:{audioStreamIndex.Value}?",
            "-map_metadata",
            "-1",
            "-map_chapters",
            "-1",
            "-c:v",
            "copy",
        };

        arguments.AddRange(HevcSampleEntryTagArguments(source));
        arguments.AddRange(RemuxAudioArguments(source, audioStreamIndex, copyAudio));

        arguments.AddRange(
        [
            // Do not let the output muxer independently sanitize negative timestamps after copyts.
            // A shared timeline shift is safe; a per-stream shift changes A/V synchronization.
            "-avoid_negative_ts",
            "disabled",
            "-f",
            "hls",
            "-hls_time",
            SegmentDurationSeconds.ToString(),
            "-hls_segment_type",
            "fmp4",
            // Each fMP4 segment is a new fragmented-MP4 output. Tell the MOV muxer to seed that
            // fragment from the packets' preserved DTS/PTS so a track with an intentional initial
            // delay records that delay in TFDT instead of being independently rebased by the player.
            // This keeps copied multichannel audio bit-exact while retaining its source A/V timing.
            "-hls_segment_options",
            "movflags=+frag_discont",
            "-hls_fmp4_init_filename",
            "init.mp4",
            "-hls_playlist_type",
            "event",
            // Deliberately NOT independent_segments. ffmpeg cuts a stream copy at any IRAP picture, and
            // an open-GOP HEVC source (x265's default) starts every GOP after the first on a CRA with
            // RASL leading pictures rather than an IDR — so most segments are not independently
            // decodable and must not be advertised as such. Our own VOD playlist re-adds the tag when
            // the probe proves every boundary is a true IDR. See BuildRemuxVodPlaylist.
            "-hls_flags",
            "temp_file",
            "-hls_list_size",
            "0",
            "-hls_segment_filename",
            Path.Combine(remuxDir, "seg_%05d.m4s"),
            Path.Combine(remuxDir, "index.m3u8"),
        ]);

        return arguments;
    }

    /// <summary>
    /// Builds the audio output arguments for a stream-copy remux.
    /// </summary>
    /// <remarks>
    /// Negotiated source audio and AAC are packet-copied so their original samples, bitrate, and channel
    /// layout remain untouched. The
    /// enclosing remux command preserves the source streams' relative timestamps with <c>-copyts</c>,
    /// <c>-start_at_zero</c>, and <c>-avoid_negative_ts disabled</c>, so timestamp correction does not
    /// require a lossy AAC-to-AAC generation. Other codecs are converted to multichannel AAC and use
    /// asynchronous resampling to correct timestamp discontinuities and fill any leading timestamp gap
    /// with silence. That keeps a delayed audio track aligned instead of letting a player rebase it.
    /// </remarks>
    internal static IReadOnlyList<string> RemuxAudioArguments(
        VideoSourceFile source,
        int? audioStreamIndex,
        bool copyAudio = false) =>
        copyAudio || IsAacCodec(SelectedAudioStreamCodec(source, audioStreamIndex))
            ? ["-c:a", "copy"]
            : TranscodedAudioArguments(
                source,
                audioStreamIndex,
                stereoBitrate: "192k",
                audioFilter: TranscodedAudioTimestampFilter(startSeconds: 0));

    /// <summary>
    /// Builds AAC encoding arguments that retain as many source channels as Apple playback routes accept.
    /// </summary>
    internal static IReadOnlyList<string> TranscodedAudioArguments(
        VideoSourceFile source,
        int? audioStreamIndex,
        string stereoBitrate,
        string? audioFilter = null) {
        var channels = Math.Clamp(
            SelectedAudioStream(source, audioStreamIndex)?.Channels ?? source.Channels ?? 2,
            1,
            8);
        var bitrate = channels switch {
            >= 7 => "512k",
            >= 3 => "384k",
            _ => stereoBitrate,
        };
        var arguments = new List<string> {
            "-c:a", MediaCodecs.Aac,
            "-ac", channels.ToString(CultureInfo.InvariantCulture),
            "-b:a", bitrate,
            "-ar", TranscodedAudioSampleRate.ToString(CultureInfo.InvariantCulture),
        };
        if (!string.IsNullOrWhiteSpace(audioFilter)) {
            arguments.AddRange(["-af", audioFilter]);
        }
        return arguments;
    }

    /// <summary>
    /// Builds an async-resample filter whose first output timestamp matches the rendition's seek point.
    /// Missing samples before the source track begins are emitted as silence without rebasing real audio.
    /// </summary>
    internal static string TranscodedAudioTimestampFilter(double startSeconds) {
        var firstPts = (long)Math.Round(
            startSeconds * TranscodedAudioSampleRate,
            MidpointRounding.AwayFromZero);
        return $"aresample=async=1:first_pts={firstPts.ToString(CultureInfo.InvariantCulture)}";
    }

    // Resolves the codec of the audio stream the remux maps, mirroring the -map expression: a null index
    // maps "0:a:0?" (the first audio stream); an explicit index maps "0:{index}?" (that absolute stream).
    private static string? SelectedAudioStreamCodec(VideoSourceFile source, int? audioStreamIndex) =>
        SelectedAudioStream(source, audioStreamIndex)?.Codec;

    private static VideoSourceStream? SelectedAudioStream(VideoSourceFile source, int? audioStreamIndex) {
        var streams = source.Streams;
        if (streams is not { Count: > 0 }) {
            return null;
        }

        if (audioStreamIndex is { } index) {
            return streams.FirstOrDefault(stream => stream.StreamIndex == index);
        }

        return streams
            .Where(stream => stream.Type.Equals(StreamKind.Audio.ToCode(), StringComparison.OrdinalIgnoreCase))
            .OrderBy(stream => stream.StreamIndex)
            .FirstOrDefault();
    }

    private static bool IsAacCodec(string? codec) =>
        codec is not null && codec.Equals(MediaCodecs.Aac, StringComparison.OrdinalIgnoreCase);

    private async Task<bool> WaitForRemuxFileAsync(
        Guid id,
        string audioCacheKey,
        string filePath,
        CancellationToken cancellationToken,
        TimeSpan? budget = null) {
        var key = $"{id}/{audioCacheKey}";
        var deadline = budget is { } window ? DateTimeOffset.UtcNow + window : (DateTimeOffset?)null;
        while (true) {
            if (File.Exists(filePath) && new FileInfo(filePath).Length > 0) {
                return true;
            }

            if (RemuxGenerations.TryGetValue(key, out var generation) && generation.Task.IsCompleted) {
                // Generation finished (or failed); the file will not appear if it is not there now.
                return File.Exists(filePath) && new FileInfo(filePath).Length > 0;
            }

            // A bounded wait (the cold event-playlist case) gives up rather than risk re-introducing a
            // long manifest hang if ffmpeg stalls; the client simply re-polls. The segment waits pass no
            // budget and keep their original unbounded behaviour.
            if (deadline is { } limit && DateTimeOffset.UtcNow >= limit) {
                return false;
            }

            await Task.Delay(SegmentPollInterval, cancellationToken);
        }
    }

    internal static string RewriteRemuxPlaylistUris(
        string playlist,
        int? audioStreamIndex,
        bool copyAudio = false) {
        if ((audioStreamIndex is null && !copyAudio) || string.IsNullOrEmpty(playlist)) {
            return playlist;
        }

        var lines = playlist.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++) {
            var line = lines[i];
            if (line.StartsWith("#EXT-X-MAP:", StringComparison.OrdinalIgnoreCase)) {
                lines[i] = RewriteMapUri(line, audioStreamIndex, copyAudio);
            } else if (line.Length > 0 && !line.StartsWith('#')) {
                lines[i] = AppendPlaybackQuery(line, audioStreamIndex, copyAudio);
            }
        }

        return string.Join('\n', lines);
    }

    private static string RewriteMapUri(string line, int? audioStreamIndex, bool copyAudio) {
        const string marker = "URI=\"";
        var uriStart = line.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (uriStart < 0) {
            return line;
        }

        uriStart += marker.Length;
        var uriEnd = line.IndexOf('"', uriStart);
        if (uriEnd < 0) {
            return line;
        }

        var uri = line[uriStart..uriEnd];
        var rewritten = AppendPlaybackQuery(uri, audioStreamIndex, copyAudio);
        return line[..uriStart] + rewritten + line[uriEnd..];
    }

    private static bool IsHevcCodec(string? codec) =>
        MediaCodecs.IsHevc(codec);

    /// <summary>
    /// Chooses the HEVC sample-entry codec tag for a stream copy. HEVC is always tagged <c>hvc1</c>;
    /// non-HEVC sources need no override.
    /// </summary>
    /// <remarks>
    /// Browsers require an explicit tag because the source's <c>hev1</c> tag (or no tag, from an MKV)
    /// does not play in fMP4. <c>hvc1</c> is the universally safe choice — it is what every HEVC-capable
    /// browser accepts (verified: Chromium's <c>MediaSource.isTypeSupported('…hvc1…')</c> is true).
    /// <para>
    /// We deliberately do NOT tag Dolby Vision sources <c>dvh1</c>. A <c>dvh1</c> sample entry advertises
    /// Dolby Vision, and a browser whose MSE cannot decode it (Chromium reports
    /// <c>isTypeSupported('…dvh1.08.06…')</c> false) rejects the buffer outright — instant failure and a
    /// fallback to a heavy transcode. With an <c>hvc1</c> tag the same browser decodes the HEVC base
    /// layer and simply ignores the Dolby Vision RPU NAL units (Profile 7/8 carry a conformant HDR10/HLG
    /// base, so this renders correctly). This mirrors what reference HLS clients serve to a
    /// non-Dolby-Vision browser: <c>-codec:v copy -tag:v hvc1</c>, reported as "HEVC (direct)". Tagging
    /// <c>dvh1</c> would only be correct for a client that advertised Dolby Vision support — which the
    /// browser device profile does not currently probe; until it does, <c>hvc1</c> is the right default.
    /// (Profile 5, whose ICtCp base has no conformant fallback, never reaches the remux — it is gated to
    /// a tone-mapped transcode by <see cref="VideoPlaybackRangePolicy"/>.)
    /// </para>
    /// </remarks>
    internal static IReadOnlyList<string> HevcSampleEntryTagArguments(VideoSourceFile source) =>
        IsHevcCodec(source.VideoCodec) ? ["-tag:v", MediaCodecs.Hvc1Tag] : [];
}
