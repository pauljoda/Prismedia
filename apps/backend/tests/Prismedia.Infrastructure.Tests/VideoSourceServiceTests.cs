using Microsoft.EntityFrameworkCore;
using Prismedia.Contracts.Media;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Media.Processing;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;
using Prismedia.Infrastructure.Processes;
using Prismedia.Infrastructure.Videos;

namespace Prismedia.Infrastructure.Tests;

public sealed class VideoSourceServiceTests : IDisposable {
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"prismedia-video-source-{Guid.NewGuid():N}");

    public VideoSourceServiceTests() {
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task GetsExistingSourceFileForVideoEntity() {
        await using var db = CreateContext();
        var videoId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var filePath = Path.Combine(_tempDir, "video.mp4");
        await File.WriteAllTextAsync(filePath, "video-bytes");
        SeedVideoSource(db, videoId, filePath, "video/mp4");
        await db.SaveChangesAsync();

        var service = new VideoSourceService(db);
        var source = await service.GetSourceAsync(videoId, CancellationToken.None);

        Assert.NotNull(source);
        Assert.Equal(filePath, source.Path);
        Assert.Equal("video/mp4", source.ContentType);
        Assert.True(source.DirectPlayable);
    }

    [Fact]
    public async Task MarksKnownTranscodeContainersAsNotDirectPlayable() {
        await using var db = CreateContext();
        var videoId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var filePath = Path.Combine(_tempDir, "video.mkv");
        await File.WriteAllTextAsync(filePath, "video-bytes");
        SeedVideoSource(db, videoId, filePath, null);
        await db.SaveChangesAsync();

        var service = new VideoSourceService(db);
        var source = await service.GetSourceAsync(videoId, CancellationToken.None);

        Assert.NotNull(source);
        Assert.Equal("video/x-matroska", source.ContentType);
        Assert.False(source.DirectPlayable);
    }

    [Fact]
    public async Task PrefersPersistedMediaSourceMetadataOverTechnicalRow() {
        await using var db = CreateContext();
        var videoId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var sourceId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var filePath = Path.Combine(_tempDir, "video.mkv");
        await File.WriteAllTextAsync(filePath, "video-bytes");
        SeedVideoSource(db, videoId, filePath, null);
        db.EntityTechnical.Add(new EntityTechnicalRow {
            EntityId = videoId,
            DurationSeconds = 12,
            Width = 640,
            Height = 360,
            Codec = "h264",
            Container = "mp4",
            UpdatedAt = DateTimeOffset.UtcNow
        });
        db.MediaSources.Add(new MediaSourceRow {
            Id = sourceId,
            EntityId = videoId,
            Path = filePath,
            Protocol = "File",
            Container = "matroska",
            DurationSeconds = 42,
            Width = 1920,
            Height = 1080,
            BitRate = 8_000_000,
            VideoCodec = "hevc",
            AudioCodec = "aac",
            FrameRate = 23.976,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var service = new VideoSourceService(db);
        var source = await service.GetSourceAsync(videoId, CancellationToken.None);

        Assert.NotNull(source);
        Assert.Equal(sourceId, source.MediaSourceId);
        Assert.Equal("matroska", source.Container);
        Assert.Equal(42, source.DurationSeconds);
        Assert.Equal(1920, source.Width);
        Assert.Equal(1080, source.Height);
        Assert.Equal("hevc", source.VideoCodec);
        Assert.Equal("aac", source.AudioCodec);
        Assert.Equal(23.976, source.FrameRate);
    }

    [Fact]
    public async Task UsesLazyProbeMetadataWhenSourceHasNotBeenPersistentlyProbed() {
        await using var db = CreateContext();
        var videoId = Guid.Parse("45454545-4545-4545-4545-454545454545");
        var filePath = Path.Combine(_tempDir, "video.mkv");
        await File.WriteAllTextAsync(filePath, "video-bytes");
        SeedVideoSource(db, videoId, filePath, null);
        await db.SaveChangesAsync();
        var probe = new MediaProbeService(new JsonProcessExecutor("""
            {
              "format": {
                "duration": "1293.444000",
                "size": "826454433",
                "bit_rate": "5109366",
                "format_name": "matroska,webm"
              },
              "streams": [
                { "index": 0, "codec_type": "video", "codec_name": "h264", "width": 1920, "height": 1080, "avg_frame_rate": "24000/1001", "disposition": { "default": 1, "forced": 0 } },
                { "index": 1, "codec_type": "audio", "codec_name": "aac", "sample_rate": "48000", "channels": 2, "disposition": { "default": 1, "forced": 0 } }
              ]
            }
            """));

        var service = new VideoSourceService(db, probe);
        var source = await service.GetSourceAsync(videoId, CancellationToken.None);

        Assert.NotNull(source);
        Assert.Equal(1293.444, source.DurationSeconds);
        Assert.Equal(1920, source.Width);
        Assert.Equal(1080, source.Height);
        Assert.Equal("matroska", source.Container);
        Assert.Equal("h264", source.VideoCodec);
        Assert.Equal("aac", source.AudioCodec);
        Assert.Equal(23.98, source.FrameRate);
        Assert.Equal(2, source.Streams!.Count);
    }

    [Fact]
    public async Task ProbesStreamsWhenPersistedSourceOnlyHasOneSyntheticAudioTrack() {
        await using var db = CreateContext();
        var videoId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var sourceId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        var filePath = Path.Combine(_tempDir, "video.mkv");
        await File.WriteAllTextAsync(filePath, "video-bytes");
        SeedVideoSource(db, videoId, filePath, null);
        db.MediaSources.Add(new MediaSourceRow {
            Id = sourceId,
            EntityId = videoId,
            Path = filePath,
            Protocol = "File",
            Container = "matroska",
            DurationSeconds = 42,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        db.MediaStreams.Add(new MediaStreamRow {
            Id = Guid.NewGuid(),
            MediaSourceId = sourceId,
            EntityId = videoId,
            StreamIndex = 1,
            Type = "Audio",
            Codec = "aac",
            Title = "Audio",
            IsDefault = true,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var probe = new MediaProbeService(new JsonProcessExecutor("""
            {
              "format": { "duration": "42", "format_name": "matroska" },
              "streams": [
                { "index": 0, "codec_type": "video", "codec_name": "h264", "width": 1920, "height": 1080, "avg_frame_rate": "24/1", "disposition": { "default": 1, "forced": 0 } },
                { "index": 1, "codec_type": "audio", "codec_name": "aac", "sample_rate": "48000", "channels": 2, "tags": { "language": "spa", "title": "Spanish" }, "disposition": { "default": 0, "forced": 0 } },
                { "index": 2, "codec_type": "audio", "codec_name": "aac", "sample_rate": "48000", "channels": 2, "tags": { "language": "eng", "title": "English" }, "disposition": { "default": 1, "forced": 0 } }
              ]
            }
            """));

        var service = new VideoSourceService(db, probe);
        var source = await service.GetSourceAsync(videoId, CancellationToken.None);

        Assert.NotNull(source);
        var audioStreams = source.Streams!.Where(stream => stream.Type == "Audio").ToList();
        Assert.Equal(2, audioStreams.Count);
        Assert.Equal(2, audioStreams.Single(stream => stream.IsDefault).StreamIndex);
        Assert.Equal("eng", audioStreams.Single(stream => stream.StreamIndex == 2).Language);
    }

    [Fact]
    public async Task ProbesStaleHevcStreamsMissingHdrMetadata() {
        await using var db = CreateContext();
        var videoId = Guid.Parse("77777777-7777-7777-7777-777777777777");
        var sourceId = Guid.Parse("99999999-9999-9999-9999-999999999999");
        var filePath = Path.Combine(_tempDir, "video.mkv");
        await File.WriteAllTextAsync(filePath, "video-bytes");
        SeedVideoSource(db, videoId, filePath, null);
        db.MediaSources.Add(new MediaSourceRow {
            Id = sourceId,
            EntityId = videoId,
            Path = filePath,
            Protocol = "File",
            Container = "matroska",
            VideoCodec = "hevc",
            DurationSeconds = 42,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        db.MediaStreams.Add(new MediaStreamRow {
            Id = Guid.NewGuid(),
            MediaSourceId = sourceId,
            EntityId = videoId,
            StreamIndex = 0,
            Type = "Video",
            Codec = "hevc",
            IsDefault = true,
            CreatedAt = DateTimeOffset.UtcNow
        });
        db.MediaStreams.Add(new MediaStreamRow {
            Id = Guid.NewGuid(),
            MediaSourceId = sourceId,
            EntityId = videoId,
            StreamIndex = 1,
            Type = "Audio",
            Codec = "aac",
            IsDefault = true,
            CreatedAt = DateTimeOffset.UtcNow
        });
        db.MediaStreams.Add(new MediaStreamRow {
            Id = Guid.NewGuid(),
            MediaSourceId = sourceId,
            EntityId = videoId,
            StreamIndex = 2,
            Type = "Audio",
            Codec = "aac",
            IsDefault = false,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var probe = new MediaProbeService(new JsonProcessExecutor("""
            {
              "format": { "duration": "42", "format_name": "matroska" },
              "streams": [
                {
                  "index": 0,
                  "codec_type": "video",
                  "codec_name": "hevc",
                  "pix_fmt": "yuv420p10le",
                  "width": 3840,
                  "height": 1920,
                  "avg_frame_rate": "24000/1001",
                  "color_range": "pc",
                  "side_data_list": [
                    {
                      "side_data_type": "DOVI configuration record",
                      "dv_profile": 5,
                      "dv_level": 6,
                      "rpu_present_flag": 1,
                      "el_present_flag": 0,
                      "bl_present_flag": 1,
                      "dv_bl_signal_compatibility_id": 0
                    }
                  ],
                  "disposition": { "default": 1, "forced": 0 }
                },
                { "index": 1, "codec_type": "audio", "codec_name": "aac", "sample_rate": "48000", "channels": 2, "disposition": { "default": 1, "forced": 0 } },
                { "index": 2, "codec_type": "audio", "codec_name": "aac", "sample_rate": "48000", "channels": 2, "disposition": { "default": 0, "forced": 0 } }
              ]
            }
            """));

        var service = new VideoSourceService(db, probe);
        var source = await service.GetSourceAsync(videoId, CancellationToken.None);

        Assert.NotNull(source);
        var video = Assert.Single(source.Streams!, stream => stream.Type == "Video");
        Assert.Equal("yuv420p10le", video.PixelFormat);
        Assert.Equal(5, video.DvProfile);
        Assert.True(video.RpuPresentFlag);
    }

    [Fact]
    public async Task ResolvesMovieContainerToItsChildVideo() {
        await using var db = CreateContext();
        var movieId = Guid.Parse("88888888-8888-8888-8888-888888888888");
        var videoId = Guid.Parse("88888888-8888-8888-8888-888888888889");
        var filePath = Path.Combine(_tempDir, "video.mp4");
        await File.WriteAllTextAsync(filePath, "video-bytes");
        db.Entities.Add(new EntityRow {
            Id = movieId,
            KindCode = EntityKindRegistry.Movie.Code,
            Title = "Movie folder",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(),
            EntityId = movieId,
            Role = EntityFileRole.Source,
            Path = _tempDir,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        SeedVideoSource(db, videoId, filePath, MediaContentTypes.VideoMp4, parentId: movieId);
        await db.SaveChangesAsync();

        var service = new VideoSourceService(db);
        var source = await service.GetSourceAsync(movieId, CancellationToken.None);

        Assert.NotNull(source);
        Assert.Equal(filePath, source.Path);
    }

    [Fact]
    public async Task ResolvesFlatSeriesContainerToItsFirstChildVideo() {
        await using var db = CreateContext();
        var seriesId = Guid.Parse("11122233-1112-2223-3334-444555566677");
        var videoId = Guid.Parse("11122233-1112-2223-3334-444555566678");
        var filePath = Path.Combine(_tempDir, "clip-01.mp4");
        await File.WriteAllTextAsync(filePath, "video-bytes");
        db.Entities.Add(new EntityRow {
            Id = seriesId,
            KindCode = EntityKindRegistry.VideoSeries.Code,
            Title = "Series folder",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(),
            EntityId = seriesId,
            Role = EntityFileRole.Source,
            Path = _tempDir,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        SeedVideoSource(db, videoId, filePath, MediaContentTypes.VideoMp4, parentId: seriesId, sortOrder: 0);
        await db.SaveChangesAsync();

        var service = new VideoSourceService(db);
        var source = await service.GetSourceAsync(seriesId, CancellationToken.None);

        Assert.NotNull(source);
        Assert.Equal(filePath, source.Path);
    }

    [Fact]
    public async Task ResolvesSeasonStructuredSeriesContainerThroughItsFirstSeason() {
        await using var db = CreateContext();
        var seriesId = Guid.Parse("22233344-2223-3334-4445-555666677788");
        var seasonId = Guid.Parse("22233344-2223-3334-4445-555666677789");
        var videoId = Guid.Parse("22233344-2223-3334-4445-555666677790");
        var filePath = Path.Combine(_tempDir, "s01e01.mp4");
        await File.WriteAllTextAsync(filePath, "video-bytes");
        db.Entities.Add(new EntityRow {
            Id = seriesId,
            KindCode = EntityKindRegistry.VideoSeries.Code,
            Title = "Series folder",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        db.Entities.Add(new EntityRow {
            Id = seasonId,
            KindCode = EntityKindRegistry.VideoSeason.Code,
            Title = "Season 1",
            ParentEntityId = seriesId,
            SortOrder = 0,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        SeedVideoSource(db, videoId, filePath, MediaContentTypes.VideoMp4, parentId: seasonId, sortOrder: 0);
        await db.SaveChangesAsync();

        var service = new VideoSourceService(db);
        var source = await service.GetSourceAsync(seriesId, CancellationToken.None);

        Assert.NotNull(source);
        Assert.Equal(filePath, source.Path);
    }

    [Fact]
    public async Task ResolvesSeasonContainerToItsChildVideo() {
        await using var db = CreateContext();
        var seasonId = Guid.Parse("33344455-3334-4445-5556-666777889900");
        var videoId = Guid.Parse("33344455-3334-4445-5556-666777889901");
        var filePath = Path.Combine(_tempDir, "s02e01.mp4");
        await File.WriteAllTextAsync(filePath, "video-bytes");
        db.Entities.Add(new EntityRow {
            Id = seasonId,
            KindCode = EntityKindRegistry.VideoSeason.Code,
            Title = "Season 2",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        SeedVideoSource(db, videoId, filePath, MediaContentTypes.VideoMp4, parentId: seasonId, sortOrder: 0);
        await db.SaveChangesAsync();

        var service = new VideoSourceService(db);
        var source = await service.GetSourceAsync(seasonId, CancellationToken.None);

        Assert.NotNull(source);
        Assert.Equal(filePath, source.Path);
    }

    [Fact]
    public async Task ReturnsNullForEmptySeriesContainer() {
        await using var db = CreateContext();
        var seriesId = Guid.Parse("44455566-4445-5556-6667-778889900112");
        db.Entities.Add(new EntityRow {
            Id = seriesId,
            KindCode = EntityKindRegistry.VideoSeries.Code,
            Title = "Empty series",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var service = new VideoSourceService(db);
        var source = await service.GetSourceAsync(seriesId, CancellationToken.None);

        Assert.Null(source);
    }

    [Fact]
    public async Task SkipsEmptySeasonToResolveALaterPlayableSeason() {
        await using var db = CreateContext();
        var seriesId = Guid.Parse("55566677-5556-6667-7778-889990011223");
        var emptySeasonId = Guid.Parse("55566677-5556-6667-7778-889990011224");
        var playableSeasonId = Guid.Parse("55566677-5556-6667-7778-889990011225");
        var videoId = Guid.Parse("55566677-5556-6667-7778-889990011226");
        var filePath = Path.Combine(_tempDir, "s02e01.mp4");
        await File.WriteAllTextAsync(filePath, "video-bytes");
        db.Entities.Add(new EntityRow {
            Id = seriesId,
            KindCode = EntityKindRegistry.VideoSeries.Code,
            Title = "Series folder",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        db.Entities.Add(new EntityRow {
            Id = emptySeasonId,
            KindCode = EntityKindRegistry.VideoSeason.Code,
            Title = "Season 1 (not yet scanned)",
            ParentEntityId = seriesId,
            SortOrder = 0,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        db.Entities.Add(new EntityRow {
            Id = playableSeasonId,
            KindCode = EntityKindRegistry.VideoSeason.Code,
            Title = "Season 2",
            ParentEntityId = seriesId,
            SortOrder = 1,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        SeedVideoSource(db, videoId, filePath, MediaContentTypes.VideoMp4, parentId: playableSeasonId, sortOrder: 0);
        await db.SaveChangesAsync();

        var service = new VideoSourceService(db);
        var source = await service.GetSourceAsync(seriesId, CancellationToken.None);

        Assert.NotNull(source);
        Assert.Equal(filePath, source.Path);
    }

    public void Dispose() {
        if (Directory.Exists(_tempDir)) {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private static PrismediaDbContext CreateContext() {
        var options = new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase($"video-source-{Guid.NewGuid():N}")
            .Options;

        return new PrismediaDbContext(options);
    }

    private static void SeedVideoSource(
        PrismediaDbContext db,
        Guid videoId,
        string path,
        string? mimeType,
        Guid? parentId = null,
        int? sortOrder = null) {
        db.Entities.Add(new EntityRow {
            Id = videoId,
            KindCode = EntityKindRegistry.Video.Code,
            Title = "Source",
            ParentEntityId = parentId,
            SortOrder = sortOrder,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(),
            EntityId = videoId,
            Role = EntityFileRole.Source,
            Path = path,
            MimeType = mimeType,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
    }

    private sealed class JsonProcessExecutor : ProcessExecutor {
        private readonly string _json;

        public JsonProcessExecutor(string json) {
            _json = json;
        }

        public override Task<ProcessExecutionResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            IReadOnlyDictionary<string, string>? environment,
            CancellationToken cancellationToken, bool lowPriority = false) {
            return Task.FromResult(new ProcessExecutionResult(0, _json, string.Empty));
        }
    }
}
