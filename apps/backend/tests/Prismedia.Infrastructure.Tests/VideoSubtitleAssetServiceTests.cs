using Microsoft.EntityFrameworkCore;
using Prismedia.Contracts.Media;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Media.Processing;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;
using Prismedia.Infrastructure.Processes;
using Prismedia.Infrastructure.Videos;

namespace Prismedia.Infrastructure.Tests;

public sealed class VideoSubtitleAssetServiceTests : IDisposable {
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"prismedia-subtitle-assets-{Guid.NewGuid():N}");

    public VideoSubtitleAssetServiceTests() {
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task ExtractsEmbeddedAssSourceFromStoredStreamIndex() {
        await using var db = CreateContext();
        var videoId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var subtitleId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var sourcePath = Path.Combine(_tempDir, "video.mkv");
        var paths = CreatePaths();
        var vttPath = Path.Combine(paths.SubtitleDir(videoId), "embedded-eng-4.vtt");
        await File.WriteAllTextAsync(sourcePath, "video");
        Directory.CreateDirectory(Path.GetDirectoryName(vttPath)!);
        await File.WriteAllTextAsync(vttPath, "WEBVTT");

        SeedVideo(db, videoId, sourcePath);
        db.EntitySubtitles.Add(new EntitySubtitleRow {
            Id = subtitleId,
            EntityId = videoId,
            Language = "eng",
            Label = "English",
            Format = "vtt",
            Source = EntitySubtitleSource.Embedded,
            StoragePath = vttPath,
            SourceFormat = "ass",
            SourcePath = "4",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var process = new SubtitleWritingProcessExecutor("[Script Info]\nTitle: English");
        var service = new VideoSubtitleAssetService(
            db,
            process,
            new MediaToolOptions("/usr/local/bin/ffmpeg"),
            paths);

        var asset = await service.GetSubtitleSourceAsync(videoId, subtitleId, CancellationToken.None);

        Assert.NotNull(asset);
        Assert.Equal("text/x-ssa; charset=utf-8", asset.ContentType);
        Assert.Equal(Path.ChangeExtension(vttPath, SubtitleFileExtensions.Ass), asset.Path);
        Assert.Equal("[Script Info]\nTitle: English", await File.ReadAllTextAsync(asset.Path));
        Assert.Equal("/usr/local/bin/ffmpeg", process.FileName);
        Assert.Contains("0:4", process.Arguments);
        Assert.Contains("-c:s", process.Arguments);
        Assert.Contains("copy", process.Arguments);
    }

    [Fact]
    public async Task ServesSidecarAssOnlyFromPairedOwnedCachePath() {
        await using var db = CreateContext();
        var videoId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var subtitleId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var paths = CreatePaths();
        var subtitleDir = paths.SubtitleDir(videoId);
        Directory.CreateDirectory(subtitleDir);
        var vttPath = Path.Combine(subtitleDir, "sidecar-track-content.vtt");
        var rawPath = Path.ChangeExtension(vttPath, SubtitleFileExtensions.Ass);
        await File.WriteAllTextAsync(vttPath, "WEBVTT");
        await File.WriteAllTextAsync(rawPath, "[Script Info]");

        SeedVideo(db, videoId, Path.Combine(_tempDir, "video.mkv"));
        db.EntitySubtitles.Add(new EntitySubtitleRow {
            Id = subtitleId,
            EntityId = videoId,
            SourceKey = "movie.en.ass",
            Language = "en",
            Format = SubtitleFormats.Vtt,
            Source = EntitySubtitleSource.Sidecar,
            StoragePath = vttPath,
            SourceFormat = SubtitleFormats.Ass,
            SourcePath = rawPath,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var service = new VideoSubtitleAssetService(
            db,
            new SubtitleWritingProcessExecutor("unused"),
            new MediaToolOptions(),
            paths);

        var asset = await service.GetSubtitleSourceAsync(videoId, subtitleId, CancellationToken.None);

        Assert.NotNull(asset);
        Assert.Equal(rawPath, asset.Path);
        Assert.Equal(MediaContentTypes.SsaUtf8, asset.ContentType);
    }

    [Fact]
    public async Task RejectsLiveLibraryPathForSidecarSource() {
        await using var db = CreateContext();
        var videoId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var subtitleId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        var paths = CreatePaths();
        var subtitleDir = paths.SubtitleDir(videoId);
        Directory.CreateDirectory(subtitleDir);
        var vttPath = Path.Combine(subtitleDir, "sidecar-track-content.vtt");
        var liveLibraryPath = Path.Combine(_tempDir, "Movie.en.ass");
        await File.WriteAllTextAsync(vttPath, "WEBVTT");
        await File.WriteAllTextAsync(liveLibraryPath, "[Script Info]");

        SeedVideo(db, videoId, Path.Combine(_tempDir, "video.mkv"));
        db.EntitySubtitles.Add(new EntitySubtitleRow {
            Id = subtitleId,
            EntityId = videoId,
            SourceKey = "movie.en.ass",
            Language = "en",
            Format = SubtitleFormats.Vtt,
            Source = EntitySubtitleSource.Sidecar,
            StoragePath = vttPath,
            SourceFormat = SubtitleFormats.Ass,
            SourcePath = liveLibraryPath,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var service = new VideoSubtitleAssetService(
            db,
            new SubtitleWritingProcessExecutor("unused"),
            new MediaToolOptions(),
            paths);

        var asset = await service.GetSubtitleSourceAsync(videoId, subtitleId, CancellationToken.None);

        Assert.Null(asset);
    }

    [Fact]
    public async Task RejectsSymlinkedSidecarSourceInsideCache() {
        await using var db = CreateContext();
        var videoId = Guid.Parse("77777777-7777-7777-7777-777777777777");
        var subtitleId = Guid.Parse("88888888-8888-8888-8888-888888888888");
        var paths = CreatePaths();
        var subtitleDir = paths.SubtitleDir(videoId);
        Directory.CreateDirectory(subtitleDir);
        var vttPath = Path.Combine(subtitleDir, "sidecar-track-content.vtt");
        var rawPath = Path.ChangeExtension(vttPath, SubtitleFileExtensions.Ass);
        var outsidePath = Path.Combine(_tempDir, "outside.ass");
        await File.WriteAllTextAsync(vttPath, "WEBVTT");
        await File.WriteAllTextAsync(outsidePath, "[Script Info]");
        File.CreateSymbolicLink(rawPath, outsidePath);

        SeedVideo(db, videoId, Path.Combine(_tempDir, "video.mkv"));
        db.EntitySubtitles.Add(new EntitySubtitleRow {
            Id = subtitleId,
            EntityId = videoId,
            SourceKey = "movie.en.ass",
            Language = "en",
            Format = SubtitleFormats.Vtt,
            Source = EntitySubtitleSource.Sidecar,
            StoragePath = vttPath,
            SourceFormat = SubtitleFormats.Ass,
            SourcePath = rawPath,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var service = new VideoSubtitleAssetService(
            db,
            new SubtitleWritingProcessExecutor("unused"),
            new MediaToolOptions(),
            paths);

        var asset = await service.GetSubtitleSourceAsync(videoId, subtitleId, CancellationToken.None);

        Assert.Null(asset);
    }

    [Fact]
    public async Task RejectsSidecarAssetsThroughSymlinkedCacheParent() {
        await using var db = CreateContext();
        var videoId = Guid.NewGuid();
        var subtitleId = Guid.NewGuid();
        var paths = CreatePaths();
        Directory.CreateDirectory(paths.CacheRoot);
        var outside = Path.Combine(_tempDir, "outside-parent");
        var outsideSubtitleDir = Path.Combine(outside, videoId.ToString(), "subtitles");
        Directory.CreateDirectory(outsideSubtitleDir);
        var rawPath = Path.Combine(outsideSubtitleDir, "sidecar-track.ass");
        var vttPath = Path.Combine(outsideSubtitleDir, "sidecar-track.vtt");
        await File.WriteAllTextAsync(rawPath, "[Script Info]");
        await File.WriteAllTextAsync(vttPath, "WEBVTT");
        Directory.CreateSymbolicLink(Path.Combine(paths.CacheRoot, "videos"), outside);
        var lexicalVttPath = Path.Combine(
            paths.CacheRoot, "videos", videoId.ToString(), "subtitles", "sidecar-track.vtt");
        var lexicalRawPath = Path.ChangeExtension(lexicalVttPath, SubtitleFileExtensions.Ass);

        SeedVideo(db, videoId, Path.Combine(_tempDir, "video.mkv"));
        db.EntitySubtitles.Add(new EntitySubtitleRow {
            Id = subtitleId,
            EntityId = videoId,
            SourceKey = new string('a', 64),
            Language = "en",
            Format = SubtitleFormats.Vtt,
            Source = EntitySubtitleSource.Sidecar,
            StoragePath = lexicalVttPath,
            SourceFormat = SubtitleFormats.Ass,
            SourcePath = lexicalRawPath,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var service = new VideoSubtitleAssetService(
            db,
            new SubtitleWritingProcessExecutor("unused"),
            new MediaToolOptions(),
            paths);

        Assert.Null(await service.GetSubtitleAsync(videoId, subtitleId, CancellationToken.None));
        Assert.Null(await service.GetSubtitleSourceAsync(videoId, subtitleId, CancellationToken.None));
    }

    public void Dispose() {
        if (Directory.Exists(_tempDir)) {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private static PrismediaDbContext CreateContext() {
        var options = new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase($"video-subtitle-assets-{Guid.NewGuid():N}")
            .Options;

        return new PrismediaDbContext(options);
    }

    private AssetPathService CreatePaths() =>
        new(Path.Combine(_tempDir, "data"), Path.Combine(_tempDir, "cache"));

    private static void SeedVideo(PrismediaDbContext db, Guid videoId, string sourcePath) {
        db.Entities.Add(new EntityRow {
            Id = videoId,
            KindCode = EntityKind.Video.ToCode(),
            Title = "Video",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(),
            EntityId = videoId,
            Role = EntityFileRole.Source,
            Path = sourcePath,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
    }

    private sealed class SubtitleWritingProcessExecutor(string content) : ProcessExecutor {
        public string? FileName { get; private set; }
        public IReadOnlyList<string> Arguments { get; private set; } = [];

        public override async Task<ProcessExecutionResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            IReadOnlyDictionary<string, string>? environment,
            CancellationToken cancellationToken,
            bool lowPriority = false) {
            FileName = fileName;
            Arguments = arguments.ToArray();
            var outputPath = arguments[^1];
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            await File.WriteAllTextAsync(outputPath, content, cancellationToken);
            return new ProcessExecutionResult(0, string.Empty, string.Empty);
        }
    }
}
