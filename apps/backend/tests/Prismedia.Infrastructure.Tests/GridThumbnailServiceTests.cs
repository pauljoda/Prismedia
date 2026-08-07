using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Media.Processing;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;
using Prismedia.Infrastructure.Processes;
using SkiaSharp;

namespace Prismedia.Infrastructure.Tests;

public sealed class GridThumbnailServiceTests : IDisposable {
    private readonly string _dataDir = Path.Combine(Path.GetTempPath(), $"prismedia-grid-{Guid.NewGuid():N}");
    private readonly AssetPathService _assets;

    public GridThumbnailServiceTests() {
        _assets = new AssetPathService(_dataDir);
    }

    [Fact]
    public async Task EnsureGeneratesDownscaledGridVariantAndRecordsFile() {
        await using var db = CreateContext();
        var entityId = Guid.NewGuid();
        SeedEntity(db, entityId);
        // Cover lives at the disk path the /assets URL maps to.
        var coverUrl = AssetPathService.VideoThumbnailUrl(entityId);
        WriteImage(_assets.ResolveAssetDiskPath(coverUrl)!, 1280, 720);
        AddEntityFile(db, entityId, EntityFileRole.Thumbnail, coverUrl, FileSourceKind.Scan.ToCode());
        await db.SaveChangesAsync();

        var service = CreateService(db);
        await service.EnsureAsync(entityId, CancellationToken.None);

        var gridPath = _assets.GridThumbnailPath(entityId);
        Assert.True(File.Exists(gridPath), "grid thumbnail file should be written");
        using (var bmp = SKBitmap.Decode(gridPath)) {
            Assert.NotNull(bmp);
            Assert.True(bmp!.Width <= 480, $"width {bmp.Width} should be downscaled to <= 480");
            Assert.Equal(720d / 1280d, bmp.Height / (double)bmp.Width, 1); // aspect preserved
        }

        var row = await db.EntityFiles.SingleAsync(f => f.EntityId == entityId && f.Role == EntityFileRole.GridThumbnail);
        Assert.Equal(AssetPathService.GridThumbnailUrl(entityId), row.Path);
        Assert.Equal("image/jpeg", row.MimeType);
        Assert.True(row.SizeBytes > 0);
    }

    [Fact]
    public async Task EnsureDerivesGridVariantFromCustomArtworkOverScanThumbnail() {
        await using var db = CreateContext();
        var entityId = Guid.NewGuid();
        SeedEntity(db, entityId);

        var scanUrl = AssetPathService.VideoThumbnailUrl(entityId);
        WriteImage(_assets.ResolveAssetDiskPath(scanUrl)!, 1280, 720);
        AddEntityFile(db, entityId, EntityFileRole.Thumbnail, scanUrl, FileSourceKind.Scan.ToCode());

        // Custom poster artwork should win cover selection, so the grid variant derives from it.
        var customUrl = $"/assets/custom/artwork/{entityId}/poster-1.jpg";
        var customPath = _assets.ResolveAssetDiskPath(customUrl)!;
        WriteImage(customPath, 600, 900); // portrait, distinct aspect ratio
        AddEntityFile(db, entityId, EntityFileRole.Poster, customUrl, FileSourceKind.Custom.ToCode());
        await db.SaveChangesAsync();

        var service = CreateService(db);
        await service.EnsureAsync(entityId, CancellationToken.None);

        using var bmp = SKBitmap.Decode(_assets.GridThumbnailPath(entityId));
        Assert.NotNull(bmp);
        // Portrait aspect proves it came from the custom artwork, not the 16:9 scan thumb.
        Assert.True(bmp!.Height > bmp.Width, "grid variant should inherit the custom poster's portrait aspect");
    }

    [Fact]
    public async Task EnsureIsNoOpWhenEntityHasNoCover() {
        await using var db = CreateContext();
        var entityId = Guid.NewGuid();
        SeedEntity(db, entityId);
        await db.SaveChangesAsync();

        var service = CreateService(db);
        await service.EnsureAsync(entityId, CancellationToken.None);

        Assert.False(File.Exists(_assets.GridThumbnailPath(entityId)));
        Assert.Null(await db.EntityFiles.FirstOrDefaultAsync(f => f.EntityId == entityId && f.Role == EntityFileRole.GridThumbnail));
    }

    [Fact]
    public async Task EnsureSkipsKindsThatPreserveOriginalArtwork() {
        await using var db = CreateContext();
        var entityId = Guid.NewGuid();
        SeedEntity(db, entityId, EntityKind.Studio);
        var logoUrl = $"/assets/plugins/artwork/{entityId}/logo.png";
        WriteImage(_assets.ResolveAssetDiskPath(logoUrl)!, 600, 240);
        AddEntityFile(db, entityId, EntityFileRole.Logo, logoUrl, FileSourceKind.Custom.ToCode());
        await db.SaveChangesAsync();

        await CreateService(db).EnsureAsync(entityId, CancellationToken.None);

        Assert.False(File.Exists(_assets.GridThumbnailPath(entityId)));
        Assert.False(File.Exists(_assets.GridThumbnail2xPath(entityId)));
        Assert.DoesNotContain(
            db.EntityFiles,
            file => file.EntityId == entityId &&
                (file.Role == EntityFileRole.GridThumbnail || file.Role == EntityFileRole.GridThumbnail2x));
    }

    [Fact]
    public async Task RefreshSkipsKindsThatPreserveOriginalArtwork() {
        await using var db = CreateContext();
        var entityId = Guid.NewGuid();
        SeedEntity(db, entityId, EntityKind.Studio);
        var coverUrl = AssetPathService.VideoThumbnailUrl(entityId);
        WriteImage(_assets.ResolveAssetDiskPath(coverUrl)!, 1280, 720);
        AddEntityFile(db, entityId, EntityFileRole.Thumbnail, coverUrl, FileSourceKind.Scan.ToCode());
        AddEntityFile(db, entityId, EntityFileRole.GridThumbnail, $"/assets/grid-thumbs/{entityId}.jpg", FileSourceKind.Scan.ToCode());
        AddEntityFile(db, entityId, EntityFileRole.GridThumbnail2x, $"/assets/grid-thumbs/{entityId}@2x.jpg", FileSourceKind.Scan.ToCode());
        WriteImage(_assets.GridThumbnailPath(entityId), 480, 270);
        WriteImage(_assets.GridThumbnail2xPath(entityId), 960, 540);
        await db.SaveChangesAsync();

        var needed = await CreateService(db).ListEntitiesNeedingRefreshAsync(CancellationToken.None);

        Assert.DoesNotContain(entityId, needed);
    }

    public void Dispose() {
        if (Directory.Exists(_dataDir)) {
            Directory.Delete(_dataDir, recursive: true);
        }
    }

    private static IImageThumbnailGenerator Resizer() =>
        new ImageThumbnailGenerator(new SkiaImageDownscaler(), new ThumbnailService(new ProcessExecutor()));

    private GridThumbnailService CreateService(PrismediaDbContext db) =>
        new(db, _assets, Resizer());

    private static PrismediaDbContext CreateContext() {
        var options = new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase($"grid-thumb-{Guid.NewGuid():N}")
            .Options;
        return new PrismediaDbContext(options);
    }

    private static void SeedEntity(PrismediaDbContext db, Guid id, EntityKind kind = EntityKind.Video) {
        var now = DateTimeOffset.UtcNow;
        db.Entities.Add(new EntityRow { Id = id, KindCode = kind.ToCode(), Title = "Video", CreatedAt = now, UpdatedAt = now });
    }

    private static void AddEntityFile(PrismediaDbContext db, Guid entityId, EntityFileRole role, string path, string source) {
        var now = DateTimeOffset.UtcNow;
        db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(),
            EntityId = entityId,
            Role = role,
            Path = path,
            MimeType = "image/jpeg",
            Source = source,
            CreatedAt = now,
            UpdatedAt = now
        });
    }

    private static void WriteImage(string path, int width, int height) {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap)) {
            canvas.Clear(SKColors.SlateGray);
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        using var output = File.Create(path);
        data.SaveTo(output);
    }

}
