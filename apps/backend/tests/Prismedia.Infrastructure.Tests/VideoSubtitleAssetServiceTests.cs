using Microsoft.EntityFrameworkCore;
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
        var vttPath = Path.Combine(_tempDir, "subtitles", "embedded-eng-4.vtt");
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
        var service = new VideoSubtitleAssetService(db, process, new MediaToolOptions("/usr/local/bin/ffmpeg"));

        var asset = await service.GetSubtitleSourceAsync(videoId, subtitleId, CancellationToken.None);

        Assert.NotNull(asset);
        Assert.Equal("text/x-ssa; charset=utf-8", asset.ContentType);
        Assert.Equal(Path.Combine(_tempDir, "subtitles", "embedded-eng-4.ass"), asset.Path);
        Assert.Equal("[Script Info]\nTitle: English", await File.ReadAllTextAsync(asset.Path));
        Assert.Equal("/usr/local/bin/ffmpeg", process.FileName);
        Assert.Contains("0:4", process.Arguments);
        Assert.Contains("-c:s", process.Arguments);
        Assert.Contains("copy", process.Arguments);
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

    private static void SeedVideo(PrismediaDbContext db, Guid videoId, string sourcePath) {
        db.Entities.Add(new EntityRow {
            Id = videoId,
            KindCode = EntityKindRegistry.Video.Code,
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
