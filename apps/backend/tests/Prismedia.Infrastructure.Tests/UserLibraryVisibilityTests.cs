using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Security;
using Prismedia.Contracts.Entities;
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

        var list = await service.ListAsync(EntityKind.Video.ToCode(), null, null, null, null, CancellationToken.None);
        Assert.Equal(GrantedVideoId, Assert.Single(list.Items).Id);

        Assert.NotNull(await service.GetAsync(GrantedVideoId, hideNsfw: false, CancellationToken.None));
        Assert.Null(await service.GetAsync(RestrictedVideoId, hideNsfw: false, CancellationToken.None));

        var checker = new EfEntityVisibilityChecker(service);
        Assert.True(await checker.IsVisibleAsync(GrantedVideoId, CancellationToken.None));
        Assert.False(await checker.IsVisibleAsync(RestrictedVideoId, CancellationToken.None));
    }

    [Fact]
    public async Task MemberMixedKindListsKeepRootlessTaxonomyAndExcludeRestrictedMedia() {
        await using var db = CreateContext();
        await SeedTwoRootedVideosAsync(db);
        var rootlessTagId = Guid.Parse("bbbb0000-0000-0000-0000-000000000003");
        var now = DateTimeOffset.UtcNow;
        db.Entities.Add(new EntityRow {
            Id = rootlessTagId,
            KindCode = EntityKind.Tag.ToCode(),
            Title = "Shared taxonomy",
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();
        var service = CreateService(db, TestUserContext.Member(GrantedRootId));

        var list = await service.ListAsync(null, null, null, null, null, CancellationToken.None);

        Assert.Equal(2, list.TotalCount);
        Assert.Equal(
            [GrantedVideoId, rootlessTagId],
            list.Items.Select(item => item.Id).Order().ToArray());
    }

    [Fact]
    public async Task AdminSeesEveryRootWithoutAccessRows() {
        await using var db = CreateContext();
        await SeedTwoRootedVideosAsync(db);
        var service = CreateService(db, TestUserContext.Admin());

        var list = await service.ListAsync(EntityKind.Video.ToCode(), null, null, null, null, CancellationToken.None);

        Assert.Equal(2, list.TotalCount);
    }

    [Fact]
    public async Task MemberSeesWantedEntitiesOnlyWhenTheirProfilesTargetGrantedLibraries() {
        await using var db = CreateContext();
        await SeedTwoProfileTargetedWantedBooksAsync(db);
        var service = CreateService(db, TestUserContext.Member(GrantedRootId));

        var list = await service.ListAsync(
            EntityKind.Book.ToCode(),
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
            KindCode = EntityKind.Book.ToCode(),
            Title = "The Anxious Generation",
            IsWanted = true,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.BookDetails.Add(new BookDetailRow { EntityId = RestrictedWantedBookId });
        await db.SaveChangesAsync();
        var service = CreateService(db, TestUserContext.Member(GrantedRootId));

        var list = await service.ListAsync(
            EntityKind.Book.ToCode(),
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
    public async Task DefaultProfileVisibilityIncludesEveryRequestKindInTheProfileFamily() {
        await using var db = CreateContext();
        var wantedTrackId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.LibraryRoots.AddRange(
            new LibraryRootRow {
                Id = GrantedRootId,
                Path = "/media/granted",
                Label = "Granted",
                Enabled = true,
                CreatedAt = now,
                UpdatedAt = now
            },
            new LibraryRootRow {
                Id = RestrictedRootId,
                Path = "/media/audio",
                Label = "Audio",
                Enabled = true,
                ScanAudio = true,
                CreatedAt = now,
                UpdatedAt = now
            });
        db.BookAcquisitionProfiles.Add(new BookAcquisitionProfileRow {
            Id = Guid.NewGuid(),
            Kind = EntityKind.AudioLibrary,
            DisplayName = "Default Audio",
            IsDefault = true,
            TargetLibraryRootId = RestrictedRootId,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Entities.Add(new EntityRow {
            Id = wantedTrackId,
            KindCode = EntityKind.AudioTrack.ToCode(),
            Title = "Future Track",
            IsWanted = true,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.AudioTrackDetails.Add(new AudioTrackDetailRow { EntityId = wantedTrackId });
        await db.SaveChangesAsync();
        var service = CreateService(db, TestUserContext.Member(GrantedRootId));

        var list = await service.ListAsync(
            EntityKind.AudioTrack.ToCode(),
            null,
            null,
            null,
            null,
            CancellationToken.None,
            wanted: true);

        Assert.Empty(list.Items);
    }

    [Fact]
    public void DefinitionDerivedDefaultProfileVisibilityTranslatesForPostgres() {
        var options = new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseNpgsql("Host=localhost;Database=prismedia;Username=prismedia;Password=prismedia")
            .Options;
        using var db = new PrismediaDbContext(options);
        var hiddenRootIds = new[] { RestrictedRootId };
        var profiles = db.BookAcquisitionProfiles;
        var hiddenKinds = profiles
            .Where(profile => profile.IsDefault && hiddenRootIds.Contains(profile.TargetLibraryRootId))
            .Select(profile => profile.Kind);
        var visibleKinds = profiles
            .Where(profile => profile.IsDefault && !hiddenRootIds.Contains(profile.TargetLibraryRootId))
            .Select(profile => profile.Kind);

        var sql = db.Entities
            .Where(EfEntityReadService.DefaultProfileVisibilityExpression(hiddenKinds, visibleKinds))
            .ToQueryString();

        Assert.Contains("SELECT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(EntityKind.AudioTrack.ToCode(), sql, StringComparison.Ordinal);
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
            EntityKind.Video.ToCode(), null, null, null, null, CancellationToken.None, favorite: true);
        var thumbnails = await service.ListAsync(
            EntityKind.Video.ToCode(), null, null, null, null, CancellationToken.None);

        Assert.Empty(favorites.Items);
        Assert.All(thumbnails.Items, item => Assert.False(item.IsFavorite));
        Assert.All(thumbnails.Items, item => Assert.Null(item.PlayCount));
    }

    [Fact]
    public async Task CollectionsAreVisibleOnlyToTheirOwnerUnlessShared() {
        await using var db = CreateContext();
        var ownerUserId = TestUserContext.UserId;
        var otherUserId = Guid.Parse("cccc0000-0000-4000-8000-000000000010");
        var ownedPrivateId = SeedCollection(db, "Mine", ownerUserId, isShared: false);
        var otherPrivateId = SeedCollection(db, "Theirs", otherUserId, isShared: false);
        var sharedId = SeedCollection(db, "Shared", otherUserId, isShared: true);
        await db.SaveChangesAsync();
        var service = CreateService(db, TestUserContext.Admin(ownerUserId));

        var list = await service.ListAsync(
            EntityKind.Collection.ToCode(),
            null,
            null,
            null,
            null,
            CancellationToken.None);
        var thumbnails = await service.GetThumbnailsAsync(
            [ownedPrivateId, otherPrivateId, sharedId],
            hideNsfw: false,
            CancellationToken.None);

        Assert.Equal(new[] { ownedPrivateId, sharedId }.Order(), list.Items.Select(item => item.Id).Order());
        Assert.Equal(new[] { ownedPrivateId, sharedId }.Order(), thumbnails.Items.Select(item => item.Id).Order());
        Assert.NotNull(await service.GetAsync(
            ownedPrivateId, hideNsfw: false,
            CancellationToken.None));
        Assert.Null(await service.GetAsync(
            otherPrivateId, hideNsfw: false,
            CancellationToken.None));
        var shared = Assert.IsType<EntityCard>(
            await service.GetAsync(
                sharedId, hideNsfw: false,
                CancellationToken.None));
        var configuration = Assert.Single(shared.Capabilities.OfType<CollectionConfigurationCapability>());
        Assert.True(configuration.IsShared);
        Assert.False(configuration.CanEdit);
    }

    private static EfEntityReadService CreateService(PrismediaDbContext db, ICurrentUserContext user) {
        var kindMappers = EntityMappers.Kinds(db, user);
        var repository = new EfEntityRepository(db, user, kindMappers, EntityMappers.Capabilities(db, user));
        return new EfEntityReadService(db, user, repository, ThumbnailContributors.For(db));
    }

    private static Guid SeedCollection(
        PrismediaDbContext db,
        string title,
        Guid ownerUserId,
        bool isShared) {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.Entities.Add(new EntityRow {
            Id = id,
            KindCode = EntityKind.Collection.ToCode(),
            Title = title,
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.CollectionDetails.Add(new CollectionDetailRow {
            EntityId = id,
            OwnerUserId = ownerUserId,
            IsShared = isShared,
        });
        return id;
    }

    private static async Task SeedTwoRootedVideosAsync(PrismediaDbContext db) {
        var now = DateTimeOffset.UtcNow;
        db.LibraryRoots.AddRange(
            new LibraryRootRow { Id = GrantedRootId, Path = "/media/a", Label = "A", Enabled = true, CreatedAt = now, UpdatedAt = now },
            new LibraryRootRow { Id = RestrictedRootId, Path = "/media/b", Label = "B", Enabled = true, CreatedAt = now, UpdatedAt = now });
        db.Entities.AddRange(
            new EntityRow { Id = GrantedVideoId, KindCode = EntityKind.Video.ToCode(), Title = "Granted", CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = RestrictedVideoId, KindCode = EntityKind.Video.ToCode(), Title = "Restricted", CreatedAt = now, UpdatedAt = now });
        db.EntityLibraryRoots.AddRange(
            new EntityLibraryRootRow { EntityId = GrantedVideoId, LibraryRootId = GrantedRootId  },
            new EntityLibraryRootRow { EntityId = RestrictedVideoId, LibraryRootId = RestrictedRootId  });
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
            new EntityRow { Id = GrantedWantedBookId, KindCode = EntityKind.Book.ToCode(), Title = "Granted wanted", IsWanted = true, CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = RestrictedWantedBookId, KindCode = EntityKind.Book.ToCode(), Title = "Restricted wanted", IsWanted = true, CreatedAt = now, UpdatedAt = now });
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
