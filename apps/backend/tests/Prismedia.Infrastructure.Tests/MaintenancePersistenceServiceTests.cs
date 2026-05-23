using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Media.Persistence;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class MaintenancePersistenceServiceTests : IDisposable {
    private readonly string _dataDir = Path.Combine(Path.GetTempPath(), $"prismedia-maintenance-{Guid.NewGuid():N}");

    [Fact]
    public async Task ClearGeneratedPreviewAssetsRemovesVideoRowsAndCacheFiles() {
        await using var db = CreateContext();
        var videoId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.Entities.Add(new EntityRow {
            Id = videoId,
            KindCode = EntityKindRegistry.Video.Code,
            Title = "HDR video",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.EntityFiles.AddRange(
            FileRow(videoId, EntityFileRole.Source, "/media/source.mkv"),
            FileRow(videoId, EntityFileRole.Thumbnail, "/assets/videos/thumb.jpg"),
            FileRow(videoId, EntityFileRole.Preview, "/assets/videos/preview.mp4"),
            FileRow(videoId, EntityFileRole.Trickplay, "/Videos/trickplay.m3u8"),
            FileRow(videoId, EntityFileRole.Hls, "/Videos/master.m3u8"));
        db.TrickplayInfos.Add(new TrickplayInfoRow {
            EntityId = videoId,
            Width = 320,
            Height = 180,
            TileWidth = 320,
            TileHeight = 180,
            ThumbnailCount = 12,
            IntervalSeconds = 10,
            Bandwidth = 1000,
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();

        var cacheRoot = Path.Combine(_dataDir, "cache");
        var videoCache = Path.Combine(cacheRoot, "videos", videoId.ToString());
        var trickplayCache = Path.Combine(cacheRoot, "trickplay", videoId.ToString(), "320");
        var hlsvCache = Path.Combine(cacheRoot, "hlsv", videoId.ToString(), "audio_00001", "720p");
        var hls2Cache = Path.Combine(cacheRoot, "hls2", videoId.ToString());
        var hlsCache = Path.Combine(cacheRoot, "hls", videoId.ToString());
        Directory.CreateDirectory(Path.Combine(videoCache, "trickplay-frames"));
        Directory.CreateDirectory(Path.Combine(videoCache, "subtitles"));
        Directory.CreateDirectory(trickplayCache);
        Directory.CreateDirectory(hlsvCache);
        Directory.CreateDirectory(hls2Cache);
        Directory.CreateDirectory(hlsCache);
        await File.WriteAllTextAsync(Path.Combine(videoCache, "thumb.jpg"), "old");
        await File.WriteAllTextAsync(Path.Combine(videoCache, "preview.mp4"), "old");
        await File.WriteAllTextAsync(Path.Combine(videoCache, "trickplay.vtt"), "old");
        await File.WriteAllTextAsync(Path.Combine(videoCache, "trickplay-frames", "000001.jpg"), "old");
        await File.WriteAllTextAsync(Path.Combine(videoCache, "subtitles", "embedded-eng-2.vtt"), "keep");
        await File.WriteAllTextAsync(Path.Combine(trickplayCache, "0.jpg"), "old");
        await File.WriteAllTextAsync(Path.Combine(hlsvCache, "seg_00001.ts"), "old");
        await File.WriteAllTextAsync(Path.Combine(hls2Cache, "master.m3u8"), "old");
        await File.WriteAllTextAsync(Path.Combine(hlsCache, "master.m3u8"), "old");

        var service = new MaintenancePersistenceService(db, _dataDir);
        await service.ClearGeneratedPreviewAssetsAsync(EntityKind.Video, videoId, CancellationToken.None);

        Assert.Single(db.EntityFiles, file => file.EntityId == videoId);
        Assert.Contains(db.EntityFiles, file => file.Role == EntityFileRole.Source);
        Assert.Empty(db.TrickplayInfos.Where(info => info.EntityId == videoId));
        Assert.False(File.Exists(Path.Combine(videoCache, "thumb.jpg")));
        Assert.False(File.Exists(Path.Combine(videoCache, "preview.mp4")));
        Assert.False(File.Exists(Path.Combine(videoCache, "trickplay.vtt")));
        Assert.False(Directory.Exists(Path.Combine(videoCache, "trickplay-frames")));
        Assert.True(File.Exists(Path.Combine(videoCache, "subtitles", "embedded-eng-2.vtt")));
        Assert.False(Directory.Exists(Path.Combine(cacheRoot, "trickplay", videoId.ToString())));
        Assert.False(Directory.Exists(Path.Combine(cacheRoot, "hlsv", videoId.ToString())));
        Assert.False(Directory.Exists(Path.Combine(cacheRoot, "hls2", videoId.ToString())));
        Assert.False(Directory.Exists(Path.Combine(cacheRoot, "hls", videoId.ToString())));
    }

    public void Dispose() {
        if (Directory.Exists(_dataDir)) {
            Directory.Delete(_dataDir, recursive: true);
        }
    }

    private static EntityFileRow FileRow(Guid entityId, EntityFileRole role, string path) {
        var now = DateTimeOffset.UtcNow;
        return new EntityFileRow {
            Id = Guid.NewGuid(),
            EntityId = entityId,
            Role = role,
            Path = path,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private static PrismediaDbContext CreateContext() {
        var options = new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase($"maintenance-{Guid.NewGuid():N}")
            .Options;

        return new PrismediaDbContext(options);
    }
}
