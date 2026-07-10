using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Entities;
using Prismedia.Contracts.Entities;
using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Entities;
using Prismedia.Infrastructure.Entities.Mappers;
using Prismedia.Infrastructure.Entities.Thumbnails;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

/// <summary>
/// Locks the shared definition of source ownership across list, thumbnail, and detail reads. The
/// hierarchy deliberately mixes media kinds so the behavior cannot depend on a TV, book, or music
/// special case.
/// </summary>
public sealed class EfEntitySourceOwnershipReadTests {
    [Fact]
    public async Task PostgreSqlFilterKeepsTheRecursiveOwnershipProjectionServerSide() {
        await using var db = new PrismediaDbContext(
            new DbContextOptionsBuilder<PrismediaDbContext>()
                .UseNpgsql("Host=localhost;Database=prismedia;Username=prismedia;Password=prismedia")
                .Options);
        var projection = new EfEntitySourceOwnershipProjection(db);

        var filtered = await projection.ApplyFilterAsync(
            db.Entities.AsNoTracking(),
            hasSourceMedia: true,
            CancellationToken.None);
        var sql = filtered.ToQueryString();

        Assert.Contains("WITH RECURSIVE source_backed", sql, StringComparison.Ordinal);
        Assert.Contains("child.parent_entity_id = parent.id", sql, StringComparison.Ordinal);
        Assert.Contains("file.entity_id = entity.id", sql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DescendantSourceOwnershipFlowsThroughFiltersThumbnailsAndDetails() {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var movie = AddHierarchy(db, now, EntityKind.Movie, EntityKind.Video);
        var series = AddHierarchy(db, now, EntityKind.VideoSeries, EntityKind.VideoSeason, EntityKind.Video);
        var book = AddHierarchy(db, now, EntityKind.Book, EntityKind.BookVolume, EntityKind.BookChapter);
        var artist = AddHierarchy(db, now, EntityKind.MusicArtist, EntityKind.AudioLibrary, EntityKind.AudioTrack);
        var album = Assert.Single(db.Entities.Local, row => row.ParentEntityId == artist.RootId);
        var directImage = AddHierarchy(db, now, EntityKind.Image);
        var filelessMovie = AddFileless(db, now, EntityKind.Movie);
        var filelessSeries = AddFileless(db, now, EntityKind.VideoSeries);
        var filelessBook = AddFileless(db, now, EntityKind.Book);
        var filelessAlbum = AddFileless(db, now, EntityKind.AudioLibrary);
        var filelessImage = AddFileless(db, now, EntityKind.Image);
        db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(),
            EntityId = filelessMovie,
            Role = EntityFileRole.Cover,
            Path = "/assets/movies/fileless/cover.jpg",
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();

        var service = CreateService(db);

        await AssertFilterAsync(service, EntityKind.Movie, movie.RootId, filelessMovie);
        await AssertFilterAsync(service, EntityKind.VideoSeries, series.RootId, filelessSeries);
        await AssertFilterAsync(service, EntityKind.Book, book.RootId, filelessBook);
        await AssertFilterAsync(service, EntityKind.AudioLibrary, album.Id, filelessAlbum);
        await AssertFilterAsync(service, EntityKind.Image, directImage.RootId, filelessImage);

        var sourceBackedIds = new[] {
            movie.RootId,
            series.RootId,
            book.RootId,
            artist.RootId,
            album.Id,
            directImage.RootId,
        };
        var thumbnails = await service.GetThumbnailsAsync(
            [.. sourceBackedIds, filelessMovie],
            hideNsfw: false,
            CancellationToken.None);
        Assert.All(
            thumbnails.Items.Where(item => sourceBackedIds.Contains(item.Id)),
            thumbnail => Assert.True(thumbnail.HasSourceMedia));
        Assert.False(Assert.Single(thumbnails.Items, item => item.Id == filelessMovie).HasSourceMedia);

        foreach (var id in sourceBackedIds) {
            var kindCode = Assert.Single(db.Entities.Local, row => row.Id == id).KindCode;
            var detail = Assert.IsAssignableFrom<IEntityCard>(
                await service.GetDetailAsync(id, kindCode, hideNsfw: false, CancellationToken.None));
            Assert.True(Assert.Single(detail.Capabilities.OfType<FileManagementCapability>()).CanDeleteFiles);
        }

        var filelessDetail = Assert.IsAssignableFrom<IEntityCard>(
            await service.GetDetailAsync(
                filelessMovie,
                EntityKindRegistry.Movie.Code,
                hideNsfw: false,
                CancellationToken.None));
        Assert.Empty(filelessDetail.Capabilities.OfType<FileManagementCapability>());
    }

    [Fact]
    public async Task FilelessDeletionClaimsAndMonitorsRetainTheSharedRetryCapability() {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var claimedMovieId = AddFileless(db, now, EntityKind.Movie, "Claimed movie");
        var monitoredBookId = AddFileless(db, now, EntityKind.Book, "Monitored book");
        var ordinaryWantedId = AddFileless(db, now, EntityKind.Movie, "Ordinary wanted movie");

        var claimedMovie = Assert.Single(db.Entities.Local, row => row.Id == claimedMovieId);
        claimedMovie.IsWanted = true;
        claimedMovie.LifecycleClaimKind = EntityLifecycleClaimKind.DeletingFiles;
        claimedMovie.LifecycleClaimId = Guid.NewGuid();
        claimedMovie.LifecycleClaimedAt = now;

        Assert.Single(db.Entities.Local, row => row.Id == monitoredBookId).IsWanted = true;
        Assert.Single(db.Entities.Local, row => row.Id == ordinaryWantedId).IsWanted = true;
        db.Monitors.Add(new MonitorRow {
            Id = Guid.NewGuid(),
            EntityId = monitoredBookId,
            Kind = EntityKind.Book,
            Status = MonitorStatus.DeletingFiles,
            Title = "Monitored book",
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var claimedMovieDetail = Assert.IsAssignableFrom<IEntityCard>(await service.GetDetailAsync(
            claimedMovieId,
            EntityKindRegistry.Movie.Code,
            hideNsfw: false,
            CancellationToken.None));
        var monitoredBookDetail = Assert.IsAssignableFrom<IEntityCard>(await service.GetDetailAsync(
            monitoredBookId,
            EntityKindRegistry.Book.Code,
            hideNsfw: false,
            CancellationToken.None));
        var ordinaryWantedDetail = Assert.IsAssignableFrom<IEntityCard>(await service.GetDetailAsync(
            ordinaryWantedId,
            EntityKindRegistry.Movie.Code,
            hideNsfw: false,
            CancellationToken.None));

        Assert.False(claimedMovieDetail.HasSourceMedia);
        Assert.True(Assert.Single(claimedMovieDetail.Capabilities.OfType<FileManagementCapability>()).CanDeleteFiles);
        Assert.False(monitoredBookDetail.HasSourceMedia);
        Assert.True(Assert.Single(monitoredBookDetail.Capabilities.OfType<FileManagementCapability>()).CanDeleteFiles);
        Assert.Empty(ordinaryWantedDetail.Capabilities.OfType<FileManagementCapability>());
    }

    [Fact]
    public async Task HasFileFalseFiltersSourceBackedParentsBeforePaging() {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        _ = AddHierarchy(
            db,
            now,
            EntityKind.Gallery,
            [EntityKind.Gallery, EntityKind.Image],
            title: "Alpha backed");
        var fileless = AddFileless(db, now, EntityKind.Gallery, title: "Bravo fileless");
        _ = AddHierarchy(
            db,
            now,
            EntityKind.Gallery,
            [EntityKind.Image],
            title: "Charlie backed");
        await db.SaveChangesAsync();

        var result = await CreateService(db).ListAsync(
            EntityKindRegistry.Gallery.Code,
            query: null,
            cursor: null,
            hideNsfw: false,
            limit: 1,
            CancellationToken.None,
            hasFile: false);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal(fileless, Assert.Single(result.Items).Id);
        Assert.Null(result.NextCursor);
    }

    [Fact]
    public async Task DetailAndCapabilityMutationsShareSourceBackedCapabilityTruth() {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var movie = AddHierarchy(db, now, EntityKind.Movie, EntityKind.Video);
        await db.SaveChangesAsync();

        var user = TestUserContext.Admin();
        var repository = new EfEntityRepository(
            db,
            user,
            EntityMappers.Kinds(db),
            EntityMappers.Capabilities(db, user));
        var sourceOwnership = new EfEntitySourceOwnershipProjection(db);
        var reads = new EfEntityReadService(
            db,
            user,
            repository,
            EntityMappers.Kinds(db),
            ThumbnailContributors.For(db),
            sourceOwnership: sourceOwnership);
        var mutations = new EntityCapabilityService(repository, sourceOwnership);

        var detail = Assert.IsAssignableFrom<IEntityCard>(await reads.GetDetailAsync(
            movie.RootId,
            EntityKindRegistry.Movie.Code,
            hideNsfw: false,
            CancellationToken.None));
        var rated = Assert.IsType<EntityCard>(await mutations.RateAsync(
            movie.RootId, 4, CancellationToken.None));
        var flagged = Assert.IsType<EntityCard>(await mutations.UpdateFlagsAsync(
            movie.RootId,
            isFavorite: true,
            isNsfw: null,
            isOrganized: null,
            CancellationToken.None));
        var progressed = Assert.IsType<EntityCard>(await mutations.UpdateProgressAsync(
            movie.RootId,
            movie.RootId,
            ProgressUnit.Page,
            index: 1,
            total: 10,
            mode: ReaderMode.Paged,
            completed: null,
            reset: false,
            location: null,
            CancellationToken.None));

        foreach (var card in new[] { detail, rated, flagged, progressed }) {
            Assert.True(Assert.Single(card.Capabilities.OfType<FileManagementCapability>()).CanDeleteFiles);
        }
    }

    private static async Task AssertFilterAsync(
        EfEntityReadService service,
        EntityKind kind,
        Guid sourceBackedId,
        Guid filelessId) {
        var code = EntityKindRegistry.ToCode(kind);
        var withFile = await service.ListAsync(
            code, null, null, false, null, CancellationToken.None, hasFile: true);
        var withoutFile = await service.ListAsync(
            code, null, null, false, null, CancellationToken.None, hasFile: false);

        Assert.Contains(withFile.Items, item => item.Id == sourceBackedId);
        Assert.DoesNotContain(withFile.Items, item => item.Id == filelessId);
        Assert.Contains(withoutFile.Items, item => item.Id == filelessId);
        Assert.DoesNotContain(withoutFile.Items, item => item.Id == sourceBackedId);
    }

    private static (Guid RootId, Guid SourceOwnerId) AddHierarchy(
        PrismediaDbContext db,
        DateTimeOffset now,
        EntityKind rootKind,
        params EntityKind[] descendantKinds) =>
        AddHierarchy(db, now, rootKind, descendantKinds, title: null);

    private static (Guid RootId, Guid SourceOwnerId) AddHierarchy(
        PrismediaDbContext db,
        DateTimeOffset now,
        EntityKind rootKind,
        EntityKind[] descendantKinds,
        string? title) {
        var rootId = Guid.NewGuid();
        db.Entities.Add(Row(rootId, rootKind, title ?? $"Backed {rootKind}", parentId: null, now));
        var parentId = rootId;
        foreach (var kind in descendantKinds) {
            var childId = Guid.NewGuid();
            db.Entities.Add(Row(childId, kind, $"Child {kind}", parentId, now));
            parentId = childId;
        }

        db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(),
            EntityId = parentId,
            Role = EntityFileRole.Source,
            Path = $"/media/{parentId}",
            CreatedAt = now,
            UpdatedAt = now,
        });
        return (rootId, parentId);
    }

    private static Guid AddFileless(
        PrismediaDbContext db,
        DateTimeOffset now,
        EntityKind kind,
        string? title = null) {
        var id = Guid.NewGuid();
        db.Entities.Add(Row(id, kind, title ?? $"Fileless {kind}", parentId: null, now));
        return id;
    }

    private static EntityRow Row(
        Guid id,
        EntityKind kind,
        string title,
        Guid? parentId,
        DateTimeOffset now) =>
        new() {
            Id = id,
            KindCode = EntityKindRegistry.ToCode(kind),
            Title = title,
            ParentEntityId = parentId,
            CreatedAt = now,
            UpdatedAt = now,
        };

    private static EfEntityReadService CreateService(PrismediaDbContext db) {
        var user = TestUserContext.Admin();
        var repository = new EfEntityRepository(
            db,
            user,
            EntityMappers.Kinds(db),
            EntityMappers.Capabilities(db, user));
        return new EfEntityReadService(
            db,
            user,
            repository,
            EntityMappers.Kinds(db),
            ThumbnailContributors.For(db));
    }

    private static PrismediaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
