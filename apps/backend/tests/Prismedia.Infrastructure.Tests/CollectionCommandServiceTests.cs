using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Collections;
using Prismedia.Application.Entities;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Contracts.Collections;
using Prismedia.Contracts.Entities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Collections;
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
        Assert.Equal(CollectionMode.Hybrid, result.Collection.Mode);

        var entity = Assert.Single(db.Entities);
        Assert.Equal(EntityKindRegistry.Collection.Code, entity.KindCode);
        Assert.True(entity.IsNsfw);
        Assert.Equal("Pinned media", Assert.Single(db.EntityDescriptions).Value);
        Assert.Equal(CollectionMode.Hybrid, Assert.Single(db.CollectionDetails).Mode);
    }

    [Theory]
    [InlineData(CollectionMode.Dynamic)]
    [InlineData(CollectionMode.Hybrid)]
    public async Task CreateAsyncAcceptsValidRuleCollections(CollectionMode mode) {
        await using var db = CreateContext();
        var matchedId = SeedEntity(db, EntityKindRegistry.Video.Code, "Rule Match");
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
        var matchedId = SeedEntity(db, EntityKindRegistry.VideoSeries.Code, "The Chair Company");
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
        var firstId = SeedEntity(db, EntityKindRegistry.Video.Code, "First");
        var secondId = SeedEntity(db, EntityKindRegistry.Image.Code, "Second");
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
        var seriesId = SeedEntity(db, EntityKindRegistry.VideoSeries.Code, "The Chair Company");
        var artistId = SeedEntity(db, EntityKindRegistry.MusicArtist.Code, "A Band");
        var albumId = SeedEntity(db, EntityKindRegistry.AudioLibrary.Code, "A Record");
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
    public async Task AddItemsAsyncRejectsPureDynamicCollections() {
        await using var db = CreateContext();
        var collectionId = SeedCollection(db, "Rules", CollectionMode.Dynamic);
        var videoId = SeedEntity(db, EntityKindRegistry.Video.Code, "Matched Video");
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
        var visibleId = SeedEntity(db, EntityKindRegistry.Video.Code, "Visible");
        _ = SeedEntity(db, EntityKindRegistry.Image.Code, "Hidden", isNsfw: true);
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
        var seriesId = SeedEntity(db, EntityKindRegistry.VideoSeries.Code, "The Chair Company");
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

    private const string EmptyRuleJson = """{"type":"group","operator":"and","children":[]}""";

    private static Prismedia.Application.Collections.CollectionCommandService CreateService(
        PrismediaDbContext db,
        ICollectionRuleEngine? ruleEngine = null,
        ICollectionRefreshPersistence? refreshPersistence = null) =>
        new(
            new CollectionCommandPersistence(db),
            new FakeEntityReadService(db),
            ruleEngine ?? new FakeCollectionRuleEngine([]),
            refreshPersistence ?? new FakeCollectionRefreshPersistence());

    private static Guid SeedCollection(
        PrismediaDbContext db,
        string title,
        CollectionMode mode = CollectionMode.Manual) {
        var id = SeedEntity(db, EntityKindRegistry.Collection.Code, title);
        db.CollectionDetails.Add(new CollectionDetailRow {
            EntityId = id,
            Mode = mode,
            RuleTreeJson = mode == CollectionMode.Manual ? null : EmptyRuleJson,
        });
        return id;
    }

    private static Guid SeedEntity(
        PrismediaDbContext db,
        string kind,
        string title,
        bool isNsfw = false) {
        var id = Guid.NewGuid();
        db.Entities.Add(new EntityRow {
            Id = id,
            KindCode = kind,
            Title = title,
            IsNsfw = isNsfw,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        return id;
    }

    private static PrismediaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase($"collection-commands-{Guid.NewGuid():N}")
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

        public async Task<IEntityCard?> GetDetailAsync(Guid id, string kind, bool hideNsfw, CancellationToken cancellationToken) {
            var entity = await db.Entities.AsNoTracking()
                .FirstOrDefaultAsync(row => row.Id == id && row.KindCode == kind, cancellationToken);
            if (entity is null) return null;

            var detail = await db.CollectionDetails.AsNoTracking()
                .FirstAsync(row => row.EntityId == id, cancellationToken);
            return new CollectionDetail {
                Id = entity.Id,
                Kind = entity.KindCode.DecodeAs<EntityKind>(),
                Title = entity.Title,
                ParentEntityId = entity.ParentEntityId,
                SortOrder = entity.SortOrder,
                Capabilities = [],
                ChildrenByKind = [],
                Relationships = [],
                Mode = detail.Mode,
                RuleTreeJson = detail.RuleTreeJson,
                CoverMode = detail.CoverMode,
                CoverItemId = detail.CoverItemEntityId,
                LastRefreshedAt = detail.LastRefreshedAt,
            };
        }
    }

    private sealed class FakeCollectionRuleEngine(IReadOnlyList<CollectionRuleMatch> matches) : ICollectionRuleEngine {
        public Task<IReadOnlyList<CollectionRuleMatch>> EvaluateAsync(
            string ruleTreeJson,
            CancellationToken cancellationToken) =>
            Task.FromResult(matches);
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
