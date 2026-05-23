using Prismedia.Infrastructure.Videos;
using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class TrickplayServiceTests : IDisposable {
    private readonly string _cacheRoot = Path.Combine(Path.GetTempPath(), $"prismedia-trickplay-{Guid.NewGuid():N}");

    public TrickplayServiceTests() {
        Directory.CreateDirectory(_cacheRoot);
    }

    [Fact]
    public async Task BuildsImagesOnlyPlaylistFromJpegTiles() {
        var itemId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var tileRoot = Path.Combine(_cacheRoot, "trickplay", itemId.ToString(), "320");
        Directory.CreateDirectory(tileRoot);
        await File.WriteAllTextAsync(Path.Combine(tileRoot, "0.jpg"), "tile0");
        await File.WriteAllTextAsync(Path.Combine(tileRoot, "1.jpg"), "tile1");
        var service = new TrickplayService(new HlsAssetServiceOptions(_cacheRoot));

        var playlist = await service.GetPlaylistAsync(itemId, 320, CancellationToken.None);

        Assert.NotNull(playlist);
        Assert.Contains("#EXT-X-IMAGES-ONLY", playlist.Content);
        Assert.Contains("#EXT-X-TILES:RESOLUTION=320x180,LAYOUT=5x5,DURATION=10", playlist.Content);
        Assert.Contains("0.jpg", playlist.Content);
        Assert.Contains("1.jpg", playlist.Content);
    }

    [Fact]
    public async Task ResolvesTileInsideExpectedWidthFolder() {
        var itemId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var tileRoot = Path.Combine(_cacheRoot, "trickplay", itemId.ToString(), "320");
        Directory.CreateDirectory(tileRoot);
        var tilePath = Path.Combine(tileRoot, "0.jpg");
        await File.WriteAllTextAsync(tilePath, "tile0");
        var service = new TrickplayService(new HlsAssetServiceOptions(_cacheRoot));

        var tile = await service.GetTileAsync(itemId, 320, 0, CancellationToken.None);

        Assert.NotNull(tile);
        Assert.Equal(tilePath, tile.Path);
        Assert.Equal("image/jpeg", tile.ContentType);
        Assert.Equal("public, max-age=31536000, immutable", tile.CacheControl);
    }

    [Fact]
    public async Task PlaylistFallsBackToAvailableGeneratedWidth() {
        await using var db = CreateContext();
        var itemId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        db.Entities.Add(new EntityRow {
            Id = itemId,
            KindCode = EntityKindRegistry.Video.Code,
            Title = "Video",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        db.TrickplayInfos.Add(new TrickplayInfoRow {
            EntityId = itemId,
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
        var tileRoot = Path.Combine(_cacheRoot, "trickplay", itemId.ToString(), "280");
        Directory.CreateDirectory(tileRoot);
        await File.WriteAllTextAsync(Path.Combine(tileRoot, "0.jpg"), "tile0");
        var service = new TrickplayService(new HlsAssetServiceOptions(_cacheRoot), db);

        var playlist = await service.GetPlaylistAsync(itemId, 320, CancellationToken.None);

        Assert.NotNull(playlist);
        Assert.Contains("#EXT-X-TILES:RESOLUTION=280x158,LAYOUT=4x4,DURATION=7", playlist.Content);
    }

    [Fact]
    public async Task TileFallsBackToAvailableGeneratedWidth() {
        var itemId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var tileRoot = Path.Combine(_cacheRoot, "trickplay", itemId.ToString(), "280");
        Directory.CreateDirectory(tileRoot);
        var tilePath = Path.Combine(tileRoot, "0.jpg");
        await File.WriteAllTextAsync(tilePath, "tile0");
        var service = new TrickplayService(new HlsAssetServiceOptions(_cacheRoot));

        var tile = await service.GetTileAsync(itemId, 320, 0, CancellationToken.None);

        Assert.NotNull(tile);
        Assert.Equal(tilePath, tile.Path);
    }

    [Fact]
    public async Task PlaylistUsesPersistedTrickplayInfoWhenAvailable() {
        await using var db = CreateContext();
        var itemId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        db.Entities.Add(new EntityRow {
            Id = itemId,
            KindCode = EntityKindRegistry.Video.Code,
            Title = "Video",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        db.TrickplayInfos.Add(new TrickplayInfoRow {
            EntityId = itemId,
            Width = 240,
            Height = 134,
            TileWidth = 4,
            TileHeight = 4,
            ThumbnailCount = 16,
            IntervalSeconds = 7,
            Bandwidth = 1234,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var tileRoot = Path.Combine(_cacheRoot, "trickplay", itemId.ToString(), "240");
        Directory.CreateDirectory(tileRoot);
        await File.WriteAllTextAsync(Path.Combine(tileRoot, "0.jpg"), "tile0");
        var service = new TrickplayService(new HlsAssetServiceOptions(_cacheRoot), db);

        var playlist = await service.GetPlaylistAsync(itemId, 240, CancellationToken.None);

        Assert.NotNull(playlist);
        Assert.Contains("#EXT-X-TILES:RESOLUTION=240x134,LAYOUT=4x4,DURATION=7", playlist.Content);
        Assert.Contains("#EXT-X-TARGETDURATION:112", playlist.Content);
    }

    public void Dispose() {
        if (Directory.Exists(_cacheRoot)) {
            Directory.Delete(_cacheRoot, recursive: true);
        }
    }

    private static PrismediaDbContext CreateContext() {
        var options = new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase($"trickplay-{Guid.NewGuid():N}")
            .Options;

        return new PrismediaDbContext(options);
    }
}
