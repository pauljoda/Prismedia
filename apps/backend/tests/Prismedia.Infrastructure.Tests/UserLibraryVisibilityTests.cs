using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Security;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Entities;
using Prismedia.Infrastructure.Entities.Mappers;
using Prismedia.Infrastructure.Entities.Thumbnails;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

/// <summary>
/// Member library-access enforcement: a member sees only entities in granted roots,
/// hidden entities behave as missing, and per-user engagement never leaks across users.
/// </summary>
public sealed class UserLibraryVisibilityTests {
    private static readonly Guid GrantedRootId = Guid.Parse("aaaa0000-0000-0000-0000-000000000001");
    private static readonly Guid RestrictedRootId = Guid.Parse("aaaa0000-0000-0000-0000-000000000002");
    private static readonly Guid GrantedVideoId = Guid.Parse("bbbb0000-0000-0000-0000-000000000001");
    private static readonly Guid RestrictedVideoId = Guid.Parse("bbbb0000-0000-0000-0000-000000000002");
    private static readonly Guid GrantedWantedBookId = Guid.Parse("dddd0000-0000-0000-0000-000000000001");
    private static readonly Guid RestrictedWantedBookId = Guid.Parse("dddd0000-0000-0000-0000-000000000002");

    [Fact]
    public async Task MemberSeesOnlyGrantedRootsInListsDetailsAndVisibilityChecks() {
        await using var db = CreateContext();
        await SeedTwoRootedVideosAsync(db);
        var member = TestUserContext.Member(GrantedRootId);
        var service = CreateService(db, member);

        var list = await service.ListAsync(EntityKindRegistry.Video.Code, null, null, null, null, CancellationToken.None);
        Assert.Equal(GrantedVideoId, Assert.Single(list.Items).Id);

        Assert.NotNull(await service.GetAsync(GrantedVideoId, hideNsfw: false, CancellationToken.None));
        Assert.Null(await service.GetAsync(RestrictedVideoId, hideNsfw: false, CancellationToken.None));

        var checker = new EfEntityVisibilityChecker(service);
        Assert.True(await checker.IsVisibleAsync(GrantedVideoId, CancellationToken.None));
        Assert.False(await checker.IsVisibleAsync(RestrictedVideoId, CancellationToken.None));
    }

    [Fact]
    public async Task AdminSeesEveryRootWithoutAccessRows() {
        await using var db = CreateContext();
        await SeedTwoRootedVideosAsync(db);
        var service = CreateService(db, TestUserContext.Admin());

        var list = await service.ListAsync(EntityKindRegistry.Video.Code, null, null, null, null, CancellationToken.None);

        Assert.Equal(2, list.TotalCount);
    }

    [Fact]
    public async Task MemberSeesWantedEntitiesOnlyWhenTheirProfilesTargetGrantedLibraries() {
        await using var db = CreateContext();
        await SeedTwoProfileTargetedWantedBooksAsync(db);
        var service = CreateService(db, TestUserContext.Member(GrantedRootId));

        var list = await service.ListAsync(
            EntityKindRegistry.Book.Code,
            null,
            null,
            null,
            null,
            CancellationToken.None,
            wanted: true);

        Assert.Equal(GrantedWantedBookId, Assert.Single(list.Items).Id);
        Assert.NotNull(await service.GetAsync(GrantedWantedBookId, hideNsfw: false, CancellationToken.None));
        Assert.Null(await service.GetAsync(RestrictedWantedBookId, hideNsfw: false, CancellationToken.None));

        var checker = new EfEntityVisibilityChecker(service);
        Assert.False(await checker.IsVisibleAsync(RestrictedWantedBookId, CancellationToken.None));
    }

    [Fact]
    public async Task MemberCannotSeeOrphanedWantedEntityWhenDefaultProfileTargetsRestrictedLibrary() {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        db.LibraryRoots.AddRange(
            new LibraryRootRow {
                Id = GrantedRootId,
                Path = "/media/movies",
                Label = "Movies",
                Enabled = true,
                ScanVideos = true,
                ScanImages = false,
                ScanAudio = false,
                ScanBooks = false,
                CreatedAt = now,
                UpdatedAt = now
            },
            new LibraryRootRow {
                Id = RestrictedRootId,
                Path = "/media/books",
                Label = "Books",
                Enabled = true,
                ScanVideos = false,
                ScanImages = false,
                ScanAudio = false,
                ScanBooks = true,
                CreatedAt = now,
                UpdatedAt = now
            });
        db.BookAcquisitionProfiles.Add(new BookAcquisitionProfileRow {
            Id = Guid.NewGuid(),
            Kind = EntityKind.Book,
            DisplayName = "Default Books",
            IsDefault = true,
            TargetLibraryRootId = RestrictedRootId,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Entities.Add(new EntityRow {
            Id = RestrictedWantedBookId,
            KindCode = EntityKindRegistry.Book.Code,
            Title = "The Anxious Generation",
            IsWanted = true,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.BookDetails.Add(new BookDetailRow { EntityId = RestrictedWantedBookId });
        await db.SaveChangesAsync();
        var service = CreateService(db, TestUserContext.Member(GrantedRootId));

        var list = await service.ListAsync(
            EntityKindRegistry.Book.Code,
            null,
            null,
            null,
            null,
            CancellationToken.None,
            wanted: true);

        Assert.Empty(list.Items);
        Assert.Null(await service.GetAsync(RestrictedWantedBookId, hideNsfw: false, CancellationToken.None));
    }

    [Fact]
    public async Task EngagementStateIsIsolatedPerUser() {
        await using var db = CreateContext();
        await SeedTwoRootedVideosAsync(db);
        var otherUserId = Guid.Parse("cccc0000-0000-0000-0000-000000000009");
        var now = DateTimeOffset.UtcNow;
        db.UserEntityStates.Add(new UserEntityStateRow {
            UserId = otherUserId,
            EntityId = GrantedVideoId,
            IsFavorite = true,
            PlayCount = 5,
            LastPlayedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();

        // The test user has no state of their own: the other user's favorites and
        // playback must not surface.
        var service = CreateService(db, TestUserContext.Admin());
        var favorites = await service.ListAsync(
            EntityKindRegistry.Video.Code, null, null, null, null, CancellationToken.None, favorite: true);
        var thumbnails = await service.ListAsync(
            EntityKindRegistry.Video.Code, null, null, null, null, CancellationToken.None);

        Assert.Empty(favorites.Items);
        Assert.All(thumbnails.Items, item => Assert.False(item.IsFavorite));
        Assert.All(thumbnails.Items, item => Assert.Null(item.PlayCount));
    }

    private static EfEntityReadService CreateService(PrismediaDbContext db, ICurrentUserContext user) {
        var repository = new EfEntityRepository(db, user, EntityMappers.Kinds(db), EntityMappers.Capabilities(db, user));
        return new EfEntityReadService(db, user, repository, EntityMappers.Kinds(db), ThumbnailContributors.For(db));
    }

    private static async Task SeedTwoRootedVideosAsync(PrismediaDbContext db) {
        var now = DateTimeOffset.UtcNow;
        db.LibraryRoots.AddRange(
            new LibraryRootRow { Id = GrantedRootId, Path = "/media/a", Label = "A", Enabled = true, CreatedAt = now, UpdatedAt = now },
            new LibraryRootRow { Id = RestrictedRootId, Path = "/media/b", Label = "B", Enabled = true, CreatedAt = now, UpdatedAt = now });
        db.Entities.AddRange(
            new EntityRow { Id = GrantedVideoId, KindCode = EntityKindRegistry.Video.Code, Title = "Granted", CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = RestrictedVideoId, KindCode = EntityKindRegistry.Video.Code, Title = "Restricted", CreatedAt = now, UpdatedAt = now });
        db.VideoDetails.AddRange(
            new VideoDetailRow { EntityId = GrantedVideoId, LibraryRootId = GrantedRootId },
            new VideoDetailRow { EntityId = RestrictedVideoId, LibraryRootId = RestrictedRootId });
        await db.SaveChangesAsync();
    }

    private static async Task SeedTwoProfileTargetedWantedBooksAsync(PrismediaDbContext db) {
        var now = DateTimeOffset.UtcNow;
        var grantedProfileId = Guid.Parse("eeee0000-0000-0000-0000-000000000001");
        var restrictedProfileId = Guid.Parse("eeee0000-0000-0000-0000-000000000002");
        db.LibraryRoots.AddRange(
            new LibraryRootRow { Id = GrantedRootId, Path = "/media/books-a", Label = "Books A", Enabled = true, CreatedAt = now, UpdatedAt = now },
            new LibraryRootRow { Id = RestrictedRootId, Path = "/media/books-b", Label = "Books B", Enabled = true, CreatedAt = now, UpdatedAt = now });
        db.BookAcquisitionProfiles.AddRange(
            new BookAcquisitionProfileRow {
                Id = grantedProfileId,
                Kind = EntityKind.Book,
                DisplayName = "Granted books",
                TargetLibraryRootId = GrantedRootId,
                CreatedAt = now,
                UpdatedAt = now
            },
            new BookAcquisitionProfileRow {
                Id = restrictedProfileId,
                Kind = EntityKind.Book,
                DisplayName = "Restricted books",
                TargetLibraryRootId = RestrictedRootId,
                CreatedAt = now,
                UpdatedAt = now
            });
        db.Entities.AddRange(
            new EntityRow { Id = GrantedWantedBookId, KindCode = EntityKindRegistry.Book.Code, Title = "Granted wanted", IsWanted = true, CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = RestrictedWantedBookId, KindCode = EntityKindRegistry.Book.Code, Title = "Restricted wanted", IsWanted = true, CreatedAt = now, UpdatedAt = now });
        db.BookDetails.AddRange(
            new BookDetailRow { EntityId = GrantedWantedBookId },
            new BookDetailRow { EntityId = RestrictedWantedBookId });
        db.Acquisitions.AddRange(
            new AcquisitionRow {
                Id = Guid.NewGuid(),
                EntityId = GrantedWantedBookId,
                ProfileId = grantedProfileId,
                Kind = EntityKind.Book,
                Title = "Granted wanted",
                CreatedAt = now,
                UpdatedAt = now
            },
            new AcquisitionRow {
                Id = Guid.NewGuid(),
                EntityId = RestrictedWantedBookId,
                ProfileId = restrictedProfileId,
                Kind = EntityKind.Book,
                Title = "Restricted wanted",
                CreatedAt = now,
                UpdatedAt = now
            });
        await db.SaveChangesAsync();
    }

    private static PrismediaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase($"user-visibility-{Guid.NewGuid():N}")
            .Options);
}
