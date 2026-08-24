using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Media;
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

    [Fact]
    public async Task EnsureExtractsComicCoverPageAndRollsItUpThroughVolumeAndSeries() {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow.AddMinutes(-5);
        var seriesId = Guid.NewGuid();
        var volumeId = Guid.NewGuid();
        var installmentId = Guid.NewGuid();
        SeedEntity(db, seriesId, EntityKind.ComicSeries, now: now);
        SeedEntity(db, volumeId, EntityKind.ComicVolume, seriesId, now: now);
        SeedEntity(db, installmentId, EntityKind.ComicInstallment, volumeId, now: now);

        var pagePath = Path.Combine(_dataDir, "cover.jpg");
        WriteImage(pagePath, 600, 900, SKColors.CornflowerBlue);
        var archivePath = Path.Combine(_dataDir, "issue.cbz");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create)) {
            archive.CreateEntryFromFile(pagePath, "pages/cover.jpg");
        }
        File.Delete(pagePath);

        AddEntityFile(db, installmentId, EntityFileRole.Source, archivePath, FileSourceKind.Scan.ToCode());
        db.EntityPageManifests.Add(new EntityPageManifestRow {
            EntityId = installmentId,
            Direction = PageReadingDirection.LeftToRight,
            DefaultMode = ReaderMode.Paged,
            CoverOrdinal = 0,
            SourceSignature = "comic-source",
            UpdatedAt = now.AddMinutes(1)
        });
        db.EntityPageEntries.Add(new EntityPageEntryRow {
            EntityId = installmentId,
            Ordinal = 0,
            ArchiveMember = "pages/cover.jpg",
            MimeType = "image/jpeg",
            PageType = PageType.FrontCover
        });
        await db.SaveChangesAsync();

        var service = CreateService(db);
        Assert.Equal([seriesId], await service.ListEntitiesNeedingRefreshAsync(CancellationToken.None));

        await service.EnsureAsync(seriesId, CancellationToken.None);

        foreach (var entityId in new[] { installmentId, volumeId, seriesId }) {
            Assert.True(File.Exists(_assets.GridThumbnailPath(entityId)));
            Assert.True(File.Exists(_assets.GridThumbnail2xPath(entityId)));
            Assert.Contains(db.EntityFiles, file =>
                file.EntityId == entityId && file.Role == EntityFileRole.GridThumbnail);
            Assert.Contains(db.EntityFiles, file =>
                file.EntityId == entityId && file.Role == EntityFileRole.GridThumbnail2x);
        }
        using var seriesImage = SKBitmap.Decode(_assets.GridThumbnailPath(seriesId));
        Assert.NotNull(seriesImage);
        Assert.Equal((480, 720), (seriesImage!.Width, seriesImage.Height));
        Assert.Empty(await service.ListEntitiesNeedingRefreshAsync(CancellationToken.None));
    }

    [Fact]
    public async Task EnsureComposesFourChildCoversIntoArtistThumbnail() {
        await using var db = CreateContext();
        var artistId = Guid.NewGuid();
        SeedEntity(db, artistId, EntityKind.MusicArtist);
        var colors = new[] { SKColors.Red, SKColors.Green, SKColors.Blue, SKColors.Gold };
        for (var index = 0; index < colors.Length; index++) {
            var albumId = Guid.NewGuid();
            SeedEntity(db, albumId, EntityKind.AudioLibrary, artistId, index);
            var coverUrl = $"/assets/custom/artwork/{albumId}/cover.jpg";
            WriteImage(_assets.ResolveAssetDiskPath(coverUrl)!, 600, 600, colors[index]);
            AddEntityFile(db, albumId, EntityFileRole.Cover, coverUrl, FileSourceKind.Custom.ToCode());
        }
        await db.SaveChangesAsync();

        await CreateService(db).EnsureAsync(artistId, CancellationToken.None);

        using var collage = SKBitmap.Decode(_assets.GridThumbnailPath(artistId));
        Assert.NotNull(collage);
        Assert.Equal((480, 480), (collage!.Width, collage.Height));
        Assert.True(collage.GetPixel(60, 60).Red > collage.GetPixel(60, 60).Blue);
        Assert.True(collage.GetPixel(420, 60).Green > collage.GetPixel(420, 60).Red);
        Assert.True(collage.GetPixel(60, 420).Blue > collage.GetPixel(60, 420).Red);
        Assert.True(collage.GetPixel(420, 420).Red > collage.GetPixel(420, 420).Blue);
    }

    [Fact]
    public async Task EnsureRemovesGeneratedVariantsWhenThumbnailChainHasNoArtwork() {
        await using var db = CreateContext();
        var artistId = Guid.NewGuid();
        SeedEntity(db, artistId, EntityKind.MusicArtist);
        AddEntityFile(db, artistId, EntityFileRole.GridThumbnail, AssetPathService.GridThumbnailUrl(artistId), FileSourceKind.Scan.ToCode());
        AddEntityFile(db, artistId, EntityFileRole.GridThumbnail2x, AssetPathService.GridThumbnail2xUrl(artistId), FileSourceKind.Scan.ToCode());
        WriteImage(_assets.GridThumbnailPath(artistId), 480, 480);
        WriteImage(_assets.GridThumbnail2xPath(artistId), 960, 960);
        await db.SaveChangesAsync();

        await CreateService(db).EnsureAsync(artistId, CancellationToken.None);

        Assert.DoesNotContain(db.EntityFiles, file =>
            file.EntityId == artistId &&
            file.Role is EntityFileRole.GridThumbnail or EntityFileRole.GridThumbnail2x);
        Assert.False(File.Exists(_assets.GridThumbnailPath(artistId)));
        Assert.False(File.Exists(_assets.GridThumbnail2xPath(artistId)));
    }

    public void Dispose() {
        if (Directory.Exists(_dataDir)) {
            Directory.Delete(_dataDir, recursive: true);
        }
    }

    private static IImageThumbnailGenerator Resizer() =>
        new ImageThumbnailGenerator(new SkiaImageDownscaler(), new ThumbnailService(new ProcessExecutor()));

    private GridThumbnailService CreateService(PrismediaDbContext db) =>
        new(db, _assets, Resizer(), new SkiaThumbnailCollageComposer());

    private static PrismediaDbContext CreateContext() {
        var options = new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase($"grid-thumb-{Guid.NewGuid():N}")
            .Options;
        return new PrismediaDbContext(options);
    }

    private static void SeedEntity(
        PrismediaDbContext db,
        Guid id,
        EntityKind kind = EntityKind.Video,
        Guid? parentEntityId = null,
        int? sortOrder = null,
        DateTimeOffset? now = null) {
        var timestamp = now ?? DateTimeOffset.UtcNow;
        db.Entities.Add(new EntityRow {
            Id = id,
            KindCode = kind.ToCode(),
            Title = kind.ToString(),
            ParentEntityId = parentEntityId,
            SortOrder = sortOrder,
            CreatedAt = timestamp,
            UpdatedAt = timestamp
        });
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

    private static void WriteImage(string path, int width, int height, SKColor? color = null) {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap)) {
            canvas.Clear(color ?? SKColors.SlateGray);
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        using var output = File.Create(path);
        data.SaveTo(output);
    }

}
