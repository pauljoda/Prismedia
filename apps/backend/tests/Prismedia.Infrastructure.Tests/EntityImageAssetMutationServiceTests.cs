using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Entities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class EntityImageAssetMutationServiceTests : IDisposable {
    private readonly string _cacheRoot = Path.Combine(Path.GetTempPath(), $"prismedia-assets-{Guid.NewGuid():N}");

    [Fact]
    public async Task UploadImageAssetUpsertsCustomEntityFileUnderServedAssetsRoot() {
        await using var db = CreateContext();
        var entityId = Guid.Parse("16161616-1616-1616-1616-161616161616");
        SeedEntity(db, entityId, EntityKindRegistry.Video.Code, "Video");
        await db.SaveChangesAsync();
        var service = new EntityImageAssetMutationService(db, new EntityImageAssetStorageOptions(_cacheRoot));

        await using var content = new MemoryStream([0x89, 0x50, 0x4e, 0x47]);
        var result = await service.UploadAsync(entityId, "poster", "poster.png", "image/png", content, CancellationToken.None);

        Assert.Equal(EntityImageAssetMutationResult.Updated, result);
        var row = await db.EntityFiles.SingleAsync(file => file.EntityId == entityId && file.Role == EntityFileRole.Poster);
        Assert.Equal("custom", row.Source);
        Assert.Equal("image/png", row.MimeType);
        Assert.StartsWith("/assets/custom/artwork/16161616-1616-1616-1616-161616161616/poster-", row.Path, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(_cacheRoot, row.Path["/assets/".Length..])));
    }

    [Fact]
    public async Task ClearImageAssetRemovesOnlyRequestedCustomRole() {
        await using var db = CreateContext();
        var entityId = Guid.Parse("17171717-1717-1717-1717-171717171717");
        var now = DateTimeOffset.UtcNow;
        SeedEntity(db, entityId, EntityKindRegistry.Video.Code, "Video");
        db.EntityFiles.AddRange(
            new EntityFileRow {
                Id = Guid.NewGuid(),
                EntityId = entityId,
                Role = EntityFileRole.Poster,
                Path = "/assets/custom/artwork/17171717-1717-1717-1717-171717171717/poster-old.png",
                Source = "custom",
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityFileRow {
                Id = Guid.NewGuid(),
                EntityId = entityId,
                Role = EntityFileRole.Thumbnail,
                Path = "/assets/videos/17171717/thumb.jpg",
                Source = "scan",
                CreatedAt = now,
                UpdatedAt = now
            });
        await db.SaveChangesAsync();
        var service = new EntityImageAssetMutationService(db, new EntityImageAssetStorageOptions(_cacheRoot));

        var result = await service.ClearAsync(entityId, "poster", CancellationToken.None);

        Assert.Equal(EntityImageAssetMutationResult.Updated, result);
        Assert.Null(await db.EntityFiles.FirstOrDefaultAsync(file => file.EntityId == entityId && file.Role == EntityFileRole.Poster));
        Assert.NotNull(await db.EntityFiles.FirstOrDefaultAsync(file => file.EntityId == entityId && file.Role == EntityFileRole.Thumbnail));
    }

    public void Dispose() {
        if (Directory.Exists(_cacheRoot)) {
            Directory.Delete(_cacheRoot, recursive: true);
        }
    }

    private static PrismediaDbContext CreateContext() {
        var options = new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase($"entity-image-assets-{Guid.NewGuid():N}")
            .Options;
        return new PrismediaDbContext(options);
    }

    private static void SeedEntity(PrismediaDbContext db, Guid id, string kind, string title) {
        var now = DateTimeOffset.UtcNow;
        db.Entities.Add(new EntityRow {
            Id = id,
            KindCode = kind,
            Title = title,
            CreatedAt = now,
            UpdatedAt = now
        });
    }
}
