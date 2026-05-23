using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Prismedia.Application.Settings;
using Prismedia.Application.Videos;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Processes;
using Prismedia.Infrastructure.Settings;

namespace Prismedia.Infrastructure.Videos;

/// <summary>
/// Filesystem-backed implementation that resolves generated HLS playback assets from the cache directory.
/// </summary>
public sealed class HlsAssetService : IHlsAssetService {
    private const int SegmentDurationSeconds = 6;
    private const int VirtualCacheFormatVersion = 7;
    private const int ActiveGenerationReuseWindowSegments = 12;
    private static readonly TimeSpan SegmentPollInterval = TimeSpan.FromMilliseconds(100);
    private static readonly ConcurrentDictionary<string, VirtualRenditionGeneration> ActiveRenditions = new();
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> VirtualCacheRefreshLocks = new();

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

        if (normalizedAssetPath.Equals("master.m3u8", StringComparison.OrdinalIgnoreCase)) {
            var trickplayStreams = await GetTrickplayStreamsAsync(id, cancellationToken);
            return await WriteTextAssetAsync(
                VirtualPath(id, audioCacheKey, "master.m3u8"),
                BuildVirtualMasterPlaylist(source, renditions, trickplayStreams, selectedAudioStreamIndex),
                ".m3u8",
                cancellationToken);
        }

        var parts = normalizedAssetPath.Split('/');
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

            return new HlsAsset(segmentPath, "video/mp2t", "public, max-age=31536000, immutable");
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

        var generationStartSegment = PrerollSegmentIndex(segmentIndex);
        var generation = FindActiveRenditionGeneration(id, rendition, audioCacheKey, segmentIndex);
        if (generation is null) {
            CancelActiveAudioGenerations(id, audioCacheKey);
            generation = StartVirtualRenditionGeneration(id, source, rendition, audioCacheKey, audioStreamIndex, generationStartSegment);
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
        if (segmentIndex - generation.StartSegment <= ActiveGenerationReuseWindowSegments) {
            return true;
        }

        var stagedPath = Path.Combine(generation.StagingDirectory, $"seg_{segmentIndex:00000}.ts");
        return File.Exists(stagedPath) && new FileInfo(stagedPath).Length > 0;
    }

    private static void CancelActiveAudioGenerations(
        Guid id,
        string audioCacheKey) {
        var prefix = $"{id}/{audioCacheKey}/";
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

        return cancelled;
    }

    private VirtualRenditionGeneration StartVirtualRenditionGeneration(
        Guid id,
        VideoSourceFile source,
        VirtualHlsRendition rendition,
        string audioCacheKey,
        int? audioStreamIndex,
        int startSegment) {
        var endSegment = SegmentCount(source.DurationSeconds!.Value) - 1;
        var key = $"{id}/{audioCacheKey}/{rendition.Name}/{startSegment}";
        return ActiveRenditions.GetOrAdd(key, _ => {
            var stagingDirectory = VirtualPath(id, audioCacheKey, "v", rendition.Name, $".gen_{startSegment:00000}_{Guid.NewGuid():N}");
            var cancellation = new CancellationTokenSource();
            var generation = new VirtualRenditionGeneration(
                startSegment,
                endSegment,
                stagingDirectory,
                cancellation,
                Task.CompletedTask);
            generation = generation with {
                Task = GenerateVirtualRenditionAsync(
                    id,
                    source,
                    rendition,
                    audioCacheKey,
                    audioStreamIndex,
                    startSegment,
                    stagingDirectory,
                    key,
                    cancellation.Token)
            };
            return generation;
        });
    }

    private async Task GenerateVirtualRenditionAsync(
        Guid id,
        VideoSourceFile source,
        VirtualHlsRendition rendition,
        string audioCacheKey,
        int? audioStreamIndex,
        int startSegment,
        string stagingDirectory,
        string generationKey,
        CancellationToken cancellationToken) {
        if (_processes is null) {
            throw new InvalidOperationException("HLS rendition generation requires a process executor.");
        }

        var playlistPath = Path.Combine(stagingDirectory, "index.generated.m3u8");
        var segmentPattern = Path.Combine(stagingDirectory, "seg_%05d.ts");
        Directory.CreateDirectory(Path.GetDirectoryName(playlistPath)!);
        var transcoderOptions = await ResolveTranscoderOptionsAsync(cancellationToken);
        var transcoderProfile = ResolveTranscoderProfile(transcoderOptions);
        var effectiveTranscoderProfile = ResolveEffectiveTranscoderProfile(source, transcoderProfile);
        if (effectiveTranscoderProfile != transcoderProfile) {
            _logger?.LogInformation(
                "Virtual HLS generation for HDR/Dolby Vision source {VideoId} is using software tone mapping instead of {TranscoderProfile}.",
                id,
                transcoderProfile);
        }

        ProcessExecutionResult result;
        try {
            result = await _processes.RunAsync(
                transcoderOptions.FfmpegPath,
                VirtualRenditionArguments(
                    source,
                    rendition,
                    audioStreamIndex,
                    startSegment,
                    playlistPath,
                    segmentPattern,
                    effectiveTranscoderProfile,
                    transcoderOptions.VaapiDevice),
                environment: null,
                cancellationToken);

            if (result.ExitCode != 0 && effectiveTranscoderProfile != HlsTranscoderProfile.Software) {
                _logger?.LogWarning(
                    "Virtual HLS generation using {TranscoderProfile} failed for {VideoId} rendition {Rendition}; retrying with software x264. Error: {Error}",
                    effectiveTranscoderProfile,
                    id,
                    rendition.Name,
                    result.StandardError);

                ResetStagingDirectory(stagingDirectory);
                result = await _processes.RunAsync(
                    transcoderOptions.FfmpegPath,
                    VirtualRenditionArguments(
                        source,
                        rendition,
                        audioStreamIndex,
                        startSegment,
                        playlistPath,
                        segmentPattern,
                        HlsTranscoderProfile.Software,
                        transcoderOptions.VaapiDevice),
                    environment: null,
                    cancellationToken);
            }
        } finally {
            ActiveRenditions.TryRemove(generationKey, out var _);
        }

        if (result.ExitCode != 0) {
            _logger?.LogWarning(
                "Virtual HLS rendition generation failed for {VideoId} rendition {Rendition}: {Error}",
                id,
                rendition.Name,
                result.StandardError);
            throw new InvalidOperationException("HLS rendition generation failed.");
        }

        foreach (var segmentPath in Directory.EnumerateFiles(stagingDirectory, "seg_*.ts")) {
            CopySegmentToCanonical(id, rendition, audioCacheKey, segmentPath);
        }
    }

    private async Task WaitForVirtualSegmentAsync(
        Guid id,
        VirtualHlsRendition rendition,
        string audioCacheKey,
        int segmentIndex,
        string outputPath,
        VirtualRenditionGeneration generation,
        CancellationToken cancellationToken) {
        var stagedPath = Path.Combine(generation.StagingDirectory, $"seg_{segmentIndex:00000}.ts");
        while (true) {
            if (File.Exists(outputPath) && new FileInfo(outputPath).Length > 0) {
                return;
            }

            if (File.Exists(stagedPath) && new FileInfo(stagedPath).Length > 0) {
                CopySegmentToCanonical(id, rendition, audioCacheKey, stagedPath);
                return;
            }

            if (generation.Task.IsCompleted) {
                try {
                    await generation.Task;
                } catch (OperationCanceledException ex) {
                    throw new FileNotFoundException(
                        $"HLS rendition generation was replaced before segment {segmentIndex} was produced for {id}/{rendition.Name}.",
                        ex);
                }

                if (File.Exists(outputPath) && new FileInfo(outputPath).Length > 0) {
                    return;
                }

                if (File.Exists(stagedPath) && new FileInfo(stagedPath).Length > 0) {
                    CopySegmentToCanonical(id, rendition, audioCacheKey, stagedPath);
                    return;
                }

                throw new FileNotFoundException(
                    $"HLS rendition generation completed without segment {segmentIndex} for {id}/{rendition.Name}.");
            }

            await Task.Delay(SegmentPollInterval, cancellationToken);
        }
    }

    private void CopySegmentToCanonical(
        Guid id,
        VirtualHlsRendition rendition,
        string audioCacheKey,
        string stagedPath) {
        if (!File.Exists(stagedPath) || new FileInfo(stagedPath).Length == 0) {
            return;
        }

        var outputPath = VirtualPath(id, audioCacheKey, "v", rendition.Name, Path.GetFileName(stagedPath));
        if (File.Exists(outputPath) && new FileInfo(outputPath).Length > 0) {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var tempPath = $"{outputPath}.{Guid.NewGuid():N}.tmp";
        File.Copy(stagedPath, tempPath, overwrite: true);
        File.Move(tempPath, outputPath, overwrite: true);
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
        assetName.Equals("index.m3u8", StringComparison.OrdinalIgnoreCase) ||
        assetName.Equals("stream.m3u8", StringComparison.OrdinalIgnoreCase);

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
        int? audioStreamIndex) {
        var lines = new List<string> { "#EXTM3U", "#EXT-X-VERSION:6" };
        foreach (var rendition in renditions) {
            var width = ScaledWidth(source.Width, source.Height, rendition.Height);
            var resolution = width is null ? "" : $",RESOLUTION={width}x{rendition.Height}";
            var codecs = H264CodecForHeight(rendition.Height);
            var bandwidth = ToBitsPerSecond(rendition.VideoBitrate);
            lines.Add(
                $"#EXT-X-STREAM-INF:BANDWIDTH={bandwidth},AVERAGE-BANDWIDTH={bandwidth}{resolution},CODECS=\"{codecs},mp4a.40.2\"");
            lines.Add(AppendAudioStreamQuery($"hls/{rendition.Name}/stream.m3u8", audioStreamIndex));
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

    private static string AppendAudioStreamQuery(string url, int? audioStreamIndex) =>
        audioStreamIndex is null ? url : $"{url}?AudioStreamIndex={audioStreamIndex.Value}";

    private static IReadOnlyList<string> VirtualRenditionArguments(
        VideoSourceFile source,
        VirtualHlsRendition rendition,
        int? audioStreamIndex,
        int startSegment,
        string playlistPath,
        string segmentPattern,
        HlsTranscoderProfile transcoderProfile,
        string vaapiDevice) {
        var gop = Math.Max(1, (int)Math.Ceiling(SegmentDurationSeconds * (source.FrameRate ?? 24)));
        var startSeconds = startSegment * SegmentDurationSeconds;
        var arguments = new List<string>
        {
            "-hide_banner",
            "-y",
            "-loglevel",
            "error",
            "-nostats",
            "-ss",
            startSeconds.ToString("0.000")
        };

        if (transcoderProfile == HlsTranscoderProfile.Vaapi) {
            arguments.AddRange(["-vaapi_device", vaapiDevice]);
        }

        arguments.AddRange(
        [
            "-i",
            source.Path,
            "-map_metadata",
            "-1",
            "-map_chapters",
            "-1"
        ]);

        arguments.AddRange(VideoFilterArguments(source, rendition, transcoderProfile));

        arguments.AddRange(
        [
            "-map",
            "0:v:0",
            "-map",
            audioStreamIndex is null ? "0:a:0?" : $"0:{audioStreamIndex.Value}?"
        ]);

        arguments.AddRange(VideoEncoderArguments(rendition, transcoderProfile));

        arguments.AddRange(
        [
            "-force_key_frames:0",
            $"expr:gte(t,n_forced*{SegmentDurationSeconds})",
            "-g",
            gop.ToString(),
            "-keyint_min",
            gop.ToString(),
            "-sc_threshold",
            "0",
            "-c:a",
            "aac",
            "-b:a",
            rendition.AudioBitrate,
            "-ac",
            "2",
            "-ar",
            "48000",
            "-copyts",
            "-avoid_negative_ts",
            "disabled",
            "-max_muxing_queue_size",
            "128",
            "-f",
            "hls",
            "-max_delay",
            "5000000",
            "-hls_time",
            SegmentDurationSeconds.ToString(),
            "-hls_segment_type",
            "mpegts",
            "-hls_playlist_type",
            "vod",
            "-hls_list_size",
            "0",
            "-hls_flags",
            "temp_file",
            "-start_number",
            startSegment.ToString(),
            "-hls_segment_filename",
            segmentPattern,
            playlistPath
        ]);

        return arguments;
    }

    private static IReadOnlyList<string> VideoFilterArguments(
        VideoSourceFile source,
        VirtualHlsRendition rendition,
        HlsTranscoderProfile transcoderProfile) {
        if (NeedsToneMapping(source)) {
            return
            [
                "-vf",
                ToneMappingFilter(source, rendition)
            ];
        }

        if (transcoderProfile == HlsTranscoderProfile.Vaapi) {
            var width = ScaledWidth(source.Width, source.Height, rendition.Height);
            var scaleWidth = width?.ToString() ?? "-2";
            return
            [
                "-vf",
                $"format=nv12,hwupload,scale_vaapi=w={scaleWidth}:h={rendition.Height}:format=nv12"
            ];
        }

        var outputFormat = transcoderProfile == HlsTranscoderProfile.Qsv ? "nv12" : "yuv420p";
        return
        [
            "-vf",
            $"scale=w=-2:h={rendition.Height}:force_original_aspect_ratio=decrease:force_divisible_by=2,format={outputFormat}"
        ];
    }

    private static string InputHdrColorParameters(VideoSourceFile source) {
        var videoStream = PrimaryVideoStream(source);
        var transfer = videoStream?.ColorTransfer;
        var colorTransfer = string.Equals(transfer, "arib-std-b67", StringComparison.OrdinalIgnoreCase)
            ? "arib-std-b67"
            : "smpte2084";
        return $"setparams=color_primaries=bt2020:color_trc={colorTransfer}:colorspace=bt2020nc";
    }

    private static string ToneMappingFilter(VideoSourceFile source, VirtualHlsRendition rendition) {
        var scale = $"scale=w=-2:h={rendition.Height}:force_original_aspect_ratio=decrease:force_divisible_by=2";
        if (RequiresDolbyVisionToneMapping(source)) {
            return $"{InputHdrColorParameters(source)},{scale},tonemapx=tonemap=bt2390:desat=0:peak=400:t=bt709:m=bt709:p=bt709:format=yuv420p";
        }

        return $"{InputHdrColorParameters(source)},zscale=t=linear:npl=100,format=gbrpf32le,zscale=p=bt709,tonemap=tonemap=hable:desat=0:peak=100,zscale=t=bt709:m=bt709:p=bt709:out_range=tv,{scale},format=yuv420p";
    }

    private static IReadOnlyList<string> VideoEncoderArguments(
        VirtualHlsRendition rendition,
        HlsTranscoderProfile transcoderProfile) {
        var encoder = transcoderProfile switch {
            HlsTranscoderProfile.VideoToolbox => "h264_videotoolbox",
            HlsTranscoderProfile.Vaapi => "h264_vaapi",
            HlsTranscoderProfile.Nvenc => "h264_nvenc",
            HlsTranscoderProfile.Qsv => "h264_qsv",
            _ => "libx264"
        };

        var arguments = new List<string>
        {
            "-c:v",
            encoder
        };

        if (transcoderProfile == HlsTranscoderProfile.Software) {
            arguments.AddRange(
            [
                "-preset",
                "veryfast",
                "-crf",
                rendition.Crf.ToString(),
                "-profile:v",
                "main",
                "-pix_fmt",
                "yuv420p"
            ]);
        } else {
            if (transcoderProfile == HlsTranscoderProfile.VideoToolbox) {
                arguments.AddRange(["-allow_sw", "1"]);
            }

            arguments.AddRange(
            [
                "-profile:v",
                "main"
            ]);

            if (transcoderProfile != HlsTranscoderProfile.Vaapi) {
                arguments.AddRange(["-pix_fmt", transcoderProfile == HlsTranscoderProfile.Qsv ? "nv12" : "yuv420p"]);
            }
        }

        arguments.AddRange(
        [
            "-b:v",
            rendition.VideoBitrate,
            "-maxrate",
            rendition.MaxRate,
            "-bufsize",
            rendition.BufferSize
        ]);

        return arguments;
    }

    private static int SegmentCount(double durationSeconds) =>
        !double.IsFinite(durationSeconds) || durationSeconds <= 0
            ? 0
            : (int)Math.Ceiling(durationSeconds / SegmentDurationSeconds);

    private static double SegmentDuration(double durationSeconds, int index) {
        var total = SegmentCount(durationSeconds);
        if (index < 0 || index >= total) return 0;
        if (index < total - 1) return SegmentDurationSeconds;
        var duration = durationSeconds - (total - 1) * SegmentDurationSeconds;
        return duration > 0 ? duration : SegmentDurationSeconds;
    }

    private static int ToBitsPerSecond(string rate) {
        var value = rate.Trim();
        var unit = value[^1];
        if (unit is 'k' or 'K' or 'm' or 'M') {
            var number = int.TryParse(value[..^1], out var parsed) ? parsed : 0;
            return unit is 'm' or 'M' ? number * 1_000_000 : number * 1_000;
        }

        return int.TryParse(value, out var raw) ? raw : 0;
    }

    private static IReadOnlyList<JellyfinQualityOption> JellyfinQualityOptions(
        int sourceVideoBitrate,
        string? videoCodec) {
        var options = JellyfinQualityPresetOptions();
        if (sourceVideoBitrate <= 0) {
            return options;
        }

        var comparableBitrate = sourceVideoBitrate;
        if (IsEfficientVideoCodec(videoCodec) && comparableBitrate <= 20_000_000) {
            comparableBitrate = (int)Math.Round(comparableBitrate * 1.5);
        }

        var selected = new List<JellyfinQualityOption>();
        var nextHigher = options
            .Where(option => option.Bitrate > comparableBitrate)
            .LastOrDefault();
        if (nextHigher is not null) {
            selected.Add(nextHigher);
        }

        selected.AddRange(options.Where(option => option.Bitrate <= comparableBitrate));
        return selected.Count > 0 ? selected : [options[^1]];
    }

    private static IReadOnlyList<JellyfinQualityOption> JellyfinQualityPresetOptions() =>
    [
        new("120mbps", 2160, 120_000_000),
        new("80mbps", 2160, 80_000_000),
        new("60mbps", 2160, 60_000_000),
        new("40mbps", 2160, 40_000_000),
        new("20mbps", 2160, 20_000_000),
        new("15mbps", 1440, 15_000_000),
        new("10mbps", 1440, 10_000_000),
        new("8mbps", 1080, 8_000_000),
        new("6mbps", 1080, 6_000_000),
        new("4mbps", 720, 4_000_000),
        new("3mbps", 720, 3_000_000),
        new("1500kbps", 720, 1_500_000),
        new("720kbps", 480, 720_000),
        new("420kbps", 360, 420_000)
    ];

    private static VirtualHlsRendition RenditionForQualityOption(
        JellyfinQualityOption option,
        int sourceHeight) {
        var height = Math.Min(sourceHeight, option.MaxHeight);
        var videoBitrate = ToRate(option.Bitrate);
        var maxRate = ToRate((int)Math.Round(option.Bitrate * 1.15));
        var bufferSize = ToRate(option.Bitrate * 2);
        return new(
            option.Name,
            height,
            videoBitrate,
            maxRate,
            bufferSize,
            option.Bitrate >= 15_000_000 ? "192k" : "128k",
            CrfForHeight(height));
    }

    private static int SourceVideoBitrate(VideoSourceFile source) =>
        source.Streams?
            .Where(stream => stream.Type.Equals("Video", StringComparison.OrdinalIgnoreCase))
            .OrderBy(stream => stream.StreamIndex)
            .Select(stream => stream.BitRate)
            .FirstOrDefault(bitRate => bitRate is > 0) ??
        source.BitRate ??
        0;

    private static bool IsEfficientVideoCodec(string? codec) =>
        codec is not null &&
        (codec.Equals("hevc", StringComparison.OrdinalIgnoreCase) ||
            codec.Equals("h265", StringComparison.OrdinalIgnoreCase) ||
            codec.Equals("av1", StringComparison.OrdinalIgnoreCase) ||
            codec.Equals("vp9", StringComparison.OrdinalIgnoreCase));

    private static HlsTranscoderProfile ResolveEffectiveTranscoderProfile(
        VideoSourceFile source,
        HlsTranscoderProfile requestedProfile) =>
        NeedsToneMapping(source) ? HlsTranscoderProfile.Software : requestedProfile;

    private static bool NeedsToneMapping(VideoSourceFile source) {
        var range = VideoPlaybackRangePolicy.Classify(PrimaryVideoStream(source));
        return !range.VideoRangeType.Equals("SDR", StringComparison.OrdinalIgnoreCase);
    }

    private static bool RequiresDolbyVisionToneMapping(VideoSourceFile source) {
        var stream = PrimaryVideoStream(source);
        return stream?.DvProfile is 5 ||
            stream?.DvBlSignalCompatibilityId is 0 ||
            VideoPlaybackRangePolicy.Classify(stream).VideoRangeType.Equals("DOVI", StringComparison.OrdinalIgnoreCase);
    }

    private static VideoSourceStream? PrimaryVideoStream(VideoSourceFile source) =>
        source.Streams?
            .Where(stream => stream.Type.Equals("Video", StringComparison.OrdinalIgnoreCase))
            .OrderBy(stream => stream.StreamIndex)
            .FirstOrDefault();

    private static string ToRate(int bitsPerSecond) =>
        bitsPerSecond % 1_000_000 == 0
            ? $"{bitsPerSecond / 1_000_000}M"
            : $"{Math.Max(1, bitsPerSecond / 1_000)}k";

    private static int CrfForHeight(int height) =>
        height switch {
            <= 480 => 22,
            <= 720 => 21,
            <= 1080 => 20,
            <= 1440 => 19,
            _ => 18
        };

    private static int NormalizeRenditionHeight(int height) =>
        Math.Max(2, height % 2 == 0 ? height : height - 1);

    private static string H264CodecForHeight(int height) =>
        height switch {
            <= 480 => "avc1.4d401e",
            <= 720 => "avc1.4d401f",
            <= 1080 => "avc1.4d4029",
            <= 1440 => "avc1.4d4032",
            _ => "avc1.4d4033"
        };

    private static int? ScaledWidth(int? sourceWidth, int? sourceHeight, int targetHeight) {
        if (sourceWidth is not > 0 || sourceHeight is not > 0 || targetHeight <= 0) return null;
        var width = (int)Math.Round((double)sourceWidth.Value / sourceHeight.Value * targetHeight);
        return width % 2 == 0 ? width : width - 1;
    }

    private static int? ParseSegmentIndex(string fileName) {
        if (!fileName.StartsWith("seg_", StringComparison.OrdinalIgnoreCase) ||
            !fileName.EndsWith(".ts", StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        var value = fileName["seg_".Length..^".ts".Length];
        return int.TryParse(value, out var index) ? index : null;
    }

    private static bool IsSameVirtualCache(
        VirtualCacheMetadata? left,
        VirtualCacheMetadata right) =>
        left is not null &&
        left.SourcePath == right.SourcePath &&
        left.SourceSize == right.SourceSize &&
        left.SourceModifiedUtc == right.SourceModifiedUtc &&
        Math.Abs(left.DurationSeconds - right.DurationSeconds) < 0.001 &&
        left.Renditions.SequenceEqual(right.Renditions) &&
        left.TranscoderProfile == right.TranscoderProfile &&
        left.FormatVersion == right.FormatVersion;

    private static HlsTranscoderProfile ResolveTranscoderProfile(HlsAssetServiceOptions options) {
        if (options.TranscoderProfile != HlsTranscoderProfile.Auto) {
            return options.TranscoderProfile;
        }

        if (OperatingSystem.IsMacOS()) {
            return HlsTranscoderProfile.VideoToolbox;
        }

        if (OperatingSystem.IsLinux() && File.Exists(options.VaapiDevice)) {
            return HlsTranscoderProfile.Vaapi;
        }

        return HlsTranscoderProfile.Software;
    }

    private async Task<HlsAssetServiceOptions> ResolveTranscoderOptionsAsync(CancellationToken cancellationToken) {
        if (_db is null) {
            return _options;
        }

        var settings = await new SettingsService(new EfSettingsPersistence(_db))
            .GetHlsSettingsAsync(cancellationToken);

        return new HlsAssetServiceOptions(
            _options.CacheRoot,
            HlsTranscoderProfiles.ParseOrDefault(settings.TranscoderProfile, _options.TranscoderProfile),
            ResolveConfiguredFfmpegPath(settings.FfmpegPath, _options.FfmpegPath),
            string.IsNullOrWhiteSpace(settings.VaapiDevice) ? _options.VaapiDevice : settings.VaapiDevice.Trim());
    }

    private static string ResolveConfiguredFfmpegPath(string? savedPath, string defaultPath) =>
        string.IsNullOrWhiteSpace(savedPath) ||
        string.Equals(savedPath.Trim(), "ffmpeg", StringComparison.OrdinalIgnoreCase)
            ? defaultPath
            : savedPath.Trim();

    private static void ResetStagingDirectory(string stagingDirectory) {
        if (Directory.Exists(stagingDirectory)) {
            Directory.Delete(stagingDirectory, recursive: true);
        }

        Directory.CreateDirectory(stagingDirectory);
    }

    private static string MimeForExtension(string extension) {
        return extension.ToLowerInvariant() switch {
            ".m3u8" => "application/vnd.apple.mpegurl",
            ".ts" => "video/mp2t",
            ".mp4" or ".m4s" => "video/mp4",
            ".vtt" => "text/vtt",
            _ => "application/octet-stream"
        };
    }

    private static string CacheControlForExtension(string extension) {
        return extension.Equals(".m3u8", StringComparison.OrdinalIgnoreCase)
            ? "public, max-age=60"
            : "public, max-age=31536000, immutable";
    }

    private sealed record VirtualHlsRendition(
        string Name,
        int Height,
        string VideoBitrate,
        string MaxRate,
        string BufferSize,
        string AudioBitrate,
        int Crf);

    private sealed record JellyfinQualityOption(string Name, int MaxHeight, int Bitrate);

    private sealed record VirtualCacheMetadata(
        string SourcePath,
        long SourceSize,
        DateTime SourceModifiedUtc,
        double DurationSeconds,
        IReadOnlyList<string> Renditions,
        string TranscoderProfile = nameof(HlsTranscoderProfile.Software),
        int FormatVersion = 0);

    private sealed record VirtualRenditionGeneration(
        int StartSegment,
        int EndSegment,
        string StagingDirectory,
        CancellationTokenSource Cancellation,
        Task Task);

    private sealed record VirtualTrickplayStream(int Width, int Height, int Bandwidth);
}
