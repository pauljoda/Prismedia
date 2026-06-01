using Prismedia.Application.Collections;
using Prismedia.Application.Entities;
using Prismedia.Application.Jellyfin;
using Prismedia.Contracts.Collections;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Jellyfin;

namespace Prismedia.Api.Tests;

/// <summary>
/// Unit coverage for the Jellyfin catalog projection behaviours that keep Infuse browsing fast and
/// complete: lean playable-list responses (no per-row detail hydration), collection poster
/// fallback, and actor filmography resolution.
/// </summary>
public sealed class JellyfinCatalogServiceTests {
    private const string ServerId = "0123456789abcdef0123456789abcdef";

    [Fact]
    public async Task BrowsingPlayableListDoesNotHydratePerRowDetail() {
        var movieId = Guid.NewGuid();
        var entities = new FakeEntityReadService();
        entities.ListByKind["movie"] = [Thumb(movieId, "movie", "Some Movie")];
        var catalog = new JellyfinCatalogService(entities, new FakeCollections());

        var result = await catalog.GetItemsAsync(
            Query(parentId: JellyfinCatalogService.MoviesViewId),
            ServerId,
            hideNsfw: false,
            CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(JellyfinProtocol.ItemTypes.Movie, item.Type);
        Assert.True(item.RunTimeTicks > 0); // duration carried from the thumbnail projection, no detail load
        // The dominant cost of the old path: a full detail load per playable row. It must not happen.
        Assert.Empty(entities.DetailCalls);
    }

    [Fact]
    public async Task BrowsingSeriesListHydratesFolderChildCount() {
        var seriesId = Guid.NewGuid();
        var seasonId = Guid.NewGuid();
        var entities = new FakeEntityReadService();
        entities.ListByKind["video-series"] = [Thumb(seriesId, "video-series", "A Show")];
        entities.Cards[seriesId] = new EntityCard {
            Id = seriesId,
            Kind = "video-series",
            Title = "A Show",
            ParentEntityId = null,
            SortOrder = null,
            Capabilities = [],
            ChildrenByKind = [new EntityGroup("video-season", "Seasons", [Thumb(seasonId, "video-season", "Season 1")])],
            Relationships = []
        };
        var catalog = new JellyfinCatalogService(entities, new FakeCollections());

        var result = await catalog.GetItemsAsync(
            Query(parentId: JellyfinCatalogService.SeriesViewId),
            ServerId,
            hideNsfw: false,
            CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(JellyfinProtocol.ItemTypes.Series, item.Type);
        Assert.Equal(1, item.ChildCount); // folder containers are still hydrated for their child count
        Assert.Contains(seriesId, entities.DetailCalls);
    }

    [Fact]
    public async Task CollectionWithoutOwnCoverGetsRepresentativePrimaryImage() {
        var collectionId = Guid.NewGuid();
        const string coverPath = "/assets/library/member-poster.jpg";
        var entities = new FakeEntityReadService();
        entities.ListByKind["collection"] = [Thumb(collectionId, "collection", "Favourites", coverUrl: null)];
        entities.Cards[collectionId] = new EntityCard {
            Id = collectionId,
            Kind = "collection",
            Title = "Favourites",
            ParentEntityId = null,
            SortOrder = null,
            Capabilities = [], // no own artwork
            ChildrenByKind = [],
            Relationships = []
        };
        var collections = new FakeCollections();
        collections.Covers[collectionId] = coverPath;
        var catalog = new JellyfinCatalogService(entities, collections);

        var list = await catalog.GetItemsAsync(
            Query(parentId: JellyfinCatalogService.CollectionsViewId),
            ServerId,
            hideNsfw: false,
            CancellationToken.None);
        var collection = Assert.Single(list.Items);
        Assert.True(collection.ImageTags!.TryGetValue("Primary", out var listTag));

        // The image endpoint must resolve the same representative cover (path + tag) the list advertised.
        var asset = await catalog.GetImageAssetAsync(collectionId, "Primary", null, hideNsfw: false, CancellationToken.None);
        Assert.NotNull(asset);
        Assert.Equal(coverPath, asset!.Path);
        Assert.Equal(listTag, asset.ImageTag);
    }

    [Fact]
    public async Task LibraryViewAdvertisesRepresentativePosterAndServesIt() {
        var movieId = Guid.NewGuid();
        const string coverPath = "/assets/library/recent-movie.jpg";
        var entities = new FakeEntityReadService();
        entities.ListByKind["movie"] = [Thumb(movieId, "movie", "Recent Movie", coverUrl: coverPath)];
        var catalog = new JellyfinCatalogService(entities, new FakeCollections());

        var views = await catalog.GetUserViewsWithArtworkAsync(ServerId, hideNsfw: false, CancellationToken.None);
        var movies = views.Items.Single(view => view.Name == "Movies");
        Assert.True(movies.ImageTags!.TryGetValue("Primary", out var viewTag));

        // The image endpoint resolves the synthetic view id to the same representative poster.
        var asset = await catalog.GetImageAssetAsync(
            JellyfinCatalogService.MoviesViewId, "Primary", null, hideNsfw: false, CancellationToken.None);
        Assert.NotNull(asset);
        Assert.Equal(coverPath, asset!.Path);
        Assert.Equal(viewTag, asset.ImageTag);
    }

    [Fact]
    public async Task PersonIdsReturnsFilmographyViaReverseRelationship() {
        var personId = Guid.NewGuid();
        var movieId = Guid.NewGuid();
        var entities = new FakeEntityReadService();
        entities.ReferencedBy[personId] = [Thumb(movieId, "movie", "Their Movie")];
        var catalog = new JellyfinCatalogService(entities, new FakeCollections());

        var result = await catalog.GetItemsAsync(
            Query(personIds: [personId]),
            ServerId,
            hideNsfw: false,
            CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(movieId, item.Id);
        Assert.Equal(JellyfinProtocol.ItemTypes.Movie, item.Type);
        Assert.Contains(personId, entities.ReferencedByCalls);
    }

    [Fact]
    public async Task BrowsingByPersonParentIdReturnsFilmography() {
        var personId = Guid.NewGuid();
        var seriesId = Guid.NewGuid();
        var entities = new FakeEntityReadService();
        entities.Cards[personId] = new EntityCard {
            Id = personId,
            Kind = "person",
            Title = "A Performer",
            ParentEntityId = null,
            SortOrder = null,
            Capabilities = [],
            ChildrenByKind = [],
            Relationships = []
        };
        entities.ReferencedBy[personId] = [Thumb(seriesId, "video-series", "Their Show")];
        var catalog = new JellyfinCatalogService(entities, new FakeCollections());

        var result = await catalog.GetItemsAsync(
            Query(parentId: personId),
            ServerId,
            hideNsfw: false,
            CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(seriesId, item.Id);
        Assert.Contains(personId, entities.ReferencedByCalls);
    }

    [Fact]
    public async Task PersonItemMapsToPersonFolderType() {
        var personId = Guid.NewGuid();
        var entities = new FakeEntityReadService();
        entities.Cards[personId] = new EntityCard {
            Id = personId,
            Kind = "person",
            Title = "A Performer",
            ParentEntityId = null,
            SortOrder = null,
            Capabilities = [],
            ChildrenByKind = [],
            Relationships = []
        };
        var catalog = new JellyfinCatalogService(entities, new FakeCollections());

        var item = await catalog.GetItemAsync(personId, ServerId, hideNsfw: false, CancellationToken.None);

        Assert.NotNull(item);
        Assert.Equal(JellyfinProtocol.ItemTypes.Person, item!.Type);
        Assert.True(item.IsFolder);
    }

    private static JellyfinItemQuery Query(
        Guid? parentId = null,
        IReadOnlyList<Guid>? personIds = null) =>
        new(
            parentId,
            [],
            Recursive: false,
            SearchTerm: null,
            IncludeItemTypes: [],
            StartIndex: 0,
            Limit: null,
            SortBy: null,
            SortOrder: null,
            IsFavorite: null,
            IsPlayed: null,
            PersonIds: personIds ?? []);

    private static EntityThumbnail Thumb(Guid id, string kind, string title, string? coverUrl = "/assets/cover.jpg") =>
        new(
            id,
            kind,
            title,
            ParentEntityId: null,
            SortOrder: null,
            coverUrl,
            CoverThumbUrl: null,
            HoverKind: "none",
            HoverUrl: null,
            HoverImages: [],
            Meta: [new EntityThumbnailMeta("duration", "01:30")],
            Rating: null,
            IsFavorite: false,
            IsNsfw: false,
            IsOrganized: true);

    private sealed class FakeEntityReadService : IEntityReadService {
        public Dictionary<string, IReadOnlyList<EntityThumbnail>> ListByKind { get; } = new();
        public Dictionary<Guid, IReadOnlyList<EntityThumbnail>> ReferencedBy { get; } = new();
        public Dictionary<Guid, EntityCard> Cards { get; } = new();
        public List<Guid> DetailCalls { get; } = [];
        public List<Guid> ReferencedByCalls { get; } = [];

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
            string? status = null) {
            IReadOnlyList<EntityThumbnail> items;
            if (referencedBy is { } personId) {
                ReferencedByCalls.Add(personId);
                items = ReferencedBy.GetValueOrDefault(personId) ?? [];
            } else {
                items = ListByKind.GetValueOrDefault(kind ?? string.Empty) ?? [];
            }

            return Task.FromResult(new EntityListResponse(items, null, items.Count));
        }

        public Task<EntityCard?> GetAsync(Guid id, bool hideNsfw, CancellationToken cancellationToken) =>
            Task.FromResult(Cards.GetValueOrDefault(id));

        public Task<EntityThumbnailBatchResponse> GetThumbnailsAsync(
            IReadOnlyList<Guid> ids,
            bool hideNsfw,
            CancellationToken cancellationToken) =>
            Task.FromResult(new EntityThumbnailBatchResponse([]));

        public Task<IEntityCard?> GetDetailAsync(Guid id, string kind, bool hideNsfw, CancellationToken cancellationToken) {
            DetailCalls.Add(id);
            return Task.FromResult<IEntityCard?>(Cards.GetValueOrDefault(id));
        }
    }

    private sealed class FakeCollections : ICollectionItemReadService {
        public Dictionary<Guid, string> Covers { get; } = new();

        public Task<CollectionItemsResponse> ListItemsAsync(
            Guid collectionId,
            bool hideNsfw,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CollectionItemsResponse([]));

        public Task<IReadOnlyDictionary<Guid, string>> ResolveCoverPathsAsync(
            IReadOnlyList<Guid> collectionIds,
            bool hideNsfw,
            CancellationToken cancellationToken) {
            IReadOnlyDictionary<Guid, string> result = collectionIds
                .Where(Covers.ContainsKey)
                .ToDictionary(id => id, id => Covers[id]);
            return Task.FromResult(result);
        }
    }
}
