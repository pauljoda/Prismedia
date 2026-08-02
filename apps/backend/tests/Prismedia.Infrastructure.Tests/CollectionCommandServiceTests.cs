using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Collections;
using Prismedia.Application.Entities;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Application.Security;
using Prismedia.Contracts.Collections;
using Prismedia.Contracts.Entities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Collections;
using Prismedia.Infrastructure.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class CollectionCommandServiceTests {
    [Fact]
    public async Task CreateAsyncPersistsCollectionSettingsDescriptionAndFlags() {
        await using var db = CreateContext();
        var service = CreateService(db);

        var result = await service.CreateAsync(
            new CollectionWriteRequest(
                "Favorites",
                "Pinned media",
                CollectionMode.Hybrid,
                EmptyRuleJson,
                CollectionCoverMode.Mosaic,
                null,
                true),
            CancellationToken.None);

        Assert.Equal(CollectionCommandStatus.Succeeded, result.Status);
        Assert.NotNull(result.Collection);
        Assert.Equal("Favorites", result.Collection.Title);
        var configuration = Configuration(result.Collection);
        Assert.Equal(CollectionMode.Hybrid, configuration.Mode);
        Assert.False(configuration.IsShared);
        Assert.True(configuration.CanEdit);

        var entity = Assert.Single(db.Entities);
        Assert.Equal(EntityKind.Collection.ToCode(), entity.KindCode);
        Assert.True(entity.IsNsfw);
        Assert.Equal("Pinned media", Assert.Single(db.EntityDescriptions).Value);
        var detail = Assert.Single(db.CollectionDetails);
        Assert.Equal(CollectionMode.Hybrid, detail.Mode);
        Assert.Equal(TestUserContext.UserId, detail.OwnerUserId);
        Assert.False(detail.IsShared);
    }

    [Fact]
    public async Task UpdateAsyncAllowsOwnerToShareCollectionAndRejectsOtherUsers() {
        await using var db = CreateContext();
        var ownerUserId = Guid.Parse("11111111-1111-4111-8111-111111111111");
        var otherUserId = Guid.Parse("22222222-2222-4222-8222-222222222222");
        var collectionId = SeedCollection(db, "Private", ownerUserId: ownerUserId);
        await db.SaveChangesAsync();
        var request = new CollectionWriteRequest(
            "Shared picks",
            null,
            CollectionMode.Manual,
            null,
            CollectionCoverMode.Mosaic,
            null,
            false,
            true);

        var rejected = await CreateService(db, user: TestUserContext.MemberAs(otherUserId))
            .UpdateAsync(collectionId, request, CancellationToken.None);

        Assert.Equal(CollectionCommandStatus.NotFound, rejected.Status);
        Assert.False((await db.CollectionDetails.SingleAsync()).IsShared);

        var updated = await CreateService(db, user: TestUserContext.MemberAs(ownerUserId))
            .UpdateAsync(collectionId, request, CancellationToken.None);

        Assert.Equal(CollectionCommandStatus.Succeeded, updated.Status);
        Assert.True(Configuration(updated.Collection!).IsShared);
        Assert.True((await db.CollectionDetails.SingleAsync()).IsShared);
    }

    [Fact]
    public async Task RulePreviewAndStoredRefreshUseAuthenticatedOwnerState() {
        await using var db = CreateContext();
        var ownerUserId = Guid.Parse("33333333-3333-4333-8333-333333333333");
        var engine = new FakeCollectionRuleEngine([]);
        var service = CreateService(db, engine, user: TestUserContext.MemberAs(ownerUserId));

        _ = await service.PreviewRulesAsync(
            new CollectionRulePreviewRequest(EmptyRuleJson),
            hideNsfw: false,
            CancellationToken.None);
        _ = await service.CreateAsync(
            new CollectionWriteRequest(
                "Skipped by me",
                null,
                CollectionMode.Dynamic,
                EmptyRuleJson,
                CollectionCoverMode.Mosaic,
                null,
                false),
            CancellationToken.None);

        Assert.Equal([ownerUserId, ownerUserId], engine.UserIds);
    }

    [Theory]
    [InlineData(CollectionMode.Dynamic)]
    [InlineData(CollectionMode.Hybrid)]
    public async Task CreateAsyncAcceptsValidRuleCollections(CollectionMode mode) {
        await using var db = CreateContext();
        var matchedId = SeedEntity(db, EntityKind.Video.ToCode(), "Rule Match");
        await db.SaveChangesAsync();
        var refreshPersistence = new FakeCollectionRefreshPersistence();
        var service = CreateService(
            db,
            new FakeCollectionRuleEngine([new CollectionRuleMatch(EntityKind.Video, matchedId)]),
            refreshPersistence);

        var result = await service.CreateAsync(
            new CollectionWriteRequest(
                "Rule Picks",
                null,
                mode,
                """{"type":"group","operator":"and","children":[{"type":"condition","entityTypes":["video"],"field":"title","operator":"contains","value":"Rule"}]}""",
                CollectionCoverMode.Mosaic,
                null,
                false),
            CancellationToken.None);

        Assert.Equal(CollectionCommandStatus.Succeeded, result.Status);
        var refresh = Assert.Single(refreshPersistence.Refreshes);
        var refreshedItem = Assert.Single(refresh.ResolvedItems);
        Assert.Equal(EntityKind.Video, refreshedItem.EntityKind);
        Assert.Equal(matchedId, refreshedItem.EntityId);
    }

    [Theory]
    [InlineData("""{"type":"condition","entityTypes":[],"field":"title","operator":"contains","value":"Rule"}""")]
    [InlineData("""{"type":"group","operator":"and","children":null}""")]
    [InlineData("""{"type":"group","operator":"xor","children":[]}""")]
    [InlineData("""{"type":"group","operator":"and","children":[{"type":"condition","entityTypes":["collection"],"field":"title","operator":"contains","value":"Rule"}]}""")]
    [InlineData("""{"type":"group","operator":"and","children":[{"type":"condition","entityTypes":[],"field":"unknown","operator":"contains","value":"Rule"}]}""")]
    [InlineData("""{"type":"group","operator":"and","children":[{"type":"condition","entityTypes":[],"field":"title","operator":"between","value":["only-one"]}]}""")]
    [InlineData("""{"type":"group","operator":"and","children":[{"type":"condition","entityTypes":[],"field":"libraryRootId","operator":"equals","value":"not-a-library-id"}]}""")]
    public async Task CreateAsyncRejectsInvalidRuleTrees(string ruleTreeJson) {
        await using var db = CreateContext();
        var service = CreateService(db);

        var result = await service.CreateAsync(
            new CollectionWriteRequest(
                "Bad Rules",
                null,
                CollectionMode.Dynamic,
                ruleTreeJson,
                CollectionCoverMode.Mosaic,
                null,
                false),
            CancellationToken.None);

        Assert.Equal(CollectionCommandStatus.Invalid, result.Status);
        Assert.Empty(db.CollectionDetails);
    }

    [Fact]
    public async Task UpdateAsyncReplacesCollectionSpecificSettings() {
        await using var db = CreateContext();
        var collectionId = SeedCollection(db, "Watch Later");
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var result = await service.UpdateAsync(
            collectionId,
            new CollectionWriteRequest(
                "Rule Picks",
                null,
                CollectionMode.Dynamic,
                EmptyRuleJson,
                CollectionCoverMode.Item,
                null,
                false),
            CancellationToken.None);

        Assert.Equal(CollectionCommandStatus.Succeeded, result.Status);
        var entity = await db.Entities.SingleAsync(row => row.Id == collectionId);
        var detail = await db.CollectionDetails.SingleAsync(row => row.EntityId == collectionId);
        Assert.Equal("Rule Picks", entity.Title);
        Assert.False(entity.IsNsfw);
        Assert.Equal(CollectionMode.Dynamic, detail.Mode);
        Assert.Equal(CollectionCoverMode.Item, detail.CoverMode);
    }

    [Fact]
    public async Task UpdateAsyncRefreshesDynamicRuleItemsOnSave() {
        await using var db = CreateContext();
        var collectionId = SeedCollection(db, "Watch Later", CollectionMode.Dynamic);
        var matchedId = SeedEntity(db, EntityKind.VideoSeries.ToCode(), "The Chair Company");
        await db.SaveChangesAsync();
        var refreshPersistence = new FakeCollectionRefreshPersistence();
        var service = CreateService(
            db,
            new FakeCollectionRuleEngine([new CollectionRuleMatch(EntityKind.VideoSeries, matchedId)]),
            refreshPersistence);

        var result = await service.UpdateAsync(
            collectionId,
            new CollectionWriteRequest(
                "Rule Picks",
                null,
                CollectionMode.Dynamic,
                """{"type":"group","operator":"and","children":[{"type":"condition","entityTypes":[],"field":"title","operator":"contains","value":"Chair"}]}""",
                CollectionCoverMode.Mosaic,
                null,
                false),
            CancellationToken.None);

        Assert.Equal(CollectionCommandStatus.Succeeded, result.Status);
        var refresh = Assert.Single(refreshPersistence.Refreshes);
        Assert.Equal(collectionId, refresh.CollectionEntityId);
        var refreshedItem = Assert.Single(refresh.ResolvedItems);
        Assert.Equal(EntityKind.VideoSeries, refreshedItem.EntityKind);
        Assert.Equal(matchedId, refreshedItem.EntityId);
    }

    [Fact]
    public async Task AddRemoveAndReorderItemsPreservesManualMembershipRules() {
        await using var db = CreateContext();
        var collectionId = SeedCollection(db, "Manual");
        var firstId = SeedEntity(db, EntityKind.Video.ToCode(), "First");
        var secondId = SeedEntity(db, EntityKind.Image.ToCode(), "Second");
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var added = await service.AddItemsAsync(
            collectionId,
            new CollectionAddItemsRequest([
                new CollectionItemReference(EntityKind.Video, firstId),
                new CollectionItemReference(EntityKind.Image, secondId),
                new CollectionItemReference(EntityKind.Video, firstId),
            ]),
            CancellationToken.None);

        Assert.Equal(CollectionCommandStatus.Succeeded, added.Status);
        Assert.Equal(2, added.Count);

        var itemRows = await db.CollectionItemDetails.OrderBy(row => row.SortOrder).ToArrayAsync();
        Assert.Equal([firstId, secondId], itemRows.Select(row => row.ItemEntityId));

        var reordered = await service.ReorderItemsAsync(
            collectionId,
            new CollectionReorderItemsRequest([itemRows[1].Id, itemRows[0].Id]),
            CancellationToken.None);

        Assert.Equal(CollectionCommandStatus.Succeeded, reordered.Status);
        Assert.Equal(2, reordered.Count);
        Assert.Equal([secondId, firstId],
            db.CollectionItemDetails.OrderBy(row => row.SortOrder).Select(row => row.ItemEntityId).ToArray());

        var removed = await service.RemoveItemsAsync(
            collectionId,
            new CollectionRemoveItemsRequest([itemRows[1].Id]),
            CancellationToken.None);

        Assert.Equal(CollectionCommandStatus.Succeeded, removed.Status);
        Assert.Equal(1, removed.Count);
        Assert.Equal(firstId, Assert.Single(db.CollectionItemDetails).ItemEntityId);
    }

    [Fact]
    public async Task AddItemsAsyncAllowsAudioContainersButRejectsNestedCollections() {
        await using var db = CreateContext();
        var collectionId = SeedCollection(db, "Manual");
        var seriesId = SeedEntity(db, EntityKind.VideoSeries.ToCode(), "The Chair Company");
        var artistId = SeedEntity(db, EntityKind.MusicArtist.ToCode(), "A Band");
        var albumId = SeedEntity(db, EntityKind.AudioLibrary.ToCode(), "A Record");
        var nestedCollectionId = SeedCollection(db, "Nested");
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var added = await service.AddItemsAsync(
            collectionId,
            new CollectionAddItemsRequest([
                new CollectionItemReference(EntityKind.VideoSeries, seriesId),
                new CollectionItemReference(EntityKind.MusicArtist, artistId),
                new CollectionItemReference(EntityKind.AudioLibrary, albumId),
            ]),
            CancellationToken.None);

        Assert.Equal(CollectionCommandStatus.Succeeded, added.Status);
        Assert.Equal([seriesId, artistId, albumId], db.CollectionItemDetails.OrderBy(row => row.SortOrder).Select(row => row.ItemEntityId).ToArray());

        var rejected = await service.AddItemsAsync(
            collectionId,
            new CollectionAddItemsRequest([new CollectionItemReference(EntityKind.Collection, nestedCollectionId)]),
            CancellationToken.None);

        Assert.Equal(CollectionCommandStatus.Invalid, rejected.Status);
        Assert.Contains("cannot be added to a collection", rejected.Message);
    }

    [Fact]
    public async Task AddItemsAsyncRejectsAudiobookTracksOwnedByBooks() {
        await using var db = CreateContext();
        var collectionId = SeedCollection(db, "Manual");
        var bookId = SeedEntity(db, EntityKind.Book.ToCode(), "Spoken Story");
        var audiobookTrackId = SeedEntity(
            db,
            EntityKind.AudioTrack.ToCode(),
            "Book Chapter",
            parentEntityId: bookId);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var result = await service.AddItemsAsync(
            collectionId,
            new CollectionAddItemsRequest([
                new CollectionItemReference(EntityKind.AudioTrack, audiobookTrackId),
            ]),
            CancellationToken.None);

        Assert.Equal(CollectionCommandStatus.Invalid, result.Status);
        Assert.Empty(db.CollectionItemDetails);
    }

    [Fact]
    public async Task AddItemsAsyncRejectsEntitiesOutsideTheViewersLibraries() {
        await using var db = CreateContext();
        var visibleRootId = Guid.NewGuid();
        var restrictedRootId = Guid.NewGuid();
        var collectionId = SeedCollection(db, "Manual");
        var restrictedVideoId = SeedEntity(db, EntityKind.Video.ToCode(), "Restricted");
        SeedRoot(db, visibleRootId, "Visible");
        SeedRoot(db, restrictedRootId, "Restricted");
        db.EntityLibraryRoots.Add(new EntityLibraryRootRow {
            EntityId = restrictedVideoId,
            LibraryRootId = restrictedRootId,
        });
        await db.SaveChangesAsync();
        var service = CreateService(db, user: TestUserContext.Member(visibleRootId));

        var result = await service.AddItemsAsync(
            collectionId,
            new CollectionAddItemsRequest([
                new CollectionItemReference(EntityKind.Video, restrictedVideoId),
            ]),
            CancellationToken.None);

        Assert.Equal(CollectionCommandStatus.Invalid, result.Status);
        Assert.Empty(db.CollectionItemDetails);
    }

    [Fact]
    public async Task RefreshAsyncCountsOnlyMembersVisibleToTheViewer() {
        await using var db = CreateContext();
        var visibleRootId = Guid.NewGuid();
        var restrictedRootId = Guid.NewGuid();
        var collectionId = SeedCollection(db, "Manual");
        var visibleVideoId = SeedEntity(db, EntityKind.Video.ToCode(), "Visible");
        var restrictedVideoId = SeedEntity(db, EntityKind.Video.ToCode(), "Restricted");
        SeedRoot(db, visibleRootId, "Visible");
        SeedRoot(db, restrictedRootId, "Restricted");
        db.EntityLibraryRoots.AddRange(
            new EntityLibraryRootRow { EntityId = visibleVideoId, LibraryRootId = visibleRootId },
            new EntityLibraryRootRow { EntityId = restrictedVideoId, LibraryRootId = restrictedRootId });
        db.CollectionItemDetails.AddRange(
            Item(collectionId, visibleVideoId, 0),
            Item(collectionId, restrictedVideoId, 1));
        await db.SaveChangesAsync();
        var service = CreateService(db, user: TestUserContext.Member(visibleRootId));

        var refresh = await service.RefreshAsync(collectionId, CancellationToken.None);

        Assert.NotNull(refresh);
        Assert.False(refresh.Refreshed);
        Assert.Equal(1, refresh.ItemCount);
    }

    [Fact]
    public async Task AddItemsAsyncRejectsPureDynamicCollections() {
        await using var db = CreateContext();
        var collectionId = SeedCollection(db, "Rules", CollectionMode.Dynamic);
        var videoId = SeedEntity(db, EntityKind.Video.ToCode(), "Matched Video");
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var result = await service.AddItemsAsync(
            collectionId,
            new CollectionAddItemsRequest([new CollectionItemReference(EntityKind.Video, videoId)]),
            CancellationToken.None);

        Assert.Equal(CollectionCommandStatus.Invalid, result.Status);
        Assert.Empty(db.CollectionItemDetails);
    }


    [Fact]
    public async Task PreviewRulesReturnsVisibleCountsAndThumbnailSample() {
        await using var db = CreateContext();
        var visibleId = SeedEntity(db, EntityKind.Video.ToCode(), "Visible");
        _ = SeedEntity(db, EntityKind.Image.ToCode(), "Hidden", isNsfw: true);
        await db.SaveChangesAsync();
        var ruleEngine = new FakeCollectionRuleEngine([
            new CollectionRuleMatch(EntityKind.Video, visibleId),
            new CollectionRuleMatch(EntityKind.Image, db.Entities.Single(row => row.Title == "Hidden").Id),
        ]);
        var service = CreateService(db, ruleEngine);

        var preview = await service.PreviewRulesAsync(
            new CollectionRulePreviewRequest(EmptyRuleJson),
            hideNsfw: true,
            CancellationToken.None);

        Assert.NotNull(preview);
        Assert.Equal(1, preview.Total);
        Assert.Equal(1, preview.ByType["video"]);
        Assert.Equal(visibleId, Assert.Single(preview.Sample).EntityId);
    }

    [Fact]
    public async Task PreviewRulesIncludesSeriesMatchesForUniversalFilters() {
        await using var db = CreateContext();
        var seriesId = SeedEntity(db, EntityKind.VideoSeries.ToCode(), "The Chair Company");
        await db.SaveChangesAsync();
        var ruleEngine = new FakeCollectionRuleEngine([
            new CollectionRuleMatch(EntityKind.VideoSeries, seriesId),
        ]);
        var service = CreateService(db, ruleEngine);

        var preview = await service.PreviewRulesAsync(
            new CollectionRulePreviewRequest(
                """{"type":"group","operator":"and","children":[{"type":"condition","entityTypes":[],"field":"title","operator":"contains","value":"Chair"}]}"""),
            hideNsfw: false,
            CancellationToken.None);

        Assert.NotNull(preview);
        Assert.Equal(1, preview.Total);
        Assert.Equal(1, preview.ByType["video-series"]);
        var item = Assert.Single(preview.Sample);
        Assert.Equal(EntityKind.VideoSeries, item.EntityType);
        Assert.Equal(seriesId, item.EntityId);
    }

    [Fact]
    public async Task PreviewRulesExcludesAudiobookTracksOwnedByBooks() {
        await using var db = CreateContext();
        var bookId = SeedEntity(db, EntityKind.Book.ToCode(), "Spoken Story");
        var audiobookTrackId = SeedEntity(
            db,
            EntityKind.AudioTrack.ToCode(),
            "Book Chapter",
            parentEntityId: bookId);
        await db.SaveChangesAsync();
        var service = CreateService(
            db,
            new FakeCollectionRuleEngine([
                new CollectionRuleMatch(EntityKind.AudioTrack, audiobookTrackId),
            ]));

        var preview = await service.PreviewRulesAsync(
            new CollectionRulePreviewRequest(EmptyRuleJson),
            hideNsfw: false,
            CancellationToken.None);

        Assert.NotNull(preview);
        Assert.Equal(0, preview.Total);
        Assert.Empty(preview.Sample);
    }

    private const string EmptyRuleJson = """{"type":"group","operator":"and","children":[]}""";

    private static Prismedia.Application.Collections.CollectionCommandService CreateService(
        PrismediaDbContext db,
        ICollectionRuleEngine? ruleEngine = null,
        ICollectionRefreshPersistence? refreshPersistence = null,
        ICurrentUserContext? user = null) {
        var currentUser = user ?? TestUserContext.Admin();
        return new(
            new CollectionCommandPersistence(
                db,
                new EfEntityCatalogQuery(
                    db,
                    new EfEntityLibraryVisibilityFilter(db, currentUser))),
            new FakeEntityReadService(db, currentUser),
            ruleEngine ?? new FakeCollectionRuleEngine([]),
            refreshPersistence ?? new FakeCollectionRefreshPersistence(),
            currentUser);
    }

    private static Guid SeedCollection(
        PrismediaDbContext db,
        string title,
        CollectionMode mode = CollectionMode.Manual,
        Guid? ownerUserId = null,
        bool isShared = false) {
        var id = SeedEntity(db, EntityKind.Collection.ToCode(), title);
        db.CollectionDetails.Add(new CollectionDetailRow {
            EntityId = id,
            OwnerUserId = ownerUserId ?? TestUserContext.UserId,
            IsShared = isShared,
            Mode = mode,
            RuleTreeJson = mode == CollectionMode.Manual ? null : EmptyRuleJson,
        });
        return id;
    }

    private static Guid SeedEntity(
        PrismediaDbContext db,
        string kind,
        string title,
        bool isNsfw = false,
        Guid? parentEntityId = null) {
        var id = Guid.NewGuid();
        db.Entities.Add(new EntityRow {
            Id = id,
            KindCode = kind,
            Title = title,
            ParentEntityId = parentEntityId,
            IsNsfw = isNsfw,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        return id;
    }

    private static CollectionItemDetailRow Item(Guid collectionId, Guid itemId, int sortOrder) =>
        new() {
            Id = Guid.NewGuid(),
            CollectionEntityId = collectionId,
            ItemEntityId = itemId,
            Source = CollectionItemSource.Manual,
            SortOrder = sortOrder,
            AddedAt = DateTimeOffset.UtcNow,
        };

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

    private static PrismediaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase($"collection-commands-{Guid.NewGuid():N}")
            .Options);

    private static CollectionConfigurationCapability Configuration(EntityCard collection) =>
        Assert.Single(collection.Capabilities.OfType<CollectionConfigurationCapability>());

    private sealed class FakeEntityReadService(
        PrismediaDbContext db,
        ICurrentUserContext currentUser) : IEntityReadService {
        public Task<EntityListResponse> ListAsync(
            string? kind,
            string? query,
            string? cursor,
            bool? hideNsfw,
            int? limit,
            CancellationToken cancellationToken,
            Guid? referencedBy = null,
            string? relationshipCode = null,
            EntityListSort? sort = null,
            EntitySortDirection? sortDirection = null,
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
            bool? engaged = null,
            bool? orphaned = null,
            bool? wanted = null,
            AcquisitionStatus? acquisitionStatus = null) =>
            throw new NotSupportedException();

        public async Task<EntityCard?> GetAsync(Guid id, bool hideNsfw, CancellationToken cancellationToken) {
            var entity = await db.Entities.AsNoTracking()
                .FirstOrDefaultAsync(row => row.Id == id && (!hideNsfw || !row.IsNsfw), cancellationToken);
            if (entity is null) {
                return null;
            }

            var capabilities = new List<EntityCapability>();
            if (entity.KindCode == EntityKind.Collection.ToCode()) {
                var detail = await db.CollectionDetails.AsNoTracking()
                    .FirstAsync(row => row.EntityId == id, cancellationToken);
                capabilities.Add(new CollectionConfigurationCapability(
                    detail.IsShared,
                    detail.OwnerUserId == currentUser.UserId,
                    detail.Mode,
                    detail.RuleTreeJson,
                    detail.CoverMode,
                    detail.LastRefreshedAt));
                capabilities.Add(new CoverSelectionCapability(detail.CoverItemEntityId));
            }

            return new EntityCard {
                Id = entity.Id,
                Kind = entity.KindCode.DecodeAs<EntityKind>(),
                Title = entity.Title,
                ParentEntityId = entity.ParentEntityId,
                SortOrder = entity.SortOrder,
                Capabilities = capabilities,
                ChildrenByKind = [],
                Relationships = [],
            };
        }

        public async Task<EntityThumbnailBatchResponse> GetThumbnailsAsync(
            IReadOnlyList<Guid> ids,
            bool hideNsfw,
            CancellationToken cancellationToken) {
            var rows = await db.Entities.AsNoTracking()
                .Where(row => ids.Contains(row.Id) && (!hideNsfw || !row.IsNsfw))
                .ToArrayAsync(cancellationToken);
            var byId = rows.ToDictionary(row => row.Id);
            return new EntityThumbnailBatchResponse(ids
                .Select(id => byId.GetValueOrDefault(id))
                .Where(row => row is not null)
                .Select(row => new EntityThumbnail(
                    row!.Id,
                    row.KindCode.DecodeAs<EntityKind>(),
                    row.Title,
                    row.ParentEntityId,
                    row.SortOrder,
                    null,
                    null,
                    ThumbnailHoverKind.None,
                    null,
                    [],
                    [],
                    null,
                    false,
                    row.IsNsfw,
                    row.IsOrganized))
                .ToArray());
        }

    }

    private sealed class FakeCollectionRuleEngine(IReadOnlyList<CollectionRuleMatch> matches) : ICollectionRuleEngine {
        public List<Guid> UserIds { get; } = [];

        public Task<IReadOnlyList<CollectionRuleMatch>> EvaluateAsync(
            string ruleTreeJson,
            Guid userId,
            CancellationToken cancellationToken) {
            UserIds.Add(userId);
            return Task.FromResult(matches);
        }
    }

    private sealed class FakeCollectionRefreshPersistence : ICollectionRefreshPersistence {
        public List<RefreshCall> Refreshes { get; } = [];

        public Task<CollectionRefreshData?> GetDynamicCollectionAsync(
            Guid collectionEntityId,
            CancellationToken cancellationToken) =>
            Task.FromResult<CollectionRefreshData?>(null);

        public Task<IReadOnlyList<CollectionRefreshData>> ListDynamicCollectionsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CollectionRefreshData>>([]);

        public Task RefreshCollectionItemsAsync(
            Guid collectionEntityId,
            IReadOnlyList<CollectionRuleMatch> resolvedItems,
            CancellationToken cancellationToken) {
            Refreshes.Add(new RefreshCall(collectionEntityId, resolvedItems));
            return Task.CompletedTask;
        }

        public sealed record RefreshCall(
            Guid CollectionEntityId,
            IReadOnlyList<CollectionRuleMatch> ResolvedItems);
    }
}
