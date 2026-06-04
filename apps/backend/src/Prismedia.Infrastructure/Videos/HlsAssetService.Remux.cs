using System.Collections.Concurrent;
using System.Globalization;
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
    // Stream-copy input pacing (see RemuxArguments). The copy reads at RemuxReadRate× realtime — far
    // above 1× playback so it stays well ahead of the viewer, far below an unthrottled copy that would
    // race the whole file to disk and pin every core. The first RemuxInitialBurstSeconds are read flat
    // out so the player gets an immediate buffer before the rate cap engages.
    private const int RemuxReadRate = 10;
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

        var remuxDir = VirtualPath(id, "remux", audioCacheKey);
        await EnsureRemuxStartedAsync(id, source, audioCacheKey, audioStreamIndex, remuxDir, options, cancellationToken);

        // The playlist is served as a complete VOD manifest computed up front from the source's
        // keyframe layout, so the whole timeline is seekable immediately. The init/segment files are
        // produced by the background remux and waited on individually as the player requests them.
        if (fileName.Equals("index.m3u8", StringComparison.OrdinalIgnoreCase)) {
            return await GetRemuxPlaylistAssetAsync(id, source, audioCacheKey, remuxDir, options, cancellationToken);
        }

        var filePath = Path.Combine(remuxDir, fileName);
        if (!await WaitForRemuxFileAsync(id, audioCacheKey, filePath, cancellationToken)) {
            return null;
        }

        return new HlsAsset(filePath, MediaContentTypes.VideoMp4, "public, max-age=31536000, immutable");
    }

    /// <summary>
    /// Resolves the remux media playlist as a complete <c>#EXT-X-PLAYLIST-TYPE:VOD</c> manifest.
    /// </summary>
    /// <remarks>
    /// The legacy remux served ffmpeg's own growing <c>EVENT</c> playlist, which only listed segments
    /// that had already been copied. Because hls.js limits the seekable range to the segments a live
    /// playlist advertises, the timeline appeared to "buffer" far ahead of playback and seeking past
    /// the copied region snapped back to it. Here we probe the source's keyframe timestamps, replicate
    /// ffmpeg's stream-copy segment boundaries exactly, and emit the full segment list with
    /// <c>#EXT-X-ENDLIST</c> so the player reports the entire duration as seekable from the first load.
    /// If the keyframe probe fails we fall back to serving ffmpeg's growing playlist as before.
    /// </remarks>
    private async Task<HlsAsset?> GetRemuxPlaylistAssetAsync(
        Guid id,
        VideoSourceFile source,
        string audioCacheKey,
        string remuxDir,
        HlsAssetServiceOptions options,
        CancellationToken cancellationToken) {
        var vodPath = Path.Combine(remuxDir, "index.vod.m3u8");
        if (File.Exists(vodPath) && new FileInfo(vodPath).Length > 0) {
            return new HlsAsset(vodPath, MediaContentTypes.HlsPlaylist, CacheControlForExtension(".m3u8"));
        }

        var durations = await ComputeRemuxSegmentDurationsAsync(source, options, cancellationToken);
        if (durations is null || durations.Count == 0) {
            // Keyframe probe unavailable: serve ffmpeg's growing event playlist as a best-effort
            // fallback so playback still works, accepting the limited seek window.
            _logger?.LogWarning(
                "Remux keyframe probe failed for {VideoId}; serving the growing event playlist.",
                id);
            var legacyPath = Path.Combine(remuxDir, "index.m3u8");
            if (!await WaitForRemuxFileAsync(id, audioCacheKey, legacyPath, cancellationToken)) {
                return null;
            }

            return new HlsAsset(legacyPath, MediaContentTypes.HlsPlaylist, "no-cache");
        }

        Directory.CreateDirectory(remuxDir);
        var tempPath = vodPath + "." + Path.GetRandomFileName();
        await File.WriteAllTextAsync(tempPath, BuildRemuxVodPlaylist(durations), cancellationToken);
        File.Move(tempPath, vodPath, overwrite: true);
        return new HlsAsset(vodPath, MediaContentTypes.HlsPlaylist, CacheControlForExtension(".m3u8"));
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
            if (Directory.Exists(remuxDir)) {
                Directory.Delete(remuxDir, recursive: true);
            }

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

        arguments.AddRange(
        [
            "-c:a",
            "aac",
            "-ac",
            "2",
            "-b:a",
            "192k",
            "-ar",
            "48000",
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

    private async Task<bool> WaitForRemuxFileAsync(
        Guid id,
        string audioCacheKey,
        string filePath,
        CancellationToken cancellationToken) {
        var key = $"{id}/{audioCacheKey}";
        while (true) {
            if (File.Exists(filePath) && new FileInfo(filePath).Length > 0) {
                return true;
            }

            if (RemuxGenerations.TryGetValue(key, out var generation) && generation.Task.IsCompleted) {
                // Generation finished (or failed); the file will not appear if it is not there now.
                return File.Exists(filePath) && new FileInfo(filePath).Length > 0;
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
        VideoSourceFile source,
        HlsAssetServiceOptions options,
        CancellationToken cancellationToken) {
        if (source.DurationSeconds is not > 0) {
            return null;
        }

        var keyframes = await ProbeVideoKeyframeTimesAsync(source.Path, options, cancellationToken);
        if (keyframes is null || keyframes.Count < 1) {
            return null;
        }

        return BuildRemuxSegmentDurations(keyframes, source.DurationSeconds.Value);
    }

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
    /// Replicates ffmpeg's stream-copy HLS boundary rule: a new segment starts at the first keyframe
    /// whose timestamp is at least <see cref="SegmentDurationSeconds" /> past the current segment's
    /// start. The final segment runs from the last boundary to the end of the source.
    /// </summary>
    internal static IReadOnlyList<double> BuildRemuxSegmentDurations(
        IReadOnlyList<double> keyframeTimes,
        double totalDuration) {
        var durations = new List<double>();
        var segmentStart = keyframeTimes[0];
        foreach (var keyframe in keyframeTimes) {
            if (keyframe - segmentStart >= SegmentDurationSeconds) {
                durations.Add(keyframe - segmentStart);
                segmentStart = keyframe;
            }
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
    internal static string BuildRemuxVodPlaylist(IReadOnlyList<double> segmentDurations) {
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
            "#EXT-X-MAP:URI=\"init.mp4\"",
        };

        for (var index = 0; index < segmentDurations.Count; index++) {
            lines.Add(string.Format(CultureInfo.InvariantCulture, "#EXTINF:{0:0.000000},", segmentDurations[index]));
            lines.Add($"seg_{index:00000}.m4s");
        }

        lines.Add("#EXT-X-ENDLIST");
        lines.Add(string.Empty);
        return string.Join('\n', lines);
    }

    private static bool IsHevcCodec(string? codec) =>
        codec is not null &&
        (codec.Equals("hevc", StringComparison.OrdinalIgnoreCase) ||
            codec.Equals("h265", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Chooses the HEVC sample-entry codec tag (and any required muxer flags) for a stream copy.
    /// </summary>
    /// <remarks>
    /// Browsers require an explicit tag because the source's hev1 tag (or no tag, from an MKV) does not
    /// play in fMP4, and the tag must match the stream's actual signal:
    /// <list type="bullet">
    /// <item>Dolby Vision (Profile 7/8 with an RPU) is tagged <c>dvh1</c> and muxed with <c>-strict -2</c>
    /// so the muxer writes the <c>dvvC</c> configuration box, keeping the stream properly signalled as
    /// Dolby Vision so the client's DV decoder handles it. Tagging a DV stream as plain <c>hvc1</c> drops
    /// the <c>dvvC</c> box while leaving the RPU NAL units in the bitstream, and browser decoders then
    /// reject it (the failure that otherwise forces a fallback transcode). Mirrors Jellyfin's
    /// DirectStream remux of Dolby Vision sources.</item>
    /// <item>Plain HEVC (SDR/HDR10/HLG) is tagged <c>hvc1</c>, which Safari/WebKit require.</item>
    /// </list>
    /// Non-HEVC sources need no tag override.
    /// </remarks>
    internal static IReadOnlyList<string> HevcSampleEntryTagArguments(VideoSourceFile source) {
        if (!IsHevcCodec(source.VideoCodec)) {
            return [];
        }

        return IsDolbyVision(source)
            ? ["-tag:v", "dvh1", "-strict", "-2"]
            : ["-tag:v", "hvc1"];
    }

    // True when the source's primary video stream carries a Dolby Vision layer (a profile and/or an
    // RPU). Such streams must be remuxed with the dvh1 tag and the dvvC configuration box so the
    // client's Dolby Vision decoder engages; a plain hvc1 tag mis-signals them and breaks playback.
    private static bool IsDolbyVision(VideoSourceFile source) {
        var stream = source.Streams?
            .Where(candidate => candidate.Type.Equals("Video", StringComparison.OrdinalIgnoreCase))
            .OrderBy(candidate => candidate.StreamIndex)
            .FirstOrDefault();
        return stream is not null && (stream.DvProfile is not null || stream.RpuPresentFlag == true);
    }
}
