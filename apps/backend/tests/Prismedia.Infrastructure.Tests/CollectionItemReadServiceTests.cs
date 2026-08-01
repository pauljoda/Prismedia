using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Entities;
using Prismedia.Application.Security;
using Prismedia.Contracts.Entities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Collections;
using Prismedia.Infrastructure.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class CollectionItemReadServiceTests {
    [Fact]
    public async Task ListMembershipOptionsAsyncReturnsOnlyOwnedMutableVisibleCollections() {
        await using var db = CreateContext();
        var ownerId = Guid.Parse("11111111-1111-4111-8111-111111111111");
        var otherOwnerId = Guid.Parse("22222222-2222-4222-8222-222222222222");
        var manualId = SeedCollection(db, "Zulu", ownerId, CollectionMode.Manual);
        var hybridId = SeedCollection(db, "Alpha", ownerId, CollectionMode.Hybrid);
        _ = SeedCollection(db, "Rules", ownerId, CollectionMode.Dynamic);
        _ = SeedCollection(db, "Shared", otherOwnerId, CollectionMode.Manual, isShared: true);
        _ = SeedCollection(db, "Private", otherOwnerId, CollectionMode.Manual);
        var nsfwId = SeedCollection(db, "Hidden", ownerId, CollectionMode.Manual, isNsfw: true);
        await db.SaveChangesAsync();

        var service = CreateService(db, TestUserContext.MemberAs(ownerId));

        var safe = await service.ListMembershipOptionsAsync(hideNsfw: true, CancellationToken.None);
        Assert.Equal([hybridId, manualId], safe.Items.Select(item => item.Id));

        var all = await service.ListMembershipOptionsAsync(hideNsfw: false, CancellationToken.None);
        Assert.Equal([hybridId, nsfwId, manualId], all.Items.Select(item => item.Id));
    }

    [Fact]
    public async Task ListItemsAsyncReturnsOrderedVisibleCollectionItems() {
        await using var db = CreateContext();
        var collectionId = Guid.NewGuid();
        var firstId = Guid.NewGuid();
        var hiddenId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var bookId = Guid.NewGuid();
        var audiobookTrackId = Guid.NewGuid();

        SeedEntity(db, collectionId, EntityKind.Collection.ToCode(), "Favorites");
        SeedEntity(db, firstId, EntityKind.Video.ToCode(), "First");
        SeedEntity(db, hiddenId, EntityKind.Image.ToCode(), "Hidden", isNsfw: true);
        SeedEntity(db, secondId, EntityKind.AudioTrack.ToCode(), "Second");
        SeedEntity(db, bookId, EntityKind.Book.ToCode(), "Spoken Story");
        SeedEntity(db, audiobookTrackId, EntityKind.AudioTrack.ToCode(), "Book Chapter", parentEntityId: bookId);
        db.CollectionDetails.Add(new CollectionDetailRow {
            EntityId = collectionId,
            OwnerUserId = TestUserContext.UserId,
        });
        db.CollectionItemDetails.AddRange(
            Item(collectionId, audiobookTrackId, 30),
            Item(collectionId, secondId, 20),
            Item(collectionId, hiddenId, 10),
            Item(collectionId, firstId, 0));
        await db.SaveChangesAsync();

        var service = CreateService(db, TestUserContext.Admin());

        var result = await service.ListItemsAsync(collectionId, hideNsfw: true, CancellationToken.None);

        Assert.Collection(result.Items,
            first => {
                Assert.Equal(firstId, first.EntityId);
                Assert.Equal(EntityKind.Video, first.EntityType);
                Assert.Equal(CollectionItemSource.Manual, first.Source);
                Assert.Equal("First", first.Entity.Title);
            },
            second => {
                Assert.Equal(secondId, second.EntityId);
                Assert.Equal(EntityKind.AudioTrack, second.EntityType);
                Assert.Equal("Second", second.Entity.Title);
            });
    }

    [Fact]
    public async Task ListItemsAsyncHidesAnotherUsersPrivateCollectionButAllowsSharedCollection() {
        await using var db = CreateContext();
        var ownerUserId = Guid.Parse("11111111-1111-4111-8111-111111111111");
        var viewerUserId = Guid.Parse("22222222-2222-4222-8222-222222222222");
        var collectionId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        SeedEntity(db, collectionId, EntityKind.Collection.ToCode(), "Scoped");
        SeedEntity(db, itemId, EntityKind.Video.ToCode(), "Item");
        db.CollectionDetails.Add(new CollectionDetailRow {
            EntityId = collectionId,
            OwnerUserId = ownerUserId,
            IsShared = false,
        });
        db.CollectionItemDetails.Add(Item(collectionId, itemId, 0));
        await db.SaveChangesAsync();
        var service = CreateService(db, TestUserContext.MemberAs(viewerUserId));

        var hidden = await service.ListItemsAsync(collectionId, hideNsfw: false, CancellationToken.None);
        Assert.Empty(hidden.Items);

        (await db.CollectionDetails.SingleAsync()).IsShared = true;
        await db.SaveChangesAsync();

        var visible = await service.ListItemsAsync(collectionId, hideNsfw: false, CancellationToken.None);
        Assert.Equal(itemId, Assert.Single(visible.Items).EntityId);
    }

    [Fact]
    public async Task GetListContextsAsyncDetectsAudioThroughDiscoveredQualityFamily() {
        await using var db = CreateContext();
        var collectionId = Guid.NewGuid();
        var audioLibraryId = Guid.NewGuid();
        var videoId = Guid.NewGuid();
        SeedEntity(db, collectionId, EntityKind.Collection.ToCode(), "Mixed media");
        SeedEntity(db, audioLibraryId, EntityKind.AudioLibrary.ToCode(), "Album");
        SeedEntity(db, videoId, EntityKind.Video.ToCode(), "Movie");
        db.CollectionItemDetails.AddRange(
            Item(collectionId, audioLibraryId, 0),
            Item(collectionId, videoId, 1));
        await db.SaveChangesAsync();

        var service = CreateService(db, TestUserContext.Admin());

        var contexts = await service.GetListContextsAsync([collectionId], hideNsfw: false, CancellationToken.None);

        var context = Assert.Single(contexts).Value;
        Assert.Equal(2, context.ChildCount);
        Assert.True(context.HasAudio);
    }

    [Fact]
    public async Task ListItemsAndContextsExcludeMembersOutsideViewersLibraries() {
        await using var db = CreateContext();
        var collectionId = Guid.NewGuid();
        var visibleRootId = Guid.NewGuid();
        var restrictedRootId = Guid.NewGuid();
        var visibleVideoId = Guid.NewGuid();
        var restrictedAudioId = Guid.NewGuid();
        SeedEntity(db, collectionId, EntityKind.Collection.ToCode(), "Scoped collection");
        SeedEntity(db, visibleVideoId, EntityKind.Video.ToCode(), "Visible video");
        SeedEntity(db, restrictedAudioId, EntityKind.AudioTrack.ToCode(), "Restricted audio");
        SeedRoot(db, visibleRootId, "Visible");
        SeedRoot(db, restrictedRootId, "Restricted");
        db.EntityLibraryRoots.AddRange(
            new EntityLibraryRootRow { EntityId = visibleVideoId, LibraryRootId = visibleRootId },
            new EntityLibraryRootRow { EntityId = restrictedAudioId, LibraryRootId = restrictedRootId });
        db.CollectionDetails.Add(new CollectionDetailRow {
            EntityId = collectionId,
            OwnerUserId = TestUserContext.UserId,
        });
        db.CollectionItemDetails.AddRange(
            Item(collectionId, visibleVideoId, 0),
            Item(collectionId, restrictedAudioId, 1));
        await db.SaveChangesAsync();

        var service = CreateService(db, TestUserContext.Member(visibleRootId));

        var items = await service.ListItemsAsync(collectionId, hideNsfw: false, CancellationToken.None);
        var contexts = await service.GetListContextsAsync([collectionId], hideNsfw: false, CancellationToken.None);

        Assert.Equal(visibleVideoId, Assert.Single(items.Items).EntityId);
        var context = Assert.Single(contexts).Value;
        Assert.Equal(1, context.ChildCount);
        Assert.False(context.HasAudio);
    }

    [Fact]
    public async Task ResolveCoverPathsAsyncFallsBackWhenConfiguredCoverIsNotCatalogEligible() {
        await using var db = CreateContext();
        var collectionId = Guid.NewGuid();
        var bookId = Guid.NewGuid();
        var hiddenConfiguredTrackId = Guid.NewGuid();
        var visibleMemberId = Guid.NewGuid();
        SeedEntity(db, collectionId, EntityKind.Collection.ToCode(), "Collection");
        SeedEntity(db, bookId, EntityKind.Book.ToCode(), "Book");
        SeedEntity(db, hiddenConfiguredTrackId, EntityKind.AudioTrack.ToCode(), "Audiobook", parentEntityId: bookId);
        SeedEntity(db, visibleMemberId, EntityKind.Video.ToCode(), "Fallback");
        db.CollectionDetails.Add(new CollectionDetailRow {
            EntityId = collectionId,
            OwnerUserId = TestUserContext.UserId,
            CoverItemEntityId = hiddenConfiguredTrackId
        });
        db.CollectionItemDetails.Add(Item(collectionId, visibleMemberId, 0));
        await db.SaveChangesAsync();

        var covers = await CreateService(db, TestUserContext.Admin())
            .ResolveCoverPathsAsync([collectionId], hideNsfw: false, CancellationToken.None);

        Assert.Equal($"/assets/test/{visibleMemberId:N}.jpg", Assert.Single(covers).Value);
    }

    [Fact]
    public async Task ResolveCoverPathsAsyncFallsBackWhenConfiguredCoverIsOutsideViewersLibraries() {
        await using var db = CreateContext();
        var collectionId = Guid.NewGuid();
        var visibleRootId = Guid.NewGuid();
        var restrictedRootId = Guid.NewGuid();
        var restrictedCoverId = Guid.NewGuid();
        var visibleMemberId = Guid.NewGuid();
        SeedEntity(db, collectionId, EntityKind.Collection.ToCode(), "Collection");
        SeedEntity(db, restrictedCoverId, EntityKind.Video.ToCode(), "Restricted cover");
        SeedEntity(db, visibleMemberId, EntityKind.Video.ToCode(), "Visible fallback");
        SeedRoot(db, visibleRootId, "Visible");
        SeedRoot(db, restrictedRootId, "Restricted");
        db.EntityLibraryRoots.AddRange(
            new EntityLibraryRootRow { EntityId = restrictedCoverId, LibraryRootId = restrictedRootId },
            new EntityLibraryRootRow { EntityId = visibleMemberId, LibraryRootId = visibleRootId });
        db.CollectionDetails.Add(new CollectionDetailRow {
            EntityId = collectionId,
            OwnerUserId = TestUserContext.UserId,
            CoverItemEntityId = restrictedCoverId,
        });
        db.CollectionItemDetails.AddRange(
            Item(collectionId, restrictedCoverId, 0),
            Item(collectionId, visibleMemberId, 1));
        await db.SaveChangesAsync();

        var covers = await CreateService(db, TestUserContext.Member(visibleRootId))
            .ResolveCoverPathsAsync([collectionId], hideNsfw: false, CancellationToken.None);

        Assert.Equal($"/assets/test/{visibleMemberId:N}.jpg", Assert.Single(covers).Value);
    }

    private static CollectionItemDetailRow Item(Guid collectionId, Guid itemId, int sortOrder) =>
        new() {
            Id = Guid.NewGuid(),
            CollectionEntityId = collectionId,
            ItemEntityId = itemId,
            Source = CollectionItemSource.Manual,
            SortOrder = sortOrder,
            AddedAt = DateTimeOffset.UtcNow
        };

    private static Guid SeedCollection(
        PrismediaDbContext db,
        string title,
        Guid ownerUserId,
        CollectionMode mode,
        bool isShared = false,
        bool isNsfw = false) {
        var id = Guid.NewGuid();
        SeedEntity(db, id, EntityKind.Collection.ToCode(), title, isNsfw);
        db.CollectionDetails.Add(new CollectionDetailRow {
            EntityId = id,
            OwnerUserId = ownerUserId,
            Mode = mode,
            IsShared = isShared,
        });
        return id;
    }

    private static void SeedEntity(
        PrismediaDbContext db,
        Guid id,
        string kind,
        string title,
        bool isNsfw = false,
        Guid? parentEntityId = null) {
        db.Entities.Add(new EntityRow {
            Id = id,
            KindCode = kind,
            Title = title,
            ParentEntityId = parentEntityId,
            IsNsfw = isNsfw,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
    }

    private static void SeedRoot(PrismediaDbContext db, Guid id, string label) {
        var now = DateTimeOffset.UtcNow;
        db.LibraryRoots.Add(new LibraryRootRow {
            Id = id,
            Path = $"/media/{id:N}",
            Label = label,
            Enabled = true,
            CreatedAt = now,
            UpdatedAt = now,
        });
    }

    private static CollectionItemReadService CreateService(
        PrismediaDbContext db,
        ICurrentUserContext currentUser) =>
        new(
            db,
            new FakeEntityReadService(db),
            currentUser,
            new EfEntityCatalogQuery(
                db,
                new EfEntityLibraryVisibilityFilter(db, currentUser)));

    private static PrismediaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase($"collection-items-{Guid.NewGuid():N}")
            .Options);

    private sealed class FakeEntityReadService(PrismediaDbContext db) : IEntityReadService {
        public Task<EntityListResponse> ListAsync(
            string? kind,
            string? query,
            string? cursor,
            bool? hideNsfw,
            int? limit,
            CancellationToken cancellationToken,
            Guid? referencedBy = null,
            string? relationshipCode = null,
            string? sort = null,
            string? sortDir = null,
            int? seed = null,
            bool? favorite = null,
            bool? organized = null,
            int? ratingMin = null,
            int? ratingMax = null,
            bool? unrated = null,
            string? status = null,
            string? bookType = null,
            string? bookFormat = null,
            bool? nsfw = null,
            bool? hasFile = null,
            bool? played = null,
            bool? orphaned = null,
            bool? wanted = null,
            AcquisitionStatus? acquisitionStatus = null) =>
            throw new NotSupportedException();

        public Task<EntityCard?> GetAsync(Guid id, bool hideNsfw, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public async Task<EntityThumbnailBatchResponse> GetThumbnailsAsync(
            IReadOnlyList<Guid> ids,
            bool hideNsfw,
            CancellationToken cancellationToken) {
            var rows = await db.Entities.AsNoTracking()
                .Where(row => ids.Contains(row.Id) && (!hideNsfw || !row.IsNsfw))
                .ToArrayAsync(cancellationToken);
            var byId = rows.ToDictionary(row => row.Id);
            var thumbnails = ids
                .Select(id => byId.GetValueOrDefault(id))
                .Where(row => row is not null)
                .Select(row => new EntityThumbnail(
                    row!.Id,
                    row.KindCode.DecodeAs<EntityKind>(),
                    row.Title,
                    row.ParentEntityId,
                    row.SortOrder,
                    $"/assets/test/{row.Id:N}.jpg",
                    null,
                    ThumbnailHoverKind.None,
                    null,
                    [],
                    [],
                    null,
                    false,
                    row.IsNsfw,
                    row.IsOrganized))
                .ToArray();
            return new EntityThumbnailBatchResponse(thumbnails);
        }

    }
}
