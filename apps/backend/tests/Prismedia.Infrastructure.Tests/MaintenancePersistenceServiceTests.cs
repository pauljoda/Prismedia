using Microsoft.EntityFrameworkCore;
using Prismedia.Contracts.Playback;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Media.Persistence;
using Prismedia.Infrastructure.Media.Processing;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class MaintenancePersistenceServiceTests : IDisposable {
    private readonly string _dataDir = Path.Combine(Path.GetTempPath(), $"prismedia-maintenance-{Guid.NewGuid():N}");

    [Theory]
    [InlineData(EntityKind.Movie)]
    [InlineData(EntityKind.VideoEpisode)]
    [InlineData(EntityKind.Video)]
    public async Task ClearGeneratedPreviewAssetsRemovesVideoRowsAndCacheFiles(EntityKind kind) {
        await using var db = CreateContext();
        var videoId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.Entities.Add(new EntityRow {
            Id = videoId,
            KindCode = kind.ToCode(),
            Title = "HDR video",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.EntityFiles.AddRange(
            FileRow(videoId, EntityFileRole.Source, "/media/source.mkv"),
            FileRow(videoId, EntityFileRole.Thumbnail, "/assets/videos/thumb.jpg"),
            FileRow(videoId, EntityFileRole.Preview, "/assets/videos/preview.mp4"),
            FileRow(videoId, EntityFileRole.Trickplay, VideoPlaybackProtocol.TrickplayPlaylistPath(videoId, 320)),
            FileRow(videoId, EntityFileRole.Hls, VideoPlaybackProtocol.HlsPath(videoId, VideoPlaybackProtocol.Hls.MasterPlaylist)),
            FileRow(videoId, EntityFileRole.Thumbnail, "/custom/thumbnail.jpg", FileSourceKind.Custom.ToCode()),
            FileRow(videoId, EntityFileRole.Poster, "/custom/poster.jpg", FileSourceKind.Custom.ToCode()),
            FileRow(videoId, EntityFileRole.Backdrop, "/custom/backdrop.jpg", FileSourceKind.Custom.ToCode()),
            FileRow(videoId, EntityFileRole.Logo, "/custom/logo.jpg", FileSourceKind.Custom.ToCode()),
            FileRow(videoId, EntityFileRole.Cover, "/plugin/cover.jpg", source: "plugin"));
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

        var service = new MaintenancePersistenceService(db, new AssetPathService(_dataDir));
        await service.ClearGeneratedPreviewAssetsAsync(kind, videoId, CancellationToken.None);

        Assert.Equal(6, db.EntityFiles.Count(file => file.EntityId == videoId));
        Assert.Contains(db.EntityFiles, file => file.Role == EntityFileRole.Source);
        Assert.Contains(db.EntityFiles, file => file.Role == EntityFileRole.Thumbnail && file.Source == FileSourceKind.Custom.ToCode());
        Assert.Contains(db.EntityFiles, file => file.Role == EntityFileRole.Cover && file.Source == "plugin");
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

    [Fact]
    public async Task CleanupOrphanedSubtitleAssetsKeepsReferencedAndRecentGenerations() {
        await using var db = CreateContext();
        var videoId = Guid.NewGuid();
        var paths = new AssetPathService(_dataDir);
        var subtitleDirectory = paths.EnsureSubtitleDirectorySafe(videoId);
        var retained = Path.Combine(subtitleDirectory, "sidecar-retained.vtt");
        var orphaned = Path.Combine(subtitleDirectory, "sidecar-orphaned.vtt");
        var recent = Path.Combine(subtitleDirectory, "sidecar-recent.vtt");
        await File.WriteAllTextAsync(retained, "WEBVTT");
        await File.WriteAllTextAsync(orphaned, "WEBVTT");
        await File.WriteAllTextAsync(recent, "WEBVTT");
        File.SetLastWriteTimeUtc(orphaned, DateTime.UtcNow.AddHours(-2));
        db.Entities.Add(new EntityRow {
            Id = videoId,
            KindCode = EntityKind.Video.ToCode(),
            Title = "Subtitled video",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        db.EntitySubtitles.Add(new EntitySubtitleRow {
            Id = Guid.NewGuid(),
            EntityId = videoId,
            Source = EntitySubtitleSource.Sidecar,
            SourceKey = "retained",
            Language = "en",
            Format = "vtt",
            StoragePath = retained,
            SourceFormat = "srt",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var service = new MaintenancePersistenceService(db, paths);

        var removed = await service.CleanupOrphanedSubtitleAssetsAsync(CancellationToken.None);

        Assert.Equal(1, removed);
        Assert.True(File.Exists(retained));
        Assert.False(File.Exists(orphaned));
        Assert.True(File.Exists(recent));
    }

    [Fact]
    public async Task ClearGeneratedPreviewAssetsLeavesStructuralKindsUntouched() {
        await using var db = CreateContext();
        var entityId = Guid.NewGuid();
        db.Entities.Add(new EntityRow {
            Id = entityId,
            KindCode = EntityKind.VideoSeries.ToCode(),
            Title = "Series",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        db.EntityFiles.Add(FileRow(entityId, EntityFileRole.Poster, "/custom/series.jpg", FileSourceKind.Custom.ToCode()));
        await db.SaveChangesAsync();

        await new MaintenancePersistenceService(db, new AssetPathService(_dataDir))
            .ClearGeneratedPreviewAssetsAsync(EntityKind.VideoSeries, entityId, CancellationToken.None);

        Assert.Single(db.EntityFiles);
    }

    public void Dispose() {
        if (Directory.Exists(_dataDir)) {
            Directory.Delete(_dataDir, recursive: true);
        }
    }

    private static EntityFileRow FileRow(
        Guid entityId,
        EntityFileRole role,
        string path,
        string? source = null) {
        var now = DateTimeOffset.UtcNow;
        return new EntityFileRow {
            Id = Guid.NewGuid(),
            EntityId = entityId,
            Role = role,
            Path = path,
            Source = source ?? FileSourceKind.Scan.ToCode(),
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
