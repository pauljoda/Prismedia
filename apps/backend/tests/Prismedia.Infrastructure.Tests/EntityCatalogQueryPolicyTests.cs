using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class EntityCatalogQueryPolicyTests {
    [Fact]
    public async Task ArchivedLibraryMetadataRemainsAddressableButLeavesEveryCatalogSurface() {
        await using var db = CreateContext();
        var archived = Row(Guid.NewGuid(), EntityKind.Movie, DateTimeOffset.UtcNow);
        archived.IsLibraryArchived = true;
        var available = Row(Guid.NewGuid(), EntityKind.Movie, DateTimeOffset.UtcNow);
        db.Entities.AddRange(archived, available);
        await db.SaveChangesAsync();

        foreach (var surface in new[] { EntityCatalogSurface.KindBrowse, EntityCatalogSurface.Discovery,
                     EntityCatalogSurface.Collection, EntityCatalogSurface.Statistics }) {
            var all = db.Entities.AsNoTracking();
            var ids = await EntityCatalogQueryPolicy.Apply(all, all, surface).Select(entity => entity.Id).ToArrayAsync();
            Assert.Equal([available.Id], ids);
        }
        Assert.NotNull(await db.Entities.FindAsync(archived.Id));
        var history = await EntityCatalogQueryPolicy.Apply(db.Entities, db.Entities,
            EntityCatalogSurface.Statistics, includeArchived: true).Select(entity => entity.Id).ToArrayAsync();
        Assert.Contains(archived.Id, history);
    }

    [Fact]
    public async Task PlansApplyTheDeclaredSurfaceMatrixWithoutRouteSpecificKindBranches() {
        await using var db = CreateContext();
        var bookId = Guid.NewGuid();
        var galleryId = Guid.NewGuid();
        var nestedGalleryId = Guid.NewGuid();
        var audiobookTrackId = Guid.NewGuid();
        var musicTrackId = Guid.NewGuid();
        var audioLibraryId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.Entities.AddRange(
            Row(bookId, EntityKind.Book, now),
            Row(galleryId, EntityKind.Gallery, now),
            Row(nestedGalleryId, EntityKind.Gallery, now, galleryId),
            Row(audiobookTrackId, EntityKind.AudioTrack, now, bookId),
            Row(audioLibraryId, EntityKind.AudioLibrary, now),
            Row(musicTrackId, EntityKind.AudioTrack, now, audioLibraryId));
        await db.SaveChangesAsync();

        var all = db.Entities.AsNoTracking();
        var typedTracks = await EntityCatalogQueryPolicy.Apply(
                all,
                all,
                EntityCatalogSurface.KindBrowse,
                [EntityKind.AudioTrack.ToCode()])
            .Where(entity => entity.KindCode == EntityKind.AudioTrack.ToCode())
            .Select(entity => entity.Id)
            .ToArrayAsync();
        var typedBooks = await EntityCatalogQueryPolicy.Apply(
                all,
                all,
                EntityCatalogSurface.KindBrowse,
                [EntityKind.Book.ToCode()])
            .Where(entity => entity.KindCode == EntityKind.Book.ToCode())
            .Select(entity => entity.Id)
            .ToArrayAsync();
        var typedGalleries = await EntityCatalogQueryPolicy.Apply(
                all,
                all,
                EntityCatalogSurface.KindBrowse,
                [EntityKind.Gallery.ToCode()])
            .Where(entity => entity.KindCode == EntityKind.Gallery.ToCode())
            .Select(entity => entity.Id)
            .ToArrayAsync();
        var discovery = await EntityCatalogQueryPolicy.Apply(
                all,
                all,
                EntityCatalogSurface.Discovery)
            .Select(entity => entity.Id)
            .ToArrayAsync();

        Assert.Equal([musicTrackId], typedTracks);
        Assert.Equal([bookId], typedBooks);
        Assert.Equal([galleryId], typedGalleries);
        Assert.DoesNotContain(audiobookTrackId, discovery);
        Assert.Contains(nestedGalleryId, discovery);
    }

    [Fact]
    public void CachedPlansExposeDefinitionOwnedQueryShapes() {
        var trackPlan = EntityCatalogQueryPolicy.PlanFor(
            EntityCatalogSurface.Collection,
            EntityKind.AudioTrack.ToCode());
        var bookPlan = EntityCatalogQueryPolicy.PlanFor(
            EntityCatalogSurface.KindBrowse,
            EntityKind.Book.ToCode());
        var galleryPlan = EntityCatalogQueryPolicy.PlanFor(
            EntityCatalogSurface.KindBrowse,
            EntityKind.Gallery.ToCode());

        Assert.Equal(EntityKind.AudioTrack.ToCode(), trackPlan.KindCode);
        Assert.Equal([EntityKind.Book.ToCode()], trackPlan.HiddenParentKindCodes);
        Assert.Empty(bookPlan.HiddenParentKindCodes);
        Assert.True(galleryPlan.RequiresTopLevel);
    }

    private static PrismediaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static EntityRow Row(Guid id, EntityKind kind, DateTimeOffset now, Guid? parentId = null) =>
        new() {
            Id = id,
            KindCode = kind.ToCode(),
            Title = kind.ToCode(),
            ParentEntityId = parentId,
            CreatedAt = now,
            UpdatedAt = now
        };
}
