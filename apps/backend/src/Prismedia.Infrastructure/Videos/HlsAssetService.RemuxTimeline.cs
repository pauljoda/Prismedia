using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Prismedia.Application.Videos;
using Prismedia.Infrastructure.Processes;

namespace Prismedia.Infrastructure.Videos;

/// <summary>
/// Segment-timeline modelling for the stream-copy (remux) HLS path: probing the source's segment
/// boundaries, caching them durably, and turning them into a media playlist that describes the media
/// the fragments actually contain.
/// </summary>
/// <remarks>
/// This is separated from the job orchestration in <c>HlsAssetService.Remux.cs</c> because getting the
/// timeline right is subtle and independent of running ffmpeg. The subtlety is open GOPs: ffmpeg cuts a
/// copy in decode order at any IRAP picture, so an open-GOP source (x265's default) starts every segment
/// after the first on a CRA whose RASL leading pictures present BEFORE it. A playlist that dates each
/// segment from its keyframe therefore claims a start the media does not have, leaving a hole at every
/// boundary. hls.js re-aligns from the fragments' own timestamps and hides this; WebKit's MSE and
/// AVFoundation stall, re-fetch the segment, and reset the pipeline. Everything here exists to describe
/// the real presentation timeline instead.
/// </remarks>
public sealed partial class HlsAssetService {
    /// <summary>
    /// One stream-copy segment boundary: the keyframe ffmpeg cuts at, and the earliest presentation
    /// time of the pictures that decode along with it.
    /// </summary>
    /// <remarks>
    /// The two differ whenever the source has open GOPs. ffmpeg cuts a copy in DECODE order at an IRAP
    /// picture, but an open-GOP CRA is followed in decode order by RASL leading pictures that PRESENT
    /// before it. The resulting fragment therefore begins playing earlier than the keyframe's own
    /// timestamp, and a playlist that dates the segment from the keyframe understates its start by the
    /// lead — leaving a hole at every boundary and overlapping the previous segment. Recording both lets
    /// the playlist describe the media that is actually there.
    /// </remarks>
    /// <param name="KeyframePts">Presentation timestamp of the IRAP picture ffmpeg cuts at.</param>
    /// <param name="PresentationStartPts">Earliest presentation timestamp in the resulting segment.</param>
    internal readonly record struct RemuxKeyframe(double KeyframePts, double PresentationStartPts) {
        /// <summary>True when the segment starting here presents exactly at its keyframe.</summary>
        public bool IsIndependent => PresentationStartPts >= KeyframePts - IndependenceEpsilonSeconds;
    }

    // Tolerance for "the segment presents at its keyframe". Well under one frame at any sane rate, so a
    // rounding artefact in the probe cannot be mistaken for real leading pictures.
    private const double IndependenceEpsilonSeconds = 0.001;

    // How much of the source the bounded independence probe reads. Long enough to cover several GOPs of
    // any realistic encode (so an open-GOP source cannot look closed by luck), short enough to stay a
    // sub-second read on the request thread — unlike the whole-file walk it replaces for this purpose.
    private const int IndependenceProbeSeconds = 60;

    /// <summary>
    /// Computes the exact per-segment boundaries the stream-copy remux will produce for one source.
    /// </summary>
    /// <returns>
    /// Segment boundaries in playlist order, or null when the keyframe layout cannot be probed (in
    /// which case the caller falls back to ffmpeg's own playlist).
    /// </returns>
    private async Task<IReadOnlyList<RemuxKeyframe>?> ComputeRemuxKeyframesAsync(
        Guid id,
        VideoSourceFile source,
        HlsAssetServiceOptions options,
        CancellationToken cancellationToken) {
        if (source.DurationSeconds is not > 0) {
            return null;
        }

        // The durable cache holds a previous exact walk. Otherwise walk the whole file: this runs off the
        // request thread precisely so it may be slow, and it is the only source that knows each segment's
        // true presentation start on an open-GOP encode.
        var keyframes = TryReadDurableKeyframes(id, source)
            ?? await ProbeVideoKeyframesAsync(source.Path, options, cancellationToken);
        if (keyframes is not { Count: > 0 }) {
            return null;
        }

        TryWriteDurableKeyframes(id, source, keyframes);
        return keyframes;
    }

    /// <summary>
    /// Resolves segment boundaries without reading the whole file, or null when that is not possible.
    /// </summary>
    /// <remarks>
    /// Two cheap sources exist: the durable per-video cache (already exact), and the Matroska Cues index.
    /// Cues list keyframe positions only — they say nothing about leading pictures — so they are trusted
    /// only once a bounded probe has established that this source's segments really do start on IDR
    /// pictures. An open-GOP source falls through to the background whole-file walk instead of being
    /// described by a playlist that cannot account for its RASL leads.
    /// </remarks>
    private async Task<IReadOnlyList<RemuxKeyframe>?> TryFastKeyframesAsync(
        Guid id,
        VideoSourceFile source,
        HlsAssetServiceOptions options,
        CancellationToken cancellationToken) {
        if (TryReadDurableKeyframes(id, source) is { Count: > 0 } cached) {
            return cached;
        }

        if (MatroskaKeyframeReader.TryReadKeyframeTimes(source.Path, _logger) is not { Count: > 1 } cues) {
            return null;
        }

        if (await ProbeSegmentsAreIndependentAsync(source.Path, options, cancellationToken) is not true) {
            return null;
        }

        return ClosedGopKeyframes(cues);
    }

    /// <summary>
    /// Reads a bounded window of the source and reports whether every keyframe in it starts a segment
    /// that presents at the keyframe itself (a closed GOP).
    /// </summary>
    /// <returns>
    /// True when no leading pictures were seen, false when any were, and null when the probe could not
    /// run or the window held too few keyframes to judge.
    /// </returns>
    private async Task<bool?> ProbeSegmentsAreIndependentAsync(
        string sourcePath,
        HlsAssetServiceOptions options,
        CancellationToken cancellationToken) {
        var sampled = await ProbeVideoKeyframesAsync(
            sourcePath,
            options,
            cancellationToken,
            IndependenceProbeSeconds);
        if (sampled is not { Count: > 1 }) {
            return null;
        }

        // Skip the first boundary: it is the start of the stream, which has nothing decodable before it
        // and so never carries leading pictures regardless of the GOP structure.
        return sampled.Skip(1).All(keyframe => keyframe.IsIndependent);
    }

    // Durable per-video keyframe cache, stored OUTSIDE the evictable transcode cache roots
    // (hlsv/hls/hls2) so the transcode-cache size cap cannot delete it. Keyed by video id and
    // validated against the source's path/size/modified time so a replaced file recomputes.
    private string KeyframeCachePath(Guid id) =>
        Path.Combine(Path.GetFullPath(_options.CacheRoot), "keyframes", $"{id}.json");

    private IReadOnlyList<RemuxKeyframe>? TryReadDurableKeyframes(Guid id, VideoSourceFile source) {
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
                cache.FormatVersion != KeyframeCacheFormatVersion ||
                !string.Equals(cache.SourcePath, source.Path, StringComparison.Ordinal) ||
                cache.SourceSize != info.Length ||
                cache.SourceModifiedUtc != info.LastWriteTimeUtc) {
                return null;
            }

            return cache.Keyframes;
        } catch {
            return null;
        }
    }

    private void TryWriteDurableKeyframes(Guid id, VideoSourceFile source, IReadOnlyList<RemuxKeyframe> keyframes) {
        try {
            var info = new FileInfo(source.Path);
            if (!info.Exists) {
                return;
            }

            var path = KeyframeCachePath(id);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var payload = JsonSerializer.Serialize(
                new DurableKeyframeCache(
                    KeyframeCacheFormatVersion,
                    source.Path,
                    info.Length,
                    info.LastWriteTimeUtc,
                    keyframes));
            var tempPath = path + "." + Path.GetRandomFileName();
            File.WriteAllText(tempPath, payload);
            File.Move(tempPath, path, overwrite: true);
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "Failed to persist the keyframe cache for {VideoId}.", id);
        }
    }

    // Bumped when the cached shape changes so older entries recompute instead of being misread. Version 2
    // added each segment's presentation start alongside its keyframe time; a version 1 entry holds
    // keyframe times only and cannot describe an open-GOP source.
    private const int KeyframeCacheFormatVersion = 2;

    private sealed record DurableKeyframeCache(
        int FormatVersion,
        string SourcePath,
        long SourceSize,
        DateTime SourceModifiedUtc,
        IReadOnlyList<RemuxKeyframe> Keyframes);

    /// <summary>
    /// Reads every video keyframe from the source together with the earliest presentation timestamp of
    /// the pictures that decode with it.
    /// </summary>
    /// <remarks>
    /// Uses packet-level probing (no decode) so it stays fast on long files. ffprobe emits packets in
    /// DECODE order, which is the order ffmpeg's HLS muxer sees when deciding stream-copy boundaries, so
    /// the packets between one keyframe and the next are exactly one segment's contents. Tracking the
    /// minimum presentation timestamp across that run is what reveals an open GOP's RASL leading
    /// pictures: they decode after their CRA but present before it. Do NOT sort the keyframe list —
    /// decode order is the information being read.
    /// </remarks>
    /// <param name="sourcePath">Absolute path to the source file.</param>
    /// <param name="options">Transcoder options carrying the ffprobe path.</param>
    /// <param name="cancellationToken">Token cancelling the probe.</param>
    /// <param name="limitSeconds">When set, read only this many seconds from the start of the source.</param>
    private async Task<IReadOnlyList<RemuxKeyframe>?> ProbeVideoKeyframesAsync(
        string sourcePath,
        HlsAssetServiceOptions options,
        CancellationToken cancellationToken,
        int? limitSeconds = null) {
        if (_processes is null) {
            return null;
        }

        var arguments = new List<string> { "-v", "error" };
        if (limitSeconds is { } window) {
            arguments.AddRange(["-read_intervals", $"%+{window.ToString(CultureInfo.InvariantCulture)}"]);
        }

        arguments.AddRange([
            "-select_streams", "v:0",
            "-show_entries", "packet=pts_time,flags",
            "-of", "csv=print_section=0",
            sourcePath,
        ]);

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

        return ParseRemuxKeyframes(result.StandardOutput);
    }

    /// <summary>
    /// Parses ffprobe's decode-ordered <c>pts_time,flags</c> packet listing into segment boundaries.
    /// </summary>
    /// <param name="probeOutput">Raw ffprobe CSV output, one packet per line in decode order.</param>
    /// <returns>Boundaries in decode order, each carrying its run's earliest presentation timestamp.</returns>
    internal static IReadOnlyList<RemuxKeyframe> ParseRemuxKeyframes(string probeOutput) {
        var keyframes = new List<double>();
        var presentationStarts = new List<double>();
        foreach (var line in probeOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
            var comma = line.IndexOf(',');
            if (comma <= 0) {
                continue;
            }

            if (!double.TryParse(line[..comma], NumberStyles.Float, CultureInfo.InvariantCulture, out var pts) ||
                !double.IsFinite(pts) || pts < 0) {
                continue;
            }

            // ffprobe emits the keyframe flag as a leading 'K' in the packet flags field.
            if (line[(comma + 1)..].IndexOf('K') >= 0) {
                keyframes.Add(pts);
                presentationStarts.Add(pts);
                continue;
            }

            // A non-keyframe packet belongs to the run opened by the last keyframe. Only a picture that
            // presents before that keyframe changes anything: it is a leading picture, and it moves the
            // segment's real start earlier.
            if (presentationStarts.Count > 0 && pts < presentationStarts[^1]) {
                presentationStarts[^1] = pts;
            }
        }

        var boundaries = new RemuxKeyframe[keyframes.Count];
        for (var index = 0; index < keyframes.Count; index++) {
            // The first segment has nothing decodable before it, so it always presents at its keyframe
            // even when the stream opens on a CRA whose leading pictures are discarded.
            boundaries[index] = index == 0
                ? new RemuxKeyframe(keyframes[0], keyframes[0])
                : new RemuxKeyframe(keyframes[index], presentationStarts[index]);
        }

        return boundaries;
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
    /// For example, keyframes <c>[0,19,20,25]</c> over a 30s source produce <c>[19,1,5,5]</c> (four
    /// segments). Jumping the threshold past the cut instead skips the keyframe at 20 and yields
    /// <c>[19,6,5]</c> (three segments) — so the VOD playlist we hand the player would reference the same
    /// <c>seg_NNNNN</c> filenames as ffmpeg's real output but with mismatched durations, corrupting
    /// seeking and cutting the buffer short of the true end.
    /// </remarks>
    internal static IReadOnlyList<double> BuildRemuxSegmentDurations(
        IReadOnlyList<double> keyframeTimes,
        double totalDuration) =>
        BuildRemuxSegmentDurations(ClosedGopKeyframes(keyframeTimes), totalDuration);

    /// <summary>
    /// Reads plain keyframe times as closed-GOP boundaries, where each segment presents at its keyframe.
    /// </summary>
    /// <remarks>
    /// Valid only for a source already established to have no leading pictures — a keyframe time on its
    /// own cannot reveal an open GOP's RASL lead. Callers reach this after
    /// <see cref="ProbeSegmentsAreIndependentAsync"/> has confirmed it.
    /// </remarks>
    /// <param name="keyframeTimes">Keyframe presentation timestamps in decode order.</param>
    /// <returns>Boundaries whose presentation start equals their keyframe.</returns>
    internal static IReadOnlyList<RemuxKeyframe> ClosedGopKeyframes(IReadOnlyList<double> keyframeTimes) =>
        keyframeTimes.Select(time => new RemuxKeyframe(time, time)).ToArray();

    /// <summary>
    /// Builds presentation-accurate segment durations from probed boundaries.
    /// </summary>
    /// <remarks>
    /// The cut decision is made on keyframe timestamps, because that is what ffmpeg's muxer compares
    /// against its rolling threshold (see the overload's remarks). The resulting <c>EXTINF</c> values,
    /// however, are measured between consecutive PRESENTATION starts, because that is what the player
    /// finds in the fragments. On a closed GOP the two are identical. On an open GOP they are not: each
    /// segment after the first really begins at its RASL leading pictures, roughly a frame or two before
    /// its CRA. Dating those segments from the keyframe leaves a hole at every boundary and overlaps the
    /// previous segment — tolerable for hls.js, which re-aligns from the fragments' own timestamps, but
    /// WebKit's MSE and AVFoundation stall, re-fetch the segment, and reset the pipeline instead.
    /// </remarks>
    /// <param name="keyframes">Probed segment boundaries in decode order.</param>
    /// <param name="totalDuration">Total source duration in seconds.</param>
    /// <returns>Segment durations in playlist order.</returns>
    internal static IReadOnlyList<double> BuildRemuxSegmentDurations(
        IReadOnlyList<RemuxKeyframe> keyframes,
        double totalDuration) {
        var durations = new List<double>();
        var segmentStart = keyframes[0].PresentationStartPts;
        var target = keyframes[0].KeyframePts + SegmentDurationSeconds;
        foreach (var keyframe in keyframes) {
            if (keyframe.KeyframePts < target) {
                continue;
            }

            durations.Add(keyframe.PresentationStartPts - segmentStart);
            segmentStart = keyframe.PresentationStartPts;
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
    /// Reports whether every segment the given boundaries produce starts on a picture that presents at
    /// its own keyframe, which is what <c>#EXT-X-INDEPENDENT-SEGMENTS</c> asserts.
    /// </summary>
    /// <param name="keyframes">Probed segment boundaries in decode order.</param>
    /// <returns>True when no boundary carries leading pictures.</returns>
    internal static bool SegmentsAreIndependent(IReadOnlyList<RemuxKeyframe> keyframes) =>
        keyframes.All(keyframe => keyframe.IsIndependent);

    /// <summary>
    /// Builds a complete fMP4 <c>VOD</c> media playlist for the remux segments, mirroring the tags
    /// ffmpeg writes (version, init map, independent segments) but listing every segment up front and
    /// terminating with <c>#EXT-X-ENDLIST</c> so the player treats the whole duration as seekable.
    /// </summary>
    internal static string BuildRemuxVodPlaylist(
        IReadOnlyList<double> segmentDurations,
        int? audioStreamIndex = null,
        bool copyAudio = false,
        bool independentSegments = true) {
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
        };

        // Only claim independence when the probe proved it. An open-GOP source's segments start on a CRA
        // with RASL leading pictures that reference pictures in the previous segment, so they cannot be
        // decoded alone; asserting otherwise is what makes strict clients treat a boundary as a hard
        // decode point and fail there.
        if (independentSegments) {
            lines.Add("#EXT-X-INDEPENDENT-SEGMENTS");
        }

        lines.Add($"#EXT-X-MAP:URI=\"{AppendPlaybackQuery("init.mp4", audioStreamIndex, copyAudio)}\"");

        for (var index = 0; index < segmentDurations.Count; index++) {
            lines.Add(string.Format(CultureInfo.InvariantCulture, "#EXTINF:{0:0.000000},", segmentDurations[index]));
            lines.Add(AppendPlaybackQuery($"seg_{index:00000}.m4s", audioStreamIndex, copyAudio));
        }

        lines.Add("#EXT-X-ENDLIST");
        lines.Add(string.Empty);
        return string.Join('\n', lines);
    }
}
