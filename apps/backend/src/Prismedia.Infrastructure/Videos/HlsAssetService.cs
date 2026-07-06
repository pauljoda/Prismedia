using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Prismedia.Application.Settings;
using Prismedia.Application.Videos;
using Prismedia.Contracts.Jellyfin;
using Prismedia.Contracts.Media;
using Prismedia.Infrastructure.Media.Processing;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Processes;
using Prismedia.Infrastructure.Settings;

namespace Prismedia.Infrastructure.Videos;

/// <summary>
/// Filesystem-backed implementation that resolves generated HLS playback assets from the cache directory.
/// </summary>
public sealed partial class HlsAssetService : IHlsAssetService {
    private const int SegmentDurationSeconds = 6;
    private const int VirtualCacheFormatVersion = 9;

    // How far ahead of a running generation's start/frontier a requested segment may be while still
    // attaching to that generation instead of starting a new one. It must comfortably exceed the
    // player's forward buffer depth (now maxBufferLength ~30s ÷ 6s = 5 segments) so normal buffering
    // never falls outside the window and cold-starts a competing transcode; the small margin above
    // that absorbs jitter and brief encoder contention. A forward seek (or resume) beyond this window
    // starts a fresh generation AT the seek point instead of making the player wait while the linear
    // transcode grinds from its current position to the target — the cause of slow forward seeks. The
    // old 50-segment (300s) window was sized for the previous 240s client buffer; tightening it is
    // coupled with the buffer reduction in adaptiveHlsBufferConfig() and must move together with it.
    private const int ActiveGenerationReuseWindowSegments = 8;
    private static readonly TimeSpan SegmentPollInterval = TimeSpan.FromMilliseconds(100);
    private static readonly ConcurrentDictionary<string, VirtualRenditionGeneration> ActiveRenditions = new();
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> VirtualCacheRefreshLocks = new();

    // Serializes the "reuse an active generation or start a new one" decision per stream so that a
    // burst of near-simultaneous startup segment requests (the player filling its initial buffer)
    // collapses onto one forward transcode instead of each racing to spawn its own.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> VirtualGenerationLocks = new();

    private readonly HlsAssetServiceOptions _options;
    private readonly IVideoSourceService? _sources;
    private readonly ProcessExecutor? _processes;
    private readonly ILogger<HlsAssetService>? _logger;
    private readonly PrismediaDbContext? _db;

    /// <summary>
    /// Creates an HLS asset resolver rooted at the configured cache directory.
    /// </summary>
    /// <param name="options">Cache-root options for generated HLS packages.</param>
    public HlsAssetService(HlsAssetServiceOptions options) {
        _options = options;
    }

    /// <summary>
    /// Creates an HLS asset resolver that can also generate missing packages on demand.
    /// </summary>
    /// <param name="options">Cache-root options for generated HLS packages.</param>
    /// <param name="sources">Source video resolver used to locate the original media file.</param>
    /// <param name="processes">Process runner used to invoke ffmpeg.</param>
    /// <param name="logger">Logger for generation diagnostics.</param>
    public HlsAssetService(
        HlsAssetServiceOptions options,
        IVideoSourceService sources,
        ProcessExecutor processes,
        ILogger<HlsAssetService> logger)
        : this(options, sources, processes, logger, null) {
    }

    /// <summary>
    /// Creates an HLS asset resolver that can advertise generated trickplay
    /// playlists alongside adaptive video renditions.
    /// </summary>
    /// <param name="options">Cache-root options for generated HLS packages.</param>
    /// <param name="sources">Source video resolver used to locate the original media file.</param>
    /// <param name="processes">Process runner used to invoke ffmpeg.</param>
    /// <param name="logger">Logger for generation diagnostics.</param>
    /// <param name="db">Database used to discover generated trickplay resolutions.</param>
    public HlsAssetService(
        HlsAssetServiceOptions options,
        IVideoSourceService sources,
        ProcessExecutor processes,
        ILogger<HlsAssetService> logger,
        PrismediaDbContext? db) {
        _options = options;
        _sources = sources;
        _processes = processes;
        _logger = logger;
        _db = db;
    }

    /// <inheritdoc />
    public async Task<HlsAsset?> GetAssetAsync(
        Guid id,
        string assetPath,
        int? audioStreamIndex,
        CancellationToken cancellationToken) {
        var normalizedAssetPath = NormalizeAssetPath(assetPath);
        if (normalizedAssetPath is null) {
            return null;
        }

        var virtualAsset = await TryGetVirtualAssetAsync(id, normalizedAssetPath, audioStreamIndex, cancellationToken);
        if (virtualAsset is not null) {
            return virtualAsset;
        }

        return FindAsset(id, normalizedAssetPath);
    }

    private IEnumerable<string> CandidatePackageRoots(Guid id) {
        var cacheRoot = Path.GetFullPath(_options.CacheRoot);
        yield return Path.Combine(cacheRoot, "hls2", id.ToString());
        yield return Path.Combine(cacheRoot, "hls", id.ToString());
    }

    private HlsAsset? FindAsset(Guid id, string normalizedAssetPath) {
        foreach (var packageRoot in CandidatePackageRoots(id)) {
            var resolved = ResolveInside(packageRoot, normalizedAssetPath);
            if (resolved is not null && File.Exists(resolved)) {
                return new HlsAsset(
                    resolved,
                    MimeForExtension(Path.GetExtension(resolved)),
                    CacheControlForExtension(Path.GetExtension(resolved)));
            }
        }

        return null;
    }

    private async Task<HlsAsset?> TryGetVirtualAssetAsync(
        Guid id,
        string normalizedAssetPath,
        int? requestedAudioStreamIndex,
        CancellationToken cancellationToken) {
        if (_sources is null || _processes is null) {
            return null;
        }

        var source = await _sources.GetSourceAsync(id, cancellationToken);
        if (source is null || source.DurationSeconds is not > 0) {
            return null;
        }

        var renditions = RenditionsFor(source);
        await EnsureVirtualCacheAsync(id, source, renditions, cancellationToken);
        var selectedAudioStreamIndex = SelectAudioStreamIndex(source, requestedAudioStreamIndex);
        var audioCacheKey = AudioCacheKey(selectedAudioStreamIndex);

        if (normalizedAssetPath.Equals(JellyfinProtocol.Hls.MasterPlaylist, StringComparison.OrdinalIgnoreCase)) {
            var trickplayStreams = await GetTrickplayStreamsAsync(id, cancellationToken);
            var transcoderOptions = await ResolveTranscoderOptionsAsync(cancellationToken);
            return await WriteTextAssetAsync(
                VirtualPath(id, audioCacheKey, JellyfinProtocol.Hls.MasterPlaylist),
                BuildVirtualMasterPlaylist(
                    source,
                    renditions,
                    trickplayStreams,
                    selectedAudioStreamIndex,
                    transcoderOptions.EnableAdaptiveBitrate),
                ".m3u8",
                cancellationToken);
        }

        if (normalizedAssetPath.Equals(JellyfinProtocol.Hls.MainPlaylist, StringComparison.OrdinalIgnoreCase)) {
            var rendition = renditions.FirstOrDefault();
            if (rendition is null) {
                return null;
            }

            return await WriteTextAssetAsync(
                VirtualPath(id, audioCacheKey, "v", rendition.Name, JellyfinProtocol.Hls.IndexPlaylist),
                BuildVirtualVariantPlaylist(source.DurationSeconds.Value, selectedAudioStreamIndex),
                ".m3u8",
                cancellationToken);
        }

        var parts = normalizedAssetPath.Split('/');

        // Stream-copy (remux) assets: v/remux/{stream.m3u8|init.mp4|seg_NNNNN.m4s}. The video is
        // copied into fMP4 HLS rather than transcoded, for clients that can decode the source codec.
        if (parts.Length == 3 &&
            parts[0].Equals("v", StringComparison.OrdinalIgnoreCase) &&
            parts[1].Equals("remux", StringComparison.OrdinalIgnoreCase)) {
            return await TryGetRemuxAssetAsync(
                id, source, audioCacheKey, selectedAudioStreamIndex, parts[2], cancellationToken);
        }

        if (parts.Length == 3 &&
            parts[0].Equals("v", StringComparison.OrdinalIgnoreCase) &&
            IsVirtualVariantPlaylist(parts[2])) {
            var rendition = ResolveRendition(renditions, parts[1]);
            if (rendition is null) return null;

            return await WriteTextAssetAsync(
                VirtualPath(id, audioCacheKey, "v", rendition.Name, "index.m3u8"),
                BuildVirtualVariantPlaylist(source.DurationSeconds.Value, selectedAudioStreamIndex),
                ".m3u8",
                cancellationToken);
        }

        if (parts.Length == 3 && parts[0].Equals("v", StringComparison.OrdinalIgnoreCase)) {
            var rendition = ResolveRendition(renditions, parts[1]);
            var segmentIndex = ParseSegmentIndex(parts[2]);
            if (rendition is null || segmentIndex is null) {
                return null;
            }

            string segmentPath;
            try {
                segmentPath = await GetVirtualSegmentAsync(
                    id,
                    source,
                    rendition,
                    audioCacheKey,
                    selectedAudioStreamIndex,
                    segmentIndex.Value,
                    cancellationToken);
            } catch (FileNotFoundException ex) {
                _logger?.LogWarning(
                    ex,
                    "Virtual HLS segment {SegmentIndex} was not generated for {VideoId}/{Rendition}.",
                    segmentIndex.Value,
                    id,
                    rendition.Name);
                return null;
            } catch (InvalidOperationException ex) {
                _logger?.LogWarning(
                    ex,
                    "Virtual HLS segment {SegmentIndex} failed to generate for {VideoId}/{Rendition}.",
                    segmentIndex.Value,
                    id,
                    rendition.Name);
                return null;
            }

            return new HlsAsset(segmentPath, MediaContentTypes.VideoMp2t, "public, max-age=31536000, immutable");
        }

        return null;
    }

    private async Task EnsureVirtualCacheAsync(
        Guid id,
        VideoSourceFile source,
        IReadOnlyList<VirtualHlsRendition> renditions,
        CancellationToken cancellationToken) {
        var refreshLock = VirtualCacheRefreshLocks.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
        await refreshLock.WaitAsync(cancellationToken);
        try {
            await EnsureVirtualCacheUnderLockAsync(id, source, renditions, cancellationToken);
        } finally {
            refreshLock.Release();
        }
    }

    private async Task EnsureVirtualCacheUnderLockAsync(
        Guid id,
        VideoSourceFile source,
        IReadOnlyList<VirtualHlsRendition> renditions,
        CancellationToken cancellationToken) {
        var root = VirtualRoot(id);
        var metaPath = Path.Combine(root, "metadata.json");
        var sourceInfo = new FileInfo(source.Path);
        var transcoderOptions = await ResolveTranscoderOptionsAsync(cancellationToken);
        var transcoderProfile = ResolveTranscoderProfile(transcoderOptions);
        var effectiveTranscoderProfile = ResolveEffectiveTranscoderProfile(source, transcoderProfile);
        var nextMeta = new VirtualCacheMetadata(
            source.Path,
            sourceInfo.Length,
            sourceInfo.LastWriteTimeUtc,
            source.DurationSeconds!.Value,
            renditions.Select(rendition => rendition.Name).ToArray(),
            effectiveTranscoderProfile.ToString(),
            VirtualCacheFormatVersion);

        if (File.Exists(metaPath)) {
            try {
                var existing = JsonSerializer.Deserialize<VirtualCacheMetadata>(
                    await File.ReadAllTextAsync(metaPath, cancellationToken));
                if (IsSameVirtualCache(existing, nextMeta)) {
                    return;
                }
            } catch {
                // Invalid metadata is treated as stale cache.
            }

            Directory.Delete(root, recursive: true);
        }

        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(
            metaPath,
            JsonSerializer.Serialize(nextMeta, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);
    }

    private async Task<HlsAsset> WriteTextAssetAsync(
        string path,
        string content,
        string extension,
        CancellationToken cancellationToken) {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content, cancellationToken);
        return new HlsAsset(path, MimeForExtension(extension), CacheControlForExtension(extension));
    }

    private async Task<string> GetVirtualSegmentAsync(
        Guid id,
        VideoSourceFile source,
        VirtualHlsRendition rendition,
        string audioCacheKey,
        int? audioStreamIndex,
        int segmentIndex,
        CancellationToken cancellationToken) {
        if (segmentIndex < 0 || segmentIndex >= SegmentCount(source.DurationSeconds!.Value)) {
            throw new FileNotFoundException("HLS segment index is outside the video duration.");
        }

        var outputPath = VirtualPath(id, audioCacheKey, "v", rendition.Name, $"seg_{segmentIndex:00000}.ts");
        if (File.Exists(outputPath) && new FileInfo(outputPath).Length > 0) {
            return outputPath;
        }

        // Decide-and-start under a per-stream lock so concurrent startup requests reuse one
        // generation instead of each spawning a parallel transcode that would then contend for the
        // hardware encoder. Waiting for the segment happens outside the lock so multiple waiters can
        // pull from the same running generation concurrently.
        var generationLock = VirtualGenerationLocks.GetOrAdd(
            $"{id}/{audioCacheKey}/{rendition.Name}",
            _ => new SemaphoreSlim(1, 1));
        await generationLock.WaitAsync(cancellationToken);
        VirtualRenditionGeneration generation;
        try {
            if (File.Exists(outputPath) && new FileInfo(outputPath).Length > 0) {
                return outputPath;
            }

            var activeGeneration = FindActiveRenditionGeneration(id, rendition, audioCacheKey, segmentIndex);
            if (activeGeneration is null) {
                CancelActiveRenditionGenerations(id, audioCacheKey, rendition);
                activeGeneration = StartVirtualRenditionGeneration(
                    id, source, rendition, audioCacheKey, audioStreamIndex, PrerollSegmentIndex(segmentIndex));
            }

            generation = activeGeneration;
        } finally {
            generationLock.Release();
        }

        await WaitForVirtualSegmentAsync(id, rendition, audioCacheKey, segmentIndex, outputPath, generation, cancellationToken);
        return outputPath;
    }

    private static int PrerollSegmentIndex(int requestedSegmentIndex) =>
        requestedSegmentIndex <= 0 ? 0 : requestedSegmentIndex - 1;

    private static VirtualRenditionGeneration? FindActiveRenditionGeneration(
        Guid id,
        VirtualHlsRendition rendition,
        string audioCacheKey,
        int segmentIndex) {
        var prefix = $"{id}/{audioCacheKey}/{rendition.Name}/";
        foreach (var (key, generation) in ActiveRenditions) {
            if (key.StartsWith(prefix, StringComparison.Ordinal) &&
                segmentIndex >= generation.StartSegment &&
                segmentIndex <= generation.EndSegment &&
                ShouldReuseActiveGeneration(generation, segmentIndex)) {
                return generation;
            }
        }

        return null;
    }

    private static bool ShouldReuseActiveGeneration(VirtualRenditionGeneration generation, int segmentIndex) {
        // Never reuse for a segment before the generation's start (a backward seek): it will never
        // produce that segment.
        if (segmentIndex < generation.StartSegment) {
            return false;
        }

        // Close to the start the generation will reach the segment quickly.
        if (segmentIndex - generation.StartSegment <= ActiveGenerationReuseWindowSegments) {
            return true;
        }

        // Otherwise reuse only when the segment is already produced, or within the reuse window of
        // the generation's current production frontier (so normal look-ahead attaches to the running
        // transcode, while a seek far beyond the frontier still starts a fresh generation).
        var stagedPath = Path.Combine(generation.StagingDirectory, $"seg_{segmentIndex:00000}.ts");
        if (File.Exists(stagedPath) && new FileInfo(stagedPath).Length > 0) {
            return true;
        }

        var frontier = LatestStagedSegment(generation.StagingDirectory);
        return frontier >= 0 && segmentIndex - frontier <= ActiveGenerationReuseWindowSegments;
    }

    private static int LatestStagedSegment(string stagingDirectory) {
        if (!Directory.Exists(stagingDirectory)) {
            return -1;
        }

        var latest = -1;
        foreach (var path in Directory.EnumerateFiles(stagingDirectory, "seg_*.ts")) {
            if (ParseSegmentIndex(Path.GetFileName(path)) is { } index && index > latest) {
                latest = index;
            }
        }

        return latest;
    }

    private static void CancelActiveRenditionGenerations(
        Guid id,
        string audioCacheKey,
        VirtualHlsRendition rendition) {
        var prefix = $"{id}/{audioCacheKey}/{rendition.Name}/";
        CancelActiveGenerationsByPrefix(prefix);
    }

    internal static int CancelActiveGenerationsForItem(Guid id) {
        if (id == Guid.Empty) {
            return 0;
        }

        return CancelActiveGenerationsByPrefix($"{id}/");
    }

    internal static int CancelAllActiveGenerations() =>
        CancelActiveGenerationsByPrefix(string.Empty);

    private static int CancelActiveGenerationsByPrefix(string prefix) {
        var cancelled = 0;
        foreach (var (key, generation) in ActiveRenditions) {
            if (key.StartsWith(prefix, StringComparison.Ordinal) && ActiveRenditions.TryRemove(key, out var removed)) {
                removed.Cancellation.Cancel();
                cancelled++;
            }
        }

        // Remux keys share the "{itemId}/..." shape, so the same prefix reaches stream-copy jobs;
        // this is what lets an explicit Stop (or a global purge) actually halt a running remux.
        foreach (var (key, remux) in RemuxGenerations) {
            if (key.StartsWith(prefix, StringComparison.Ordinal) && RemuxGenerations.TryRemove(key, out var removed)) {
                removed.Cancellation.Cancel();
                cancelled++;
            }
        }

        return cancelled;
    }

    /// <summary>
    /// Cancels transcode and remux jobs that no longer have a live viewer, or that have run past a
    /// hard lifetime ceiling. A job is reaped when its owning item is absent from <paramref name="liveItemIds"/>
    /// and it has been running longer than <paramref name="idleGrace"/> (a startup guard), or
    /// unconditionally once it exceeds <paramref name="maxLifetime"/>. Already-produced segments stay in
    /// the cache, so a reaped session resumes from cache and only re-encodes from the frontier.
    /// </summary>
    /// <param name="liveItemIds">Item ids whose playback session pinged within the liveness window.</param>
    /// <param name="idleGrace">Minimum job age before an orphaned job becomes eligible for reaping.</param>
    /// <param name="maxLifetime">Absolute age after which any job is cancelled, even a live one.</param>
    /// <returns>The number of jobs cancelled.</returns>
    internal static int ReapOrphanedJobs(IReadOnlySet<Guid> liveItemIds, TimeSpan idleGrace, TimeSpan maxLifetime) {
        var now = DateTimeOffset.UtcNow;
        var reaped = 0;

        foreach (var (key, generation) in ActiveRenditions) {
            if (ShouldReapJob(generation.EntityId, generation.StartedAtUtc, now, liveItemIds, idleGrace, maxLifetime) &&
                ActiveRenditions.TryRemove(key, out var removed)) {
                removed.Cancellation.Cancel();
                reaped++;
            }
        }

        foreach (var (key, remux) in RemuxGenerations) {
            // An in-progress copy that was requested recently is being actively played, even if its
            // session ping lapsed (hls.js can go quiet for the length of its buffer). Never cancel it —
            // cancelling orphans the job, and the next request restarts it, which is exactly the churn
            // that used to break playback. Only reap copies with no recent request (or past max age).
            var recentlyRequested = RemuxLastRequestedUtc.TryGetValue(key, out var lastRequested) &&
                now - lastRequested < RemuxActiveWindow;
            if (!remux.Task.IsCompleted &&
                (now - remux.StartedAtUtc) <= maxLifetime &&
                recentlyRequested) {
                continue;
            }

            if (!remux.Task.IsCompleted &&
                ShouldReapJob(remux.EntityId, remux.StartedAtUtc, now, liveItemIds, idleGrace, maxLifetime) &&
                RemuxGenerations.TryRemove(key, out var removed)) {
                removed.Cancellation.Cancel();
                RemuxLastRequestedUtc.TryRemove(key, out _);
                reaped++;
            }
        }

        return reaped;
    }

    // A remux copy requested within this window is treated as actively played and never reaped (unless
    // it exceeds the absolute max lifetime). Comfortably longer than the client's forward buffer so a
    // quiet, fully-buffered player does not look abandoned. The copy itself finishes in ~3 min anyway.
    private static readonly TimeSpan RemuxActiveWindow = TimeSpan.FromMinutes(5);

    private static bool ShouldReapJob(
        Guid entityId,
        DateTimeOffset startedAtUtc,
        DateTimeOffset now,
        IReadOnlySet<Guid> liveItemIds,
        TimeSpan idleGrace,
        TimeSpan maxLifetime) {
        var age = now - startedAtUtc;
        if (age > maxLifetime) {
            return true;
        }

        return age > idleGrace && !liveItemIds.Contains(entityId);
    }

    private static string? NormalizeAssetPath(string assetPath) {
        var normalized = assetPath.Replace('\\', '/').TrimStart('/');
        if (string.IsNullOrWhiteSpace(normalized) ||
            normalized.Contains("..", StringComparison.Ordinal) ||
            Path.IsPathRooted(normalized)) {
            return null;
        }

        return normalized;
    }

    private static bool IsVirtualVariantPlaylist(string assetName) =>
        assetName.Equals(JellyfinProtocol.Hls.IndexPlaylist, StringComparison.OrdinalIgnoreCase) ||
        assetName.Equals(JellyfinProtocol.Hls.StreamPlaylist, StringComparison.OrdinalIgnoreCase);

    private static string? ResolveInside(string root, string assetPath) {
        var rootFullPath = Path.GetFullPath(root);
        var resolved = Path.GetFullPath(Path.Combine(rootFullPath, assetPath));
        var rootWithSeparator = rootFullPath.EndsWith(Path.DirectorySeparatorChar)
            ? rootFullPath
            : rootFullPath + Path.DirectorySeparatorChar;

        return resolved == rootFullPath ||
            resolved.StartsWith(rootWithSeparator, StringComparison.Ordinal)
                ? resolved
                : null;
    }

    private string VirtualRoot(Guid id) =>
        Path.Combine(Path.GetFullPath(_options.CacheRoot), "hlsv", id.ToString());

    private string VirtualPath(Guid id, params string[] parts) =>
        Path.Combine([VirtualRoot(id), .. parts]);

    private static string AudioCacheKey(int? audioStreamIndex) =>
        audioStreamIndex is null ? "audio_default" : $"audio_{audioStreamIndex.Value:00000}";

    private static int? SelectAudioStreamIndex(VideoSourceFile source, int? requestedAudioStreamIndex) {
        var audioStreams = source.Streams?
            .Where(stream => stream.Type.Equals("Audio", StringComparison.OrdinalIgnoreCase))
            .OrderBy(stream => stream.StreamIndex)
            .ToList() ?? [];

        if (requestedAudioStreamIndex is not null &&
            (audioStreams.Count == 0 || audioStreams.Any(stream => stream.StreamIndex == requestedAudioStreamIndex.Value))) {
            return requestedAudioStreamIndex.Value;
        }

        return audioStreams.FirstOrDefault(stream => stream.IsDefault)?.StreamIndex ??
            audioStreams.FirstOrDefault()?.StreamIndex;
    }

    private static IReadOnlyList<VirtualHlsRendition> RenditionsFor(VideoSourceFile source) {
        var sourceHeight = NormalizeRenditionHeight(source.Height ?? 720);
        var sourceBitrate = SourceVideoBitrate(source);
        return JellyfinQualityOptions(sourceBitrate, source.VideoCodec)
            .Select(option => RenditionForQualityOption(option, sourceHeight))
            .GroupBy(rendition => rendition.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }

    private static VirtualHlsRendition? ResolveRendition(
        IReadOnlyList<VirtualHlsRendition> renditions,
        string name) =>
        renditions.FirstOrDefault(candidate =>
            candidate.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ??
        ResolveHeightRenditionAlias(renditions, name);

    private static VirtualHlsRendition? ResolveHeightRenditionAlias(
        IReadOnlyList<VirtualHlsRendition> renditions,
        string name) {
        if (!name.EndsWith('p') || !int.TryParse(name[..^1], out var height)) {
            return null;
        }

        return renditions
            .Where(rendition => rendition.Height == height)
            .OrderByDescending(rendition => ToBitsPerSecond(rendition.VideoBitrate))
            .FirstOrDefault();
    }

    private static string BuildVirtualMasterPlaylist(
        VideoSourceFile source,
        IReadOnlyList<VirtualHlsRendition> renditions,
        IReadOnlyList<VirtualTrickplayStream> trickplayStreams,
        int? audioStreamIndex,
        bool adaptiveBitrate) {
        // Default to a single rung (the highest, source-capped quality) like the reference media
        // server: advertising the full ladder lets the player switch quality and spawn a second
        // concurrent transcode, which is the main way one viewer pins the CPU. Adaptive bitrate is
        // opt-in via settings; when on, the full ladder is advertised.
        var advertised = adaptiveBitrate || renditions.Count == 0
            ? renditions
            : renditions.Take(1).ToArray();
        var lines = new List<string> { "#EXTM3U", "#EXT-X-VERSION:6" };
        foreach (var rendition in advertised) {
            var width = ScaledWidth(source.Width, source.Height, rendition.Height);
            var resolution = width is null ? "" : $",RESOLUTION={width}x{rendition.Height}";
            var codecs = H264CodecForHeight(rendition.Height);
            var bandwidth = ToBitsPerSecond(rendition.VideoBitrate);
            lines.Add(
                $"#EXT-X-STREAM-INF:BANDWIDTH={bandwidth},AVERAGE-BANDWIDTH={bandwidth}{resolution},CODECS=\"{codecs},mp4a.40.2\"");
            lines.Add(AppendAudioStreamQuery($"hls/{rendition.Name}/{JellyfinProtocol.Hls.StreamPlaylist}", audioStreamIndex));
        }

        foreach (var stream in trickplayStreams) {
            lines.Add(
                $"#EXT-X-IMAGE-STREAM-INF:BANDWIDTH={Math.Max(0, stream.Bandwidth)},RESOLUTION={stream.Width}x{stream.Height},CODECS=\"jpeg\",URI=\"Trickplay/{stream.Width}/tiles.m3u8\"");
        }

        lines.Add(string.Empty);
        return string.Join('\n', lines);
    }

    private async Task<IReadOnlyList<VirtualTrickplayStream>> GetTrickplayStreamsAsync(
        Guid id,
        CancellationToken cancellationToken) {
        if (_db is null) {
            return [];
        }

        return await _db.TrickplayInfos.AsNoTracking()
            .Where(row => row.EntityId == id)
            .OrderBy(row => row.Width)
            .Select(row => new VirtualTrickplayStream(row.Width, row.Height, row.Bandwidth))
            .ToListAsync(cancellationToken);
    }

    private static string BuildVirtualVariantPlaylist(double durationSeconds, int? audioStreamIndex) {
        var total = SegmentCount(durationSeconds);
        var durations = Enumerable.Range(0, total)
            .Select(index => SegmentDuration(durationSeconds, index))
            .ToArray();
        var targetDuration = durations.Length == 0
            ? SegmentDurationSeconds
            : (int)Math.Ceiling(durations.Max());
        var lines = new List<string>
        {
            "#EXTM3U",
            "#EXT-X-VERSION:6",
            "#EXT-X-PLAYLIST-TYPE:VOD",
            $"#EXT-X-TARGETDURATION:{targetDuration}",
            "#EXT-X-MEDIA-SEQUENCE:0",
            "#EXT-X-INDEPENDENT-SEGMENTS"
        };

        for (var index = 0; index < total; index++) {
            lines.Add($"#EXTINF:{SegmentDuration(durationSeconds, index):0.000000},");
            lines.Add(AppendAudioStreamQuery($"seg_{index:00000}.ts", audioStreamIndex));
        }

        lines.Add("#EXT-X-ENDLIST");
        lines.Add(string.Empty);
        return string.Join('\n', lines);
    }

    private static string AppendAudioStreamQuery(string url, int? audioStreamIndex) {
        if (audioStreamIndex is null || url.Contains("AudioStreamIndex=", StringComparison.OrdinalIgnoreCase)) {
            return url;
        }

        var separator = url.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return $"{url}{separator}AudioStreamIndex={audioStreamIndex.Value}";
    }

}
