using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Settings;
using Prismedia.Application.Videos;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;
using Prismedia.Infrastructure.Processes;
using Prismedia.Infrastructure.Videos;

namespace Prismedia.Infrastructure.Tests;

public sealed class HlsAssetServiceTests : IDisposable {
    private const string SegmentLengthText = "6";
    private readonly string _cacheRoot = Path.Combine(Path.GetTempPath(), $"prismedia-hls-assets-{Guid.NewGuid():N}");

    public HlsAssetServiceTests() {
        Directory.CreateDirectory(_cacheRoot);
    }

    [Fact]
    public async Task ResolvesHls2ManifestAndSegmentAssets() {
        var videoId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var packageDir = Path.Combine(_cacheRoot, "hls2", videoId.ToString(), "v", "720p");
        Directory.CreateDirectory(packageDir);
        var manifestPath = Path.Combine(_cacheRoot, "hls2", videoId.ToString(), "master.m3u8");
        var segmentPath = Path.Combine(packageDir, "seg_00000.ts");
        await File.WriteAllTextAsync(manifestPath, "#EXTM3U");
        await File.WriteAllTextAsync(segmentPath, "segment");

        var service = new HlsAssetService(new HlsAssetServiceOptions(_cacheRoot));
        var manifest = await service.GetAssetAsync(videoId, "master.m3u8", null, CancellationToken.None);
        var segment = await service.GetAssetAsync(videoId, "v/720p/seg_00000.ts", null, CancellationToken.None);

        Assert.NotNull(manifest);
        Assert.Equal("application/vnd.apple.mpegurl", manifest.ContentType);
        Assert.Equal("public, max-age=60", manifest.CacheControl);
        Assert.NotNull(segment);
        Assert.Equal("video/mp2t", segment.ContentType);
        Assert.Equal("public, max-age=31536000, immutable", segment.CacheControl);
    }

    [Fact]
    public async Task RejectsTraversalOutsidePackageRoot() {
        var videoId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var service = new HlsAssetService(new HlsAssetServiceOptions(_cacheRoot));

        var asset = await service.GetAssetAsync(videoId, "../secret.ts", null, CancellationToken.None);

        Assert.Null(asset);
    }

    [Fact]
    public void RemuxSegmentDurationsMatchFfmpegStreamCopyBoundaries() {
        // Models a 1080p source with a 2.002s GOP (keyframe every 2.002s) over ~1951.296s, which is
        // exactly what ffmpeg's stream-copy HLS muxer cuts into 325 segments: 324 of 6.006s (three
        // GOPs each, the first keyframe at/after every 6s mark) plus a short final segment.
        const double gop = 2.002;
        const double totalDuration = 1951.296;
        var keyframes = new List<double>();
        for (var t = 0.0; t < totalDuration; t += gop) {
            keyframes.Add(Math.Round(t, 6));
        }

        var durations = HlsAssetService.BuildRemuxSegmentDurations(keyframes, totalDuration);

        Assert.Equal(325, durations.Count);
        Assert.All(durations.Take(durations.Count - 1), duration => Assert.Equal(6.006, duration, 3));
        Assert.Equal(totalDuration, durations.Sum(), 3);
        // The final segment is shorter and never starts a new boundary for the trailing sub-6s keyframes.
        Assert.True(durations[^1] < 6.006);
    }

    [Fact]
    public void RemuxVodPlaylistIsCompleteAndSeekable() {
        var durations = new List<double> { 6.006, 6.006, 4.2 };

        var playlist = HlsAssetService.BuildRemuxVodPlaylist(durations);

        Assert.StartsWith("#EXTM3U", playlist);
        Assert.Contains("#EXT-X-PLAYLIST-TYPE:VOD", playlist);
        Assert.Contains("#EXT-X-MAP:URI=\"init.mp4\"", playlist);
        Assert.Contains("#EXT-X-TARGETDURATION:6", playlist);
        Assert.Contains("#EXTINF:6.006000,\nseg_00000.m4s", playlist);
        Assert.Contains("#EXTINF:4.200000,\nseg_00002.m4s", playlist);
        Assert.EndsWith("#EXT-X-ENDLIST\n", playlist);
        // A growing EVENT playlist (the old behavior) would limit the seekable range; assert we never emit it.
        Assert.DoesNotContain("EVENT", playlist);
    }

    [Fact]
    public async Task VirtualManifestIsVodAndCoversFullDuration() {
        var videoId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var sourcePath = Path.Combine(_cacheRoot, "source.mkv");
        await File.WriteAllTextAsync(sourcePath, "source");
        var process = new ManifestWritingProcessExecutor();
        var service = new HlsAssetService(
            new HlsAssetServiceOptions(_cacheRoot),
            new FakeVideoSourceService(new VideoSourceFile(
                videoId,
                sourcePath,
                "video/x-matroska",
                false,
                DurationSeconds: 13,
                Width: 3840,
                Height: 1920,
                BitRate: 22_000_000,
                VideoCodec: "hevc")),
            process,
            NullLogger<HlsAssetService>.Instance);

        var master = await service.GetAssetAsync(videoId, "master.m3u8", null, CancellationToken.None);
        var variant = await service.GetAssetAsync(videoId, "v/720p/stream.m3u8", null, CancellationToken.None);

        Assert.NotNull(master);
        var masterPlaylist = await File.ReadAllTextAsync(master.Path);
        Assert.Contains("hls/40mbps/stream.m3u8", masterPlaylist);
        Assert.Contains("hls/20mbps/stream.m3u8", masterPlaylist);
        Assert.Contains("hls/15mbps/stream.m3u8", masterPlaylist);
        Assert.Contains("hls/8mbps/stream.m3u8", masterPlaylist);
        Assert.Contains("hls/720kbps/stream.m3u8", masterPlaylist);
        Assert.Contains("RESOLUTION=3840x1920", masterPlaylist);
        Assert.Contains("RESOLUTION=2880x1440", masterPlaylist);
        Assert.Contains("RESOLUTION=960x480", masterPlaylist);
        Assert.NotNull(variant);
        var playlist = await File.ReadAllTextAsync(variant.Path);
        Assert.Contains("#EXT-X-PLAYLIST-TYPE:VOD", playlist);
        Assert.Contains("#EXT-X-TARGETDURATION:6", playlist);
        Assert.Contains("#EXTINF:1.000000,", playlist);
        Assert.Contains("#EXT-X-ENDLIST", playlist);
        Assert.False(process.WasCalled);
    }

    [Fact]
    public async Task VirtualManifestAdvertisesGeneratedTrickplayPlaylist() {
        await using var db = CreateContext();
        var videoId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        db.Entities.Add(new EntityRow {
            Id = videoId,
            KindCode = EntityKindRegistry.Video.Code,
            Title = "Video",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        db.TrickplayInfos.Add(new TrickplayInfoRow {
            EntityId = videoId,
            Width = 280,
            Height = 158,
            TileWidth = 4,
            TileHeight = 4,
            ThumbnailCount = 16,
            IntervalSeconds = 7,
            Bandwidth = 1234,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var sourcePath = Path.Combine(_cacheRoot, "source.mkv");
        await File.WriteAllTextAsync(sourcePath, "source");
        var service = new HlsAssetService(
            new HlsAssetServiceOptions(_cacheRoot),
            new FakeVideoSourceService(new VideoSourceFile(
                videoId,
                sourcePath,
                "video/x-matroska",
                false,
                DurationSeconds: 13,
                Width: 1920,
                Height: 960)),
            new ManifestWritingProcessExecutor(),
            NullLogger<HlsAssetService>.Instance,
            db);

        var master = await service.GetAssetAsync(videoId, "master.m3u8", null, CancellationToken.None);

        Assert.NotNull(master);
        var content = await File.ReadAllTextAsync(master.Path);
        Assert.Contains("#EXT-X-IMAGE-STREAM-INF:BANDWIDTH=1234,RESOLUTION=280x158,CODECS=\"jpeg\",URI=\"Trickplay/280/tiles.m3u8\"", content);
    }

    [Fact]
    public async Task ConcurrentVirtualCacheRefreshesDoNotRaceDirectoryDeletion() {
        var videoId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        var sourcePath = Path.Combine(_cacheRoot, "source.mkv");
        await File.WriteAllTextAsync(sourcePath, "source");
        var virtualRoot = Path.Combine(_cacheRoot, "hlsv", videoId.ToString());
        Directory.CreateDirectory(Path.Combine(virtualRoot, "v", "720p"));
        await File.WriteAllTextAsync(
            Path.Combine(virtualRoot, "metadata.json"),
            """
            {
              "SourcePath": "/stale/source.mkv",
              "SourceSize": 1,
              "SourceModifiedUtc": "2001-01-01T00:00:00Z",
              "DurationSeconds": 1,
              "Renditions": ["720p"]
            }
            """);
        await File.WriteAllTextAsync(Path.Combine(virtualRoot, "v", "720p", "seg_00000.ts"), "stale");
        var source = new VideoSourceFile(
            videoId,
            sourcePath,
            "video/x-matroska",
            false,
            DurationSeconds: 13,
            Width: 1920,
            Height: 960);
        var service = new HlsAssetService(
            new HlsAssetServiceOptions(_cacheRoot),
            new CoordinatedVideoSourceService(source, expectedCalls: 12),
            new ManifestWritingProcessExecutor(),
            NullLogger<HlsAssetService>.Instance);

        var requests = Enumerable.Range(0, 12)
            .Select(index => service.GetAssetAsync(
                videoId,
                index % 2 == 0 ? "master.m3u8" : "v/720p/stream.m3u8",
                null,
                CancellationToken.None))
            .ToArray();
        var assets = await Task.WhenAll(requests);

        Assert.All(assets, Assert.NotNull);
        Assert.True(File.Exists(Path.Combine(virtualRoot, "metadata.json")));
    }

    [Fact]
    public async Task VirtualCacheWithoutFormatVersionIsRefreshed() {
        var videoId = Guid.Parse("88888888-8888-8888-8888-888888888888");
        var sourcePath = Path.Combine(_cacheRoot, "source.mkv");
        await File.WriteAllTextAsync(sourcePath, "source");
        var sourceInfo = new FileInfo(sourcePath);
        var virtualRoot = Path.Combine(_cacheRoot, "hlsv", videoId.ToString());
        Directory.CreateDirectory(Path.Combine(virtualRoot, "v", "720p"));
        await File.WriteAllTextAsync(
            Path.Combine(virtualRoot, "metadata.json"),
            $$"""
            {
              "SourcePath": "{{sourcePath.Replace("\\", "\\\\")}}",
              "SourceSize": {{sourceInfo.Length}},
              "SourceModifiedUtc": "{{sourceInfo.LastWriteTimeUtc:O}}",
              "DurationSeconds": 13,
              "Renditions": ["720p"]
            }
            """);
        await File.WriteAllTextAsync(Path.Combine(virtualRoot, "v", "720p", "seg_00000.ts"), "old");
        var service = new HlsAssetService(
            new HlsAssetServiceOptions(_cacheRoot),
            new FakeVideoSourceService(new VideoSourceFile(
                videoId,
                sourcePath,
                "video/x-matroska",
                false,
                DurationSeconds: 13,
                Width: 1920,
                Height: 960)),
            new ManifestWritingProcessExecutor(),
            NullLogger<HlsAssetService>.Instance);

        var asset = await service.GetAssetAsync(videoId, "master.m3u8", null, CancellationToken.None);

        Assert.NotNull(asset);
        var metadata = await File.ReadAllTextAsync(Path.Combine(virtualRoot, "metadata.json"));
        Assert.Contains("\"FormatVersion\": 9", metadata);
        Assert.False(File.Exists(Path.Combine(virtualRoot, "v", "720p", "seg_00000.ts")));
    }

    [Fact]
    public async Task VirtualSegmentStartsContinuousRenditionGeneration() {
        var videoId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var sourcePath = Path.Combine(_cacheRoot, "source.mkv");
        await File.WriteAllTextAsync(sourcePath, "source");
        var process = new ManifestWritingProcessExecutor();
        var service = new HlsAssetService(
            // Pin the software encoder so the libx264 assertion is host-independent; on macOS the
            // Auto profile resolves to VideoToolbox, which is covered by its own test.
            new HlsAssetServiceOptions(_cacheRoot, HlsTranscoderProfile.Software),
            new FakeVideoSourceService(new VideoSourceFile(
                videoId,
                sourcePath,
                "video/x-matroska",
                false,
                DurationSeconds: 13,
                Width: 1920,
                Height: 960)),
            process,
            NullLogger<HlsAssetService>.Instance);

        var segment = await service.GetAssetAsync(videoId, "v/720p/seg_00000.ts", null, CancellationToken.None);

        Assert.NotNull(segment);
        Assert.Equal("video/mp2t", segment.ContentType);
        Assert.True(process.WasCalled);
        Assert.Contains(process.ArgumentHistory, arguments =>
            arguments.Contains("-hls_segment_filename"));
        Assert.Contains(process.ArgumentHistory, arguments =>
            arguments.Contains("libx264"));
    }

    [Fact]
    public async Task VirtualSegmentsUseVideoToolboxEncoderWhenConfigured() {
        var videoId = Guid.Parse("42424242-4242-4242-4242-424242424242");
        var sourcePath = Path.Combine(_cacheRoot, "source.mkv");
        await File.WriteAllTextAsync(sourcePath, "source");
        var process = new ManifestWritingProcessExecutor();
        var service = new HlsAssetService(
            new HlsAssetServiceOptions(_cacheRoot, HlsTranscoderProfile.VideoToolbox),
            new FakeVideoSourceService(new VideoSourceFile(
                videoId,
                sourcePath,
                "video/x-matroska",
                false,
                DurationSeconds: 13,
                Width: 1920,
                Height: 960)),
            process,
            NullLogger<HlsAssetService>.Instance);

        var segment = await service.GetAssetAsync(videoId, "v/720p/seg_00000.ts", null, CancellationToken.None);

        Assert.NotNull(segment);
        var arguments = Assert.Single(process.ArgumentHistory);
        Assert.Contains("h264_videotoolbox", arguments);
        Assert.Contains("-allow_sw", arguments);
        Assert.DoesNotContain("libx264", arguments);
    }

    [Fact]
    public async Task VirtualSegmentsUseVaapiEncoderWhenConfigured() {
        var videoId = Guid.Parse("43434343-4343-4343-4343-434343434343");
        var sourcePath = Path.Combine(_cacheRoot, "source.mkv");
        await File.WriteAllTextAsync(sourcePath, "source");
        var process = new ManifestWritingProcessExecutor();
        var service = new HlsAssetService(
            new HlsAssetServiceOptions(_cacheRoot, HlsTranscoderProfile.Vaapi, VaapiDevice: "/dev/dri/renderD129"),
            new FakeVideoSourceService(new VideoSourceFile(
                videoId,
                sourcePath,
                "video/x-matroska",
                false,
                DurationSeconds: 13,
                Width: 1920,
                Height: 960)),
            process,
            NullLogger<HlsAssetService>.Instance);

        var segment = await service.GetAssetAsync(videoId, "v/720p/seg_00000.ts", null, CancellationToken.None);

        Assert.NotNull(segment);
        var arguments = Assert.Single(process.ArgumentHistory);
        Assert.Contains("-vaapi_device", arguments);
        Assert.Contains("/dev/dri/renderD129", arguments);
        Assert.Contains(arguments, argument =>
            argument.Contains("scale_vaapi=w=1440:h=720:format=nv12", StringComparison.Ordinal));
        Assert.Contains("h264_vaapi", arguments);
        Assert.DoesNotContain("libx264", arguments);
    }

    [Fact]
    public async Task VirtualSegmentsUseSavedHlsTranscoderSettings() {
        await using var db = CreateContext();
        var videoId = Guid.Parse("45454545-4545-4545-4545-454545454545");
        var sourcePath = Path.Combine(_cacheRoot, "source.mkv");
        await File.WriteAllTextAsync(sourcePath, "source");
        db.AppSettings.AddRange(
            SettingRow(AppSettingKeys.HlsTranscoderProfile, "Vaapi"),
            SettingRow(AppSettingKeys.HlsFfmpegPath, "/usr/local/bin/ffmpeg-gpu"),
            SettingRow(AppSettingKeys.HlsVaapiDevice, "/dev/dri/renderD129"));
        await db.SaveChangesAsync();
        var process = new ManifestWritingProcessExecutor();
        var service = new HlsAssetService(
            new HlsAssetServiceOptions(_cacheRoot),
            new FakeVideoSourceService(new VideoSourceFile(
                videoId,
                sourcePath,
                "video/x-matroska",
                false,
                DurationSeconds: 13,
                Width: 1920,
                Height: 960)),
            process,
            NullLogger<HlsAssetService>.Instance,
            db);

        var segment = await service.GetAssetAsync(videoId, "v/720p/seg_00000.ts", null, CancellationToken.None);

        Assert.NotNull(segment);
        Assert.Equal("/usr/local/bin/ffmpeg-gpu", Assert.Single(process.FileNameHistory));
        var arguments = Assert.Single(process.ArgumentHistory);
        Assert.Contains("/dev/dri/renderD129", arguments);
        Assert.Contains("h264_vaapi", arguments);
    }

    [Fact]
    public async Task VirtualSegmentsAreGeneratedByOneContinuousHlsMuxer() {
        var videoId = Guid.Parse("77777777-7777-7777-7777-777777777777");
        var sourcePath = Path.Combine(_cacheRoot, "source.mkv");
        await File.WriteAllTextAsync(sourcePath, "source");
        var process = new ManifestWritingProcessExecutor();
        var service = new HlsAssetService(
            new HlsAssetServiceOptions(_cacheRoot),
            new FakeVideoSourceService(new VideoSourceFile(
                videoId,
                sourcePath,
                "video/x-matroska",
                false,
                DurationSeconds: 13,
                Width: 1920,
                Height: 960)),
            process,
            NullLogger<HlsAssetService>.Instance);

        var segment = await service.GetAssetAsync(videoId, "v/720p/seg_00001.ts", null, CancellationToken.None);

        Assert.NotNull(segment);
        var arguments = Assert.Single(process.ArgumentHistory);
        Assert.Contains("-f", arguments);
        Assert.Contains("hls", arguments);
        Assert.Contains("-hls_time", arguments);
        Assert.Contains(SegmentLengthText, arguments);
        Assert.Contains("-hls_segment_filename", arguments);
        Assert.Contains("-hls_flags", arguments);
        Assert.Contains("temp_file", arguments);
        Assert.Contains("-start_number", arguments);
        var startNumberIndex = arguments.ToList().IndexOf("-start_number");
        Assert.True(startNumberIndex >= 0);
        Assert.Equal("0", arguments[startNumberIndex + 1]);
        Assert.Contains("-copyts", arguments);
        Assert.Contains("-avoid_negative_ts", arguments);
        Assert.Contains("disabled", arguments);
        Assert.DoesNotContain("-output_ts_offset", arguments);
    }

    [Fact]
    public async Task FarVirtualSegmentStartsGenerationWithOneSegmentPreroll() {
        var videoId = Guid.Parse("99999999-9999-9999-9999-999999999999");
        var sourcePath = Path.Combine(_cacheRoot, "source.mkv");
        await File.WriteAllTextAsync(sourcePath, "source");
        var process = new ManifestWritingProcessExecutor();
        var service = new HlsAssetService(
            new HlsAssetServiceOptions(_cacheRoot),
            new FakeVideoSourceService(new VideoSourceFile(
                videoId,
                sourcePath,
                "video/x-matroska",
                false,
                DurationSeconds: 180,
                Width: 1920,
                Height: 960)),
            process,
            NullLogger<HlsAssetService>.Instance);

        var segment = await service.GetAssetAsync(videoId, "v/720p/seg_00020.ts", null, CancellationToken.None);

        Assert.NotNull(segment);
        var arguments = Assert.Single(process.ArgumentHistory);
        Assert.Contains("-ss", arguments);
        Assert.Contains("114.000", arguments);
        Assert.Contains("-start_number", arguments);
        var startNumberIndex = arguments.ToList().IndexOf("-start_number");
        Assert.True(startNumberIndex >= 0);
        Assert.Equal("19", arguments[startNumberIndex + 1]);
        Assert.DoesNotContain("-t", arguments);
    }

    [Fact]
    public async Task FarVirtualSegmentPrerollsSoRequestedSegmentIsGenerated() {
        var videoId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var sourcePath = Path.Combine(_cacheRoot, "source.mkv");
        await File.WriteAllTextAsync(sourcePath, "source");
        var process = new WritesOnlyNextSegmentProcessExecutor();
        var service = new HlsAssetService(
            new HlsAssetServiceOptions(_cacheRoot),
            new FakeVideoSourceService(new VideoSourceFile(
                videoId,
                sourcePath,
                "video/x-matroska",
                false,
                DurationSeconds: 180,
                Width: 1920,
                Height: 960)),
            process,
            NullLogger<HlsAssetService>.Instance);

        var segment = await service.GetAssetAsync(videoId, "v/720p/seg_00020.ts", null, CancellationToken.None);

        Assert.NotNull(segment);
        Assert.Equal("seg_00020.ts", Path.GetFileName(segment.Path));
        var arguments = Assert.Single(process.ArgumentHistory);
        Assert.Contains("-ss", arguments);
        Assert.Contains("114.000", arguments);
        Assert.Contains("-start_number", arguments);
        var startNumberIndex = arguments.ToList().IndexOf("-start_number");
        Assert.True(startNumberIndex >= 0);
        Assert.Equal("19", arguments[startNumberIndex + 1]);
    }

    [Fact]
    public async Task FarVirtualSegmentStartsSeparateGenerationWhenInitialGenerationIsStillRunning() {
        var videoId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
        var sourcePath = Path.Combine(_cacheRoot, "source.mkv");
        await File.WriteAllTextAsync(sourcePath, "source");
        var process = new BlockingInitialSegmentProcessExecutor();
        var service = new HlsAssetService(
            new HlsAssetServiceOptions(_cacheRoot),
            new FakeVideoSourceService(new VideoSourceFile(
                videoId,
                sourcePath,
                "video/x-matroska",
                false,
                DurationSeconds: 600,
                Width: 1920,
                Height: 960)),
            process,
            NullLogger<HlsAssetService>.Instance);

        var initialSegment = service.GetAssetAsync(videoId, "v/720p/seg_00000.ts", null, CancellationToken.None);
        await process.InitialGenerationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // A segment far beyond the running generation's reuse window is treated as a forward seek:
        // it starts a fresh generation at the seek point and the original generation is replaced.
        var farSegment = await service.GetAssetAsync(videoId, "v/720p/seg_00070.ts", null, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));

        process.ReleaseInitialGeneration();
        var replacedInitialSegment = await initialSegment.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Null(replacedInitialSegment);
        Assert.NotNull(farSegment);
        Assert.Equal("seg_00070.ts", Path.GetFileName(farSegment.Path));
        Assert.Equal(2, process.ArgumentHistory.Count);
        Assert.Contains(process.ArgumentHistory, arguments => {
            var startNumberIndex = arguments.ToList().IndexOf("-start_number");
            return startNumberIndex >= 0 && arguments[startNumberIndex + 1] == "0";
        });
        Assert.Contains(process.ArgumentHistory, arguments => {
            var startNumberIndex = arguments.ToList().IndexOf("-start_number");
            return startNumberIndex >= 0 && arguments[startNumberIndex + 1] == "69";
        });
    }

    [Fact]
    public async Task FarVirtualSegmentDoesNotCancelOtherActiveRenditionsForSameAudioTrack() {
        var videoId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
        var sourcePath = Path.Combine(_cacheRoot, "source.mkv");
        await File.WriteAllTextAsync(sourcePath, "source");
        var process = new BlockingInitialSegmentProcessExecutor();
        var service = new HlsAssetService(
            new HlsAssetServiceOptions(_cacheRoot),
            new FakeVideoSourceService(new VideoSourceFile(
                videoId,
                sourcePath,
                "video/x-matroska",
                false,
                DurationSeconds: 180,
                Width: 3840,
                Height: 1920)),
            process,
            NullLogger<HlsAssetService>.Instance);

        var initialSegment = service.GetAssetAsync(videoId, "v/8mbps/seg_00000.ts", null, CancellationToken.None);
        await process.InitialGenerationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var farSegment = await service.GetAssetAsync(videoId, "v/720kbps/seg_00020.ts", null, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));
        process.ReleaseInitialGeneration();
        var completedInitialSegment = await initialSegment.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.NotNull(completedInitialSegment);
        Assert.Equal("seg_00000.ts", Path.GetFileName(completedInitialSegment.Path));
        Assert.NotNull(farSegment);
        Assert.Equal("seg_00020.ts", Path.GetFileName(farSegment.Path));
        Assert.Contains(process.ArgumentHistory, arguments =>
            arguments.Any(argument => argument.Contains("/8mbps/", StringComparison.Ordinal)) &&
            arguments.Contains("0"));
        Assert.Contains(process.ArgumentHistory, arguments =>
            arguments.Any(argument => argument.Contains("/720kbps/", StringComparison.Ordinal)) &&
            arguments.Contains("19"));
    }

    [Fact]
    public async Task CancellingPlaybackSessionCancelsActiveVirtualGenerationForItem() {
        var videoId = Guid.Parse("fefefefe-fefe-fefe-fefe-fefefefefefe");
        var sourcePath = Path.Combine(_cacheRoot, "source.mkv");
        await File.WriteAllTextAsync(sourcePath, "source");
        var process = new BlockingInitialSegmentProcessExecutor();
        var service = new HlsAssetService(
            new HlsAssetServiceOptions(_cacheRoot),
            new FakeVideoSourceService(new VideoSourceFile(
                videoId,
                sourcePath,
                "video/x-matroska",
                false,
                DurationSeconds: 180,
                Width: 1920,
                Height: 960)),
            process,
            NullLogger<HlsAssetService>.Instance);
        var sessions = new TranscodeSessionService();
        sessions.Register("session-1", videoId);

        var initialSegment = service.GetAssetAsync(videoId, "v/720p/seg_00000.ts", null, CancellationToken.None);
        await process.InitialGenerationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await sessions.CancelAsync("session-1", CancellationToken.None);
        process.ReleaseInitialGeneration();
        var cancelledSegment = await initialSegment.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Null(cancelledSegment);
    }

    [Fact]
    public async Task VirtualSegmentsUseDefaultSourceAudioStream() {
        var videoId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var sourcePath = Path.Combine(_cacheRoot, "source.mkv");
        await File.WriteAllTextAsync(sourcePath, "source");
        var process = new ManifestWritingProcessExecutor();
        var service = new HlsAssetService(
            new HlsAssetServiceOptions(_cacheRoot),
            new FakeVideoSourceService(new VideoSourceFile(
                videoId,
                sourcePath,
                "video/x-matroska",
                false,
                DurationSeconds: 180,
                Width: 1920,
                Height: 960,
                Streams:
                [
                    new(0, "Video", "h264", null, "Video", 1920, 960, 24, null, null, null, true, false),
                    new(1, "Audio", "aac", "spa", "Spanish", null, null, null, null, 48000, 2, false, false),
                    new(2, "Audio", "aac", "eng", "English", null, null, null, null, 48000, 2, true, false)
                ])),
            process,
            NullLogger<HlsAssetService>.Instance);

        var segment = await service.GetAssetAsync(videoId, "v/720p/seg_00000.ts", null, CancellationToken.None);

        Assert.NotNull(segment);
        var arguments = Assert.Single(process.ArgumentHistory);
        Assert.Contains("-map", arguments);
        Assert.Contains("0:2?", arguments);
    }

    [Fact]
    public async Task VirtualSegmentsToneMapHdrSourcesToBt709SoftwareOutput() {
        var videoId = Guid.Parse("abababab-abab-abab-abab-abababababab");
        var sourcePath = Path.Combine(_cacheRoot, "hdr-source.mkv");
        await File.WriteAllTextAsync(sourcePath, "source");
        var process = new ManifestWritingProcessExecutor();
        var service = new HlsAssetService(
            new HlsAssetServiceOptions(_cacheRoot, HlsTranscoderProfile.Vaapi),
            new FakeVideoSourceService(new VideoSourceFile(
                videoId,
                sourcePath,
                "video/x-matroska",
                false,
                DurationSeconds: 180,
                Width: 3840,
                Height: 2160,
                Streams:
                [
                    new(0, "Video", "hevc", null, "Video", 3840, 2160, 24, null, null, null, true, false) {
                        PixelFormat = "yuv420p10le",
                        ColorTransfer = "smpte2084",
                        ColorPrimaries = "bt2020",
                        ColorSpace = "bt2020nc"
                    }
                ])),
            process,
            NullLogger<HlsAssetService>.Instance);

        var segment = await service.GetAssetAsync(videoId, "v/8mbps/seg_00000.ts", null, CancellationToken.None);

        Assert.NotNull(segment);
        var arguments = Assert.Single(process.ArgumentHistory);
        Assert.Contains("libx264", arguments);
        Assert.DoesNotContain("h264_vaapi", arguments);
        Assert.Contains(arguments, argument =>
            argument.Contains("zscale=t=linear", StringComparison.Ordinal) &&
            argument.Contains("tonemap=tonemap=hable", StringComparison.Ordinal) &&
            argument.Contains("zscale=t=bt709:m=bt709:p=bt709", StringComparison.Ordinal) &&
            argument.Contains("format=yuv420p", StringComparison.Ordinal));
    }

    [Fact]
    public async Task VirtualSegmentsUseTonemapxForDolbyVisionProfileFiveSources() {
        var videoId = Guid.Parse("bcbcbcbc-bcbc-bcbc-bcbc-bcbcbcbcbcbc");
        var sourcePath = Path.Combine(_cacheRoot, "dovi-source.mkv");
        await File.WriteAllTextAsync(sourcePath, "source");
        var process = new ManifestWritingProcessExecutor();
        var service = new HlsAssetService(
            new HlsAssetServiceOptions(_cacheRoot, HlsTranscoderProfile.Software),
            new FakeVideoSourceService(new VideoSourceFile(
                videoId,
                sourcePath,
                "video/x-matroska",
                false,
                DurationSeconds: 180,
                Width: 3840,
                Height: 1920,
                Streams:
                [
                    new(0, "Video", "hevc", null, "Video", 3840, 1920, 24, null, null, null, true, false) {
                        PixelFormat = "yuv420p10le",
                        ColorRange = "pc",
                        DvProfile = 5,
                        DvLevel = 6,
                        RpuPresentFlag = true,
                        BlPresentFlag = true,
                        DvBlSignalCompatibilityId = 0
                    }
                ])),
            process,
            NullLogger<HlsAssetService>.Instance);

        var segment = await service.GetAssetAsync(videoId, "v/8mbps/seg_00000.ts", null, CancellationToken.None);

        Assert.NotNull(segment);
        var arguments = Assert.Single(process.ArgumentHistory);
        // Profile 5 must tone-map via tonemapx (which applies the Dolby Vision RPU) on the full-res
        // decoded frames BEFORE scaling — swscale would otherwise drop the RPU side data — and must
        // not force input colour tags. See FfmpegToneMapping for the rationale.
        Assert.Contains(arguments, argument =>
            argument.Contains("tonemapx=tonemap=bt2390", StringComparison.Ordinal) &&
            argument.Contains("peak=100", StringComparison.Ordinal) &&
            argument.Contains("r=tv:format=yuv420p", StringComparison.Ordinal) &&
            !argument.Contains("setparams", StringComparison.Ordinal) &&
            argument.IndexOf("tonemapx", StringComparison.Ordinal) <
                argument.IndexOf("scale=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task VirtualSegmentsUseHdrToneMappingForDolbyVisionProfileEightSources() {
        var videoId = Guid.Parse("c8c8c8c8-c8c8-c8c8-c8c8-c8c8c8c8c8c8");
        var sourcePath = Path.Combine(_cacheRoot, "dovi-profile-eight-source.mkv");
        await File.WriteAllTextAsync(sourcePath, "source");
        var process = new ManifestWritingProcessExecutor();
        var service = new HlsAssetService(
            new HlsAssetServiceOptions(_cacheRoot, HlsTranscoderProfile.Software),
            new FakeVideoSourceService(new VideoSourceFile(
                videoId,
                sourcePath,
                "video/x-matroska",
                false,
                DurationSeconds: 180,
                Width: 3840,
                Height: 1920,
                Streams:
                [
                    new(0, "Video", "hevc", null, "Video", 3840, 1920, 24, null, null, null, true, false) {
                        PixelFormat = "yuv420p10le",
                        ColorTransfer = "smpte2084",
                        ColorPrimaries = "bt2020",
                        ColorSpace = "bt2020nc",
                        DvProfile = 8,
                        DvLevel = 6,
                        RpuPresentFlag = true,
                        BlPresentFlag = true,
                        DvBlSignalCompatibilityId = 1
                    }
                ])),
            process,
            NullLogger<HlsAssetService>.Instance);

        var segment = await service.GetAssetAsync(videoId, "v/8mbps/seg_00000.ts", null, CancellationToken.None);

        Assert.NotNull(segment);
        var arguments = Assert.Single(process.ArgumentHistory);
        Assert.DoesNotContain(arguments, argument => argument.Contains("tonemapx", StringComparison.Ordinal));
        Assert.Contains(arguments, argument =>
            argument.Contains("zscale=t=linear", StringComparison.Ordinal) &&
            argument.Contains("tonemap=tonemap=hable", StringComparison.Ordinal) &&
            argument.Contains("format=yuv420p", StringComparison.Ordinal));
    }

    [Fact]
    public async Task VirtualSegmentsRetryToneMappedSourcesWithBasicSoftwareOutputWhenFilterIsMissing() {
        var videoId = Guid.Parse("d9d9d9d9-d9d9-d9d9-d9d9-d9d9d9d9d9d9");
        var sourcePath = Path.Combine(_cacheRoot, "dovi-profile-eight-missing-filter-source.mkv");
        await File.WriteAllTextAsync(sourcePath, "source");
        var process = new MissingToneMapFilterThenWritingProcessExecutor();
        var service = new HlsAssetService(
            new HlsAssetServiceOptions(_cacheRoot, HlsTranscoderProfile.Software),
            new FakeVideoSourceService(new VideoSourceFile(
                videoId,
                sourcePath,
                "video/x-matroska",
                false,
                DurationSeconds: 180,
                Width: 3840,
                Height: 1920,
                Streams:
                [
                    new(0, "Video", "hevc", null, "Video", 3840, 1920, 24, null, null, null, true, false) {
                        PixelFormat = "yuv420p10le",
                        ColorTransfer = "smpte2084",
                        ColorPrimaries = "bt2020",
                        ColorSpace = "bt2020nc",
                        DvProfile = 8,
                        DvLevel = 6,
                        RpuPresentFlag = true,
                        BlPresentFlag = true,
                        DvBlSignalCompatibilityId = 1
                    }
                ])),
            process,
            NullLogger<HlsAssetService>.Instance);

        var segment = await service.GetAssetAsync(videoId, "v/8mbps/seg_00000.ts", null, CancellationToken.None);

        Assert.NotNull(segment);
        Assert.Equal(2, process.ArgumentHistory.Count);
        Assert.Contains(process.ArgumentHistory[0], argument =>
            argument.Contains("zscale=t=linear", StringComparison.Ordinal) &&
            argument.Contains("tonemap=tonemap=hable", StringComparison.Ordinal));
        Assert.Contains(process.ArgumentHistory[1], argument =>
            argument.Contains("scale=w=-2:h=1080", StringComparison.Ordinal) &&
            argument.Contains("format=yuv420p", StringComparison.Ordinal));
        Assert.DoesNotContain(process.ArgumentHistory[1], argument => argument.Contains("tonemap", StringComparison.Ordinal));
        Assert.True(File.Exists(segment.Path));
    }

    [Fact]
    public async Task MissingVirtualSegmentReturnsNullAsset() {
        var videoId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var sourcePath = Path.Combine(_cacheRoot, "source.mkv");
        await File.WriteAllTextAsync(sourcePath, "source");
        var service = new HlsAssetService(
            new HlsAssetServiceOptions(_cacheRoot),
            new FakeVideoSourceService(new VideoSourceFile(
                videoId,
                sourcePath,
                "video/x-matroska",
                false,
                DurationSeconds: 180,
                Width: 1920,
                Height: 960)),
            new MissingSegmentProcessExecutor(),
            NullLogger<HlsAssetService>.Instance);

        var segment = await service.GetAssetAsync(videoId, "v/720p/seg_00020.ts", null, CancellationToken.None);

        Assert.Null(segment);
    }

    [Fact]
    public async Task FailedVirtualSegmentGenerationReturnsNullAsset() {
        var videoId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        var sourcePath = Path.Combine(_cacheRoot, "source.mkv");
        await File.WriteAllTextAsync(sourcePath, "source");
        var service = new HlsAssetService(
            new HlsAssetServiceOptions(_cacheRoot),
            new FakeVideoSourceService(new VideoSourceFile(
                videoId,
                sourcePath,
                "video/x-matroska",
                false,
                DurationSeconds: 180,
                Width: 1920,
                Height: 960)),
            new FailingProcessExecutor(),
            NullLogger<HlsAssetService>.Instance);

        var segment = await service.GetAssetAsync(videoId, "v/720p/seg_00000.ts", null, CancellationToken.None);

        Assert.Null(segment);
    }

    public void Dispose() {
        if (Directory.Exists(_cacheRoot)) {
            DeleteDirectoryWithRetry(_cacheRoot);
        }
    }

    private static void DeleteDirectoryWithRetry(string path) {
        for (var attempt = 0; attempt < 5; attempt++) {
            try {
                Directory.Delete(path, recursive: true);
                return;
            } catch (IOException) when (attempt < 4) {
                Thread.Sleep(50);
            }
        }
    }

    private static PrismediaDbContext CreateContext() {
        var options = new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase($"hls-assets-{Guid.NewGuid():N}")
            .Options;

        return new PrismediaDbContext(options);
    }

    private static AppSettingRow SettingRow(string key, string value) {
        var now = DateTimeOffset.UtcNow;
        return new AppSettingRow {
            Key = key,
            ValueJson = JsonSerializer.Serialize(value),
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    private sealed class FakeVideoSourceService : IVideoSourceService {
        private readonly VideoSourceFile _source;

        public FakeVideoSourceService(VideoSourceFile source) {
            _source = source;
        }

        public Task<VideoSourceFile?> GetSourceAsync(Guid id, CancellationToken cancellationToken) {
            return Task.FromResult(id == _source.EntityId ? _source : null);
        }
    }

    private sealed class CoordinatedVideoSourceService : IVideoSourceService {
        private readonly VideoSourceFile _source;
        private readonly int _expectedCalls;
        private readonly TaskCompletionSource _allArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;

        public CoordinatedVideoSourceService(VideoSourceFile source, int expectedCalls) {
            _source = source;
            _expectedCalls = expectedCalls;
        }

        public async Task<VideoSourceFile?> GetSourceAsync(Guid id, CancellationToken cancellationToken) {
            if (Interlocked.Increment(ref _calls) >= _expectedCalls) {
                _allArrived.TrySetResult();
            }

            await _allArrived.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            return id == _source.EntityId ? _source : null;
        }
    }

    private sealed class ManifestWritingProcessExecutor : ProcessExecutor {
        public bool WasCalled { get; private set; }
        public IReadOnlyList<string> LastArguments { get; private set; } = [];
        public List<string> FileNameHistory { get; } = [];
        public List<IReadOnlyList<string>> ArgumentHistory { get; } = [];

        public override async Task<ProcessExecutionResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            IReadOnlyDictionary<string, string>? environment,
            CancellationToken cancellationToken, bool lowPriority = false) {
            WasCalled = true;
            LastArguments = arguments;
            FileNameHistory.Add(fileName);
            ArgumentHistory.Add(arguments);
            var outputPath = arguments[^1];
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            var segmentPatternIndex = arguments.ToList().IndexOf("-hls_segment_filename");
            if (segmentPatternIndex >= 0 &&
                segmentPatternIndex < arguments.Count - 1) {
                var segmentPattern = arguments[segmentPatternIndex + 1];
                var startNumberIndex = arguments.ToList().IndexOf("-start_number");
                var startNumber = startNumberIndex >= 0 &&
                    startNumberIndex < arguments.Count - 1 &&
                    int.TryParse(arguments[startNumberIndex + 1], out var parsedStart)
                        ? parsedStart
                        : 0;
                for (var index = startNumber; index < startNumber + 5; index++) {
                    await File.WriteAllTextAsync(
                        segmentPattern.Replace("%05d", index.ToString("00000")),
                        "segment",
                        cancellationToken);
                }
            }

            await File.WriteAllTextAsync(outputPath, "segment", cancellationToken);
            return new ProcessExecutionResult(0, string.Empty, string.Empty);
        }
    }

    private sealed class MissingSegmentProcessExecutor : ProcessExecutor {
        public override async Task<ProcessExecutionResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            IReadOnlyDictionary<string, string>? environment,
            CancellationToken cancellationToken, bool lowPriority = false) {
            var outputPath = arguments[^1];
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            await File.WriteAllTextAsync(outputPath, "playlist", cancellationToken);
            return new ProcessExecutionResult(0, string.Empty, string.Empty);
        }
    }

    private sealed class FailingProcessExecutor : ProcessExecutor {
        public override Task<ProcessExecutionResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            IReadOnlyDictionary<string, string>? environment,
            CancellationToken cancellationToken, bool lowPriority = false) =>
            Task.FromResult(new ProcessExecutionResult(1, string.Empty, "No such filter: 'tonemapx'"));
    }

    private sealed class MissingToneMapFilterThenWritingProcessExecutor : ProcessExecutor {
        public List<IReadOnlyList<string>> ArgumentHistory { get; } = [];

        public override async Task<ProcessExecutionResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            IReadOnlyDictionary<string, string>? environment,
            CancellationToken cancellationToken, bool lowPriority = false) {
            ArgumentHistory.Add(arguments);
            if (arguments.Any(argument => argument.Contains("tonemap", StringComparison.Ordinal))) {
                return new ProcessExecutionResult(1, string.Empty, "No such filter: 'zscale'");
            }

            var outputPath = arguments[^1];
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            var segmentPatternIndex = arguments.ToList().IndexOf("-hls_segment_filename");
            if (segmentPatternIndex >= 0 && segmentPatternIndex < arguments.Count - 1) {
                var segmentPattern = arguments[segmentPatternIndex + 1];
                await File.WriteAllTextAsync(segmentPattern.Replace("%05d", "00000"), "segment", cancellationToken);
            }

            await File.WriteAllTextAsync(outputPath, "playlist", cancellationToken);
            return new ProcessExecutionResult(0, string.Empty, string.Empty);
        }
    }

    private sealed class WritesOnlyNextSegmentProcessExecutor : ProcessExecutor {
        public List<IReadOnlyList<string>> ArgumentHistory { get; } = [];

        public override async Task<ProcessExecutionResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            IReadOnlyDictionary<string, string>? environment,
            CancellationToken cancellationToken, bool lowPriority = false) {
            ArgumentHistory.Add(arguments);
            var outputPath = arguments[^1];
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            var segmentPatternIndex = arguments.ToList().IndexOf("-hls_segment_filename");
            var startNumberIndex = arguments.ToList().IndexOf("-start_number");
            if (segmentPatternIndex >= 0 &&
                startNumberIndex >= 0 &&
                segmentPatternIndex < arguments.Count - 1 &&
                startNumberIndex < arguments.Count - 1 &&
                int.TryParse(arguments[startNumberIndex + 1], out var startNumber)) {
                var segmentPattern = arguments[segmentPatternIndex + 1];
                await File.WriteAllTextAsync(
                    segmentPattern.Replace("%05d", (startNumber + 1).ToString("00000")),
                    "segment",
                    cancellationToken);
            }

            await File.WriteAllTextAsync(outputPath, "playlist", cancellationToken);
            return new ProcessExecutionResult(0, string.Empty, string.Empty);
        }
    }

    private sealed class BlockingInitialSegmentProcessExecutor : ProcessExecutor {
        private readonly TaskCompletionSource _releaseInitialGeneration = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource InitialGenerationStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<IReadOnlyList<string>> ArgumentHistory { get; } = [];

        public void ReleaseInitialGeneration() {
            _releaseInitialGeneration.TrySetResult();
        }

        public override async Task<ProcessExecutionResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            IReadOnlyDictionary<string, string>? environment,
            CancellationToken cancellationToken, bool lowPriority = false) {
            ArgumentHistory.Add(arguments);
            var outputPath = arguments[^1];
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

            var segmentPatternIndex = arguments.ToList().IndexOf("-hls_segment_filename");
            var startNumberIndex = arguments.ToList().IndexOf("-start_number");
            var segmentPattern = segmentPatternIndex >= 0 && segmentPatternIndex < arguments.Count - 1
                ? arguments[segmentPatternIndex + 1]
                : null;
            var startNumber = startNumberIndex >= 0 &&
                startNumberIndex < arguments.Count - 1 &&
                int.TryParse(arguments[startNumberIndex + 1], out var parsedStart)
                    ? parsedStart
                    : 0;

            if (startNumber == 0) {
                InitialGenerationStarted.TrySetResult();
                await _releaseInitialGeneration.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            }

            if (segmentPattern is not null) {
                var segmentToWrite = startNumber == 0 ? startNumber : startNumber + 1;
                await File.WriteAllTextAsync(
                    segmentPattern.Replace("%05d", segmentToWrite.ToString("00000")),
                    "segment",
                    cancellationToken);
            }

            await File.WriteAllTextAsync(outputPath, "playlist", cancellationToken);
            return new ProcessExecutionResult(0, string.Empty, string.Empty);
        }
    }
}
