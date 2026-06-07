using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Prismedia.Application.Videos;
using Prismedia.Contracts.Media;
using Prismedia.Infrastructure.Processes;

namespace Prismedia.Infrastructure.Videos;

/// <summary>
/// Stream-copy (remux) HLS path for <see cref="HlsAssetService"/>. When a client can decode the
/// source video codec but not its container (for example a browser that hardware-decodes HEVC but
/// cannot demux MKV), the video is copied — not re-encoded — into an fMP4 HLS stream while the audio
/// is transcoded to AAC. Copying is near free (tens of seconds for a whole movie versus a slow,
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
        string assetName,
        CancellationToken cancellationToken) {
        if (_processes is null) {
            return null;
        }

        var fileName = assetName switch {
            "stream.m3u8" or "index.m3u8" => "index.m3u8",
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
        await EnsureRemuxStartedAsync(id, source, audioCacheKey, audioStreamIndex, remuxDir, options, cancellationToken);

        // The playlist is served as ffmpeg's growing event playlist immediately, while the complete VOD
        // manifest (full seekable timeline) is computed off the request thread and swapped in once ready.
        // The init/segment files are produced by the background remux and waited on as the player asks.
        if (fileName.Equals("index.m3u8", StringComparison.OrdinalIgnoreCase)) {
            return await GetRemuxPlaylistAssetAsync(id, source, audioCacheKey, audioStreamIndex, remuxDir, options, cancellationToken);
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
        string remuxDir,
        HlsAssetServiceOptions options,
        CancellationToken cancellationToken) {
        var vodPath = Path.Combine(remuxDir, "index.vod.m3u8");
        if (File.Exists(vodPath) && new FileInfo(vodPath).Length > 0) {
            return await WriteRemuxServedPlaylistAsync(vodPath, remuxDir, audioStreamIndex, CacheControlForExtension(".m3u8"), cancellationToken);
        }

        // Fast path: get the keyframe times cheaply — from the durable cache, or by reading the
        // Matroska Cues index directly (near-instant; no whole-file ffprobe walk). When available,
        // build the full VOD playlist synchronously so the player gets the whole seekable timeline on
        // the FIRST request, even though the segment cache was evicted. This is the common case for the
        // .mkv movies that remux.
        if (source.DurationSeconds is > 0 &&
            TryFastKeyframes(id, source) is { Count: > 1 } fastKeyframes) {
            TryWriteDurableKeyframes(id, source, fastKeyframes);
            return await WriteRemuxVodAsync(
                remuxDir,
                BuildRemuxSegmentDurations(fastKeyframes, source.DurationSeconds.Value),
                audioStreamIndex,
                cancellationToken);
        }

        // No fast index (e.g. a container with no keyframe index, or an .mkv missing Cues): kick the
        // whole-file keyframe probe + VOD build onto a background task (deduped per remux key; it
        // persists the durable keyframe cache so the next play is instant) and serve the growing event
        // playlist now, bounded so a stalled ffmpeg can never re-introduce the manifest hang. The full
        // VOD takes over once the build lands.
        EnsureRemuxVodComputationStarted(id, source, audioCacheKey, audioStreamIndex, remuxDir, options);

        var legacyPath = Path.Combine(remuxDir, "index.m3u8");
        if (!await WaitForRemuxFileAsync(id, audioCacheKey, legacyPath, cancellationToken, EventPlaylistWaitBudget)) {
            return null;
        }

        return await WriteRemuxServedPlaylistAsync(legacyPath, remuxDir, audioStreamIndex, "no-cache", cancellationToken);
    }

    /// <summary>Writes the remux VOD playlist atomically and returns it as a cacheable asset.</summary>
    private async Task<HlsAsset> WriteRemuxVodAsync(
        string remuxDir,
        IReadOnlyList<double> durations,
        int? audioStreamIndex,
        CancellationToken cancellationToken) {
        Directory.CreateDirectory(remuxDir);
        var vodPath = Path.Combine(remuxDir, "index.vod.m3u8");
        var tempPath = vodPath + "." + Path.GetRandomFileName();
        await File.WriteAllTextAsync(tempPath, BuildRemuxVodPlaylist(durations, audioStreamIndex), cancellationToken);
        File.Move(tempPath, vodPath, overwrite: true);
        return new HlsAsset(vodPath, MediaContentTypes.HlsPlaylist, CacheControlForExtension(".m3u8"));
    }

    private static async Task<HlsAsset> WriteRemuxServedPlaylistAsync(
        string sourcePath,
        string remuxDir,
        int? audioStreamIndex,
        string cacheControl,
        CancellationToken cancellationToken) {
        var servedPath = Path.Combine(remuxDir, "index.served.m3u8");
        var playlist = await File.ReadAllTextAsync(sourcePath, cancellationToken);
        var rewritten = RewriteRemuxPlaylistUris(playlist, audioStreamIndex);
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
        string remuxDir,
        HlsAssetServiceOptions options) {
        var key = $"{id}/{audioCacheKey}";
        if (RemuxVodComputations.ContainsKey(key)) {
            return;
        }

        RemuxVodComputations.GetOrAdd(key, _ => ComputeRemuxVodAsync(id, source, audioCacheKey, audioStreamIndex, remuxDir, options, key));
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
            var durations = await ComputeRemuxSegmentDurationsAsync(id, source, options, token);
            if (durations is null || durations.Count == 0) {
                _logger?.LogWarning(
                    "Remux keyframe probe produced no segments for {VideoId}; keeping the event playlist.",
                    id);
                return;
            }

            await WriteRemuxVodAsync(remuxDir, durations, audioStreamIndex, token);
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
                GenerateRemuxAsync(id, source, audioStreamIndex, remuxDir, key, options, cancellation.Token),
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
            var arguments = RemuxArguments(source, audioStreamIndex, remuxDir);
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

    private IReadOnlyList<string> RemuxArguments(VideoSourceFile source, int? audioStreamIndex, string remuxDir) {
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
            // entire timeline up front; Jellyfin avoids this by reading copy-remux input at a bounded rate
            // (-readrate). We read the first RemuxInitialBurstSeconds as fast as possible so playback has an
            // immediate buffer, then cap at RemuxReadRate× realtime — far above playback speed (so the copy
            // always stays well ahead) but far below "race the whole file", keeping CPU near Jellyfin's.
            "-readrate_initial_burst",
            RemuxInitialBurstSeconds.ToString(CultureInfo.InvariantCulture),
            "-readrate",
            RemuxReadRate.ToString(CultureInfo.InvariantCulture),
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
        arguments.AddRange(RemuxAudioArguments(source, audioStreamIndex));

        arguments.AddRange(
        [
            "-f",
            "hls",
            "-hls_time",
            SegmentDurationSeconds.ToString(),
            "-hls_segment_type",
            "fmp4",
            "-hls_fmp4_init_filename",
            "init.mp4",
            "-hls_playlist_type",
            "event",
            "-hls_flags",
            "independent_segments+temp_file",
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
    /// AAC source audio is copied (<c>-c:a copy</c>): re-encoding AAC to AAC is pointless work, and a copy
    /// preserves the original channel layout (5.1/7.1) instead of downmixing to stereo. Every
    /// fMP4-HLS-capable client decodes AAC, so this is universally safe without inspecting the client's
    /// audio capabilities. Any other codec is transcoded to stereo AAC — the safe universal baseline —
    /// because we cannot assume the client can decode it. Honoring the full per-client copy decision for
    /// AC3/EAC3/etc. is gated on the device-profile audio capability and is tracked separately (see
    /// docs/hls-streaming-parity-plan.md, Track B / CopyAudio).
    /// </remarks>
    internal static IReadOnlyList<string> RemuxAudioArguments(VideoSourceFile source, int? audioStreamIndex) =>
        IsAacCodec(SelectedAudioStreamCodec(source, audioStreamIndex))
            ? ["-c:a", "copy"]
            : ["-c:a", "aac", "-ac", "2", "-b:a", "192k", "-ar", "48000"];

    // Resolves the codec of the audio stream the remux maps, mirroring the -map expression: a null index
    // maps "0:a:0?" (the first audio stream); an explicit index maps "0:{index}?" (that absolute stream).
    private static string? SelectedAudioStreamCodec(VideoSourceFile source, int? audioStreamIndex) {
        var streams = source.Streams;
        if (streams is not { Count: > 0 }) {
            return null;
        }

        if (audioStreamIndex is { } index) {
            return streams.FirstOrDefault(stream => stream.StreamIndex == index)?.Codec;
        }

        return streams
            .Where(stream => stream.Type.Equals("Audio", StringComparison.OrdinalIgnoreCase))
            .OrderBy(stream => stream.StreamIndex)
            .FirstOrDefault()?.Codec;
    }

    private static bool IsAacCodec(string? codec) =>
        codec is not null && codec.Equals("aac", StringComparison.OrdinalIgnoreCase);

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

    /// <summary>
    /// Computes the exact per-segment durations the stream-copy remux will produce for one source.
    /// </summary>
    /// <returns>
    /// Segment durations in playlist order, or null when the source duration or keyframe layout
    /// cannot be probed (in which case the caller falls back to ffmpeg's own playlist).
    /// </returns>
    private async Task<IReadOnlyList<double>?> ComputeRemuxSegmentDurationsAsync(
        Guid id,
        VideoSourceFile source,
        HlsAssetServiceOptions options,
        CancellationToken cancellationToken) {
        if (source.DurationSeconds is not > 0) {
            return null;
        }

        // Prefer the fast keyframe sources (durable cache, then the Matroska Cues index); only fall back
        // to the slow whole-file ffprobe walk when neither is available, and persist whatever we get.
        var keyframes = TryFastKeyframes(id, source)
            ?? await ProbeVideoKeyframeTimesAsync(source.Path, options, cancellationToken);
        if (keyframes is { Count: > 0 }) {
            TryWriteDurableKeyframes(id, source, keyframes);
        } else {
            return null;
        }

        return BuildRemuxSegmentDurations(keyframes, source.DurationSeconds.Value);
    }

    // Fast, non-blocking keyframe sources: the durable per-video cache, then a direct read of the
    // Matroska Cues index. Returns null when neither is available (the caller then runs the slow
    // ffprobe scan in the background). Never touches the source's full data, so it is safe to call
    // synchronously on the request thread.
    private IReadOnlyList<double>? TryFastKeyframes(Guid id, VideoSourceFile source) =>
        TryReadDurableKeyframes(id, source) ?? MatroskaKeyframeReader.TryReadKeyframeTimes(source.Path, _logger);

    // Durable per-video keyframe cache, stored OUTSIDE the evictable transcode cache roots
    // (hlsv/hls/hls2) so the transcode-cache size cap cannot delete it. Keyed by video id and
    // validated against the source's path/size/modified time so a replaced file recomputes.
    private string KeyframeCachePath(Guid id) =>
        Path.Combine(Path.GetFullPath(_options.CacheRoot), "keyframes", $"{id}.json");

    private IReadOnlyList<double>? TryReadDurableKeyframes(Guid id, VideoSourceFile source) {
        var path = KeyframeCachePath(id);
        if (!File.Exists(path)) {
            return null;
        }

        try {
            var info = new FileInfo(source.Path);
            if (!info.Exists) {
                return null;
            }

            var cache = JsonSerializer.Deserialize<DurableKeyframeCache>(File.ReadAllText(path));
            if (cache is null ||
                !string.Equals(cache.SourcePath, source.Path, StringComparison.Ordinal) ||
                cache.SourceSize != info.Length ||
                cache.SourceModifiedUtc != info.LastWriteTimeUtc) {
                return null;
            }

            return cache.KeyframeTimes;
        } catch {
            return null;
        }
    }

    private void TryWriteDurableKeyframes(Guid id, VideoSourceFile source, IReadOnlyList<double> keyframes) {
        try {
            var info = new FileInfo(source.Path);
            if (!info.Exists) {
                return;
            }

            var path = KeyframeCachePath(id);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var payload = JsonSerializer.Serialize(
                new DurableKeyframeCache(source.Path, info.Length, info.LastWriteTimeUtc, keyframes));
            var tempPath = path + "." + Path.GetRandomFileName();
            File.WriteAllText(tempPath, payload);
            File.Move(tempPath, path, overwrite: true);
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "Failed to persist the keyframe cache for {VideoId}.", id);
        }
    }

    private sealed record DurableKeyframeCache(
        string SourcePath,
        long SourceSize,
        DateTime SourceModifiedUtc,
        IReadOnlyList<double> KeyframeTimes);

    /// <summary>
    /// Reads the presentation timestamps of every video keyframe packet from the source.
    /// </summary>
    /// <remarks>
    /// Uses packet-level probing (no decode) so it stays fast on long files. These timestamps are
    /// exactly what ffmpeg's HLS muxer sees when deciding stream-copy segment boundaries.
    /// </remarks>
    private async Task<IReadOnlyList<double>?> ProbeVideoKeyframeTimesAsync(
        string sourcePath,
        HlsAssetServiceOptions options,
        CancellationToken cancellationToken) {
        if (_processes is null) {
            return null;
        }

        var arguments = new[]
        {
            "-v", "error",
            "-select_streams", "v:0",
            "-show_entries", "packet=pts_time,flags",
            "-of", "csv=print_section=0",
            sourcePath,
        };

        ProcessExecutionResult result;
        try {
            result = await _processes.RunAsync(options.FfprobePath, arguments, environment: null, cancellationToken);
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            _logger?.LogWarning(ex, "ffprobe keyframe probe could not run for {Source}.", sourcePath);
            return null;
        }

        if (result.ExitCode != 0) {
            _logger?.LogWarning(
                "ffprobe keyframe probe failed for {Source}: {Error}",
                sourcePath,
                result.StandardError);
            return null;
        }

        var times = new List<double>();
        foreach (var line in result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
            var comma = line.IndexOf(',');
            if (comma <= 0) {
                continue;
            }

            // ffprobe emits the keyframe flag as a leading 'K' in the packet flags field.
            if (line[(comma + 1)..].IndexOf('K') < 0) {
                continue;
            }

            if (double.TryParse(line[..comma], NumberStyles.Float, CultureInfo.InvariantCulture, out var pts) &&
                double.IsFinite(pts) && pts >= 0) {
                times.Add(pts);
            }
        }

        times.Sort();
        return times;
    }

    /// <summary>
    /// Replicates ffmpeg's stream-copy HLS boundary rule: it advances a cut threshold by exactly one
    /// <see cref="SegmentDurationSeconds" /> at a time (<c>6s, 12s, 18s, …</c> from the first keyframe)
    /// and cuts at the first keyframe at or after the current threshold, then steps the threshold on by
    /// one. The final segment runs from the last boundary to the end of the source.
    /// </summary>
    /// <remarks>
    /// The threshold advances by a single step per cut — it is NOT jumped forward past the keyframe that
    /// triggered the cut. That distinction only matters when a long GOP carries a keyframe well past the
    /// threshold: ffmpeg then leaves the threshold one step on, so the very next keyframe (which may be
    /// only a moment later) is cut immediately, producing a short segment. Verified against
    /// jellyfin-ffmpeg: keyframes <c>[0,19,20,25]</c> over a 30s source produce <c>[19,1,5,5]</c> (four
    /// segments). Jumping the threshold past the cut instead skips the keyframe at 20 and yields
    /// <c>[19,6,5]</c> (three segments) — so the VOD playlist we hand the player would reference the same
    /// <c>seg_NNNNN</c> filenames as ffmpeg's real output but with mismatched durations, corrupting
    /// seeking and cutting the buffer short of the true end.
    /// </remarks>
    internal static IReadOnlyList<double> BuildRemuxSegmentDurations(
        IReadOnlyList<double> keyframeTimes,
        double totalDuration) {
        var durations = new List<double>();
        var first = keyframeTimes[0];
        var segmentStart = first;
        var target = first + SegmentDurationSeconds;
        foreach (var keyframe in keyframeTimes) {
            if (keyframe < target) {
                continue;
            }

            durations.Add(keyframe - segmentStart);
            segmentStart = keyframe;
            // Step the threshold on by one segment from its PREVIOUS value, never past the cut keyframe,
            // so a keyframe landing just beyond a late cut still triggers the next cut (see remarks).
            target += SegmentDurationSeconds;
        }

        var lastDuration = totalDuration - segmentStart;
        if (lastDuration > 0.0001) {
            durations.Add(lastDuration);
        } else if (durations.Count == 0) {
            durations.Add(totalDuration);
        }

        return durations;
    }

    /// <summary>
    /// Builds a complete fMP4 <c>VOD</c> media playlist for the remux segments, mirroring the tags
    /// ffmpeg writes (version, init map, independent segments) but listing every segment up front and
    /// terminating with <c>#EXT-X-ENDLIST</c> so the player treats the whole duration as seekable.
    /// </summary>
    internal static string BuildRemuxVodPlaylist(IReadOnlyList<double> segmentDurations, int? audioStreamIndex = null) {
        var targetDuration = segmentDurations.Count == 0
            ? SegmentDurationSeconds
            : Math.Max(1, (int)Math.Round(segmentDurations.Max(), MidpointRounding.AwayFromZero));
        var lines = new List<string>
        {
            "#EXTM3U",
            "#EXT-X-VERSION:7",
            $"#EXT-X-TARGETDURATION:{targetDuration}",
            "#EXT-X-MEDIA-SEQUENCE:0",
            "#EXT-X-PLAYLIST-TYPE:VOD",
            "#EXT-X-INDEPENDENT-SEGMENTS",
            $"#EXT-X-MAP:URI=\"{AppendAudioStreamQuery("init.mp4", audioStreamIndex)}\"",
        };

        for (var index = 0; index < segmentDurations.Count; index++) {
            lines.Add(string.Format(CultureInfo.InvariantCulture, "#EXTINF:{0:0.000000},", segmentDurations[index]));
            lines.Add(AppendAudioStreamQuery($"seg_{index:00000}.m4s", audioStreamIndex));
        }

        lines.Add("#EXT-X-ENDLIST");
        lines.Add(string.Empty);
        return string.Join('\n', lines);
    }

    internal static string RewriteRemuxPlaylistUris(string playlist, int? audioStreamIndex) {
        if (audioStreamIndex is null || string.IsNullOrEmpty(playlist)) {
            return playlist;
        }

        var index = audioStreamIndex.Value;
        var lines = playlist.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++) {
            var line = lines[i];
            if (line.StartsWith("#EXT-X-MAP:", StringComparison.OrdinalIgnoreCase)) {
                lines[i] = RewriteMapUri(line, index);
            } else if (line.Length > 0 && !line.StartsWith('#')) {
                lines[i] = AppendAudioStreamQuery(line, index);
            }
        }

        return string.Join('\n', lines);
    }

    private static string RewriteMapUri(string line, int audioStreamIndex) {
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
        var rewritten = AppendAudioStreamQuery(uri, audioStreamIndex);
        return line[..uriStart] + rewritten + line[uriEnd..];
    }

    private static bool IsHevcCodec(string? codec) =>
        codec is not null &&
        (codec.Equals("hevc", StringComparison.OrdinalIgnoreCase) ||
            codec.Equals("h265", StringComparison.OrdinalIgnoreCase));

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
    /// base, so this renders correctly). This mirrors what a reference client (Jellyfin) serves to a
    /// non-Dolby-Vision browser: <c>-codec:v copy -tag:v hvc1</c>, reported as "HEVC (direct)". Tagging
    /// <c>dvh1</c> would only be correct for a client that advertised Dolby Vision support — which the
    /// browser device profile does not currently probe; until it does, <c>hvc1</c> is the right default.
    /// (Profile 5, whose ICtCp base has no conformant fallback, never reaches the remux — it is gated to
    /// a tone-mapped transcode by <see cref="VideoPlaybackRangePolicy"/>.)
    /// </para>
    /// </remarks>
    internal static IReadOnlyList<string> HevcSampleEntryTagArguments(VideoSourceFile source) =>
        IsHevcCodec(source.VideoCodec) ? ["-tag:v", "hvc1"] : [];
}
