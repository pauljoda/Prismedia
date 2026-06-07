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
    public async Task UserViewsIncludeUnwatchedMovieAndSeriesLibraries() {
        var catalog = new JellyfinCatalogService(new FakeEntityReadService(), new FakeCollections());

        var views = await catalog.GetUserViewsWithArtworkAsync(ServerId, hideNsfw: false, CancellationToken.None);

        Assert.Contains(views.Items, view =>
            view.Id == JellyfinCatalogService.UnwatchedMoviesViewId &&
            view.Name == "Unwatched Movies" &&
            view.CollectionType == JellyfinProtocol.CollectionTypes.Movies);
        Assert.Contains(views.Items, view =>
            view.Id == JellyfinCatalogService.UnwatchedSeriesViewId &&
            view.Name == "Unwatched Series" &&
            view.CollectionType == JellyfinProtocol.CollectionTypes.Shows);
    }

    [Fact]
    public async Task BrowsingUnwatchedMoviesUsesPlayedFilter() {
        var unwatchedMovieId = Guid.NewGuid();
        var watchedMovieId = Guid.NewGuid();
        var entities = new FakeEntityReadService();
        entities.ListByKind["movie"] = [
            Thumb(unwatchedMovieId, "movie", "Unwatched Movie"),
            Thumb(watchedMovieId, "movie", "Watched Movie")
        ];
        entities.PlayedById[unwatchedMovieId] = false;
        entities.PlayedById[watchedMovieId] = true;
        var catalog = new JellyfinCatalogService(entities, new FakeCollections());

        var result = await catalog.GetItemsAsync(
            Query(parentId: JellyfinCatalogService.UnwatchedMoviesViewId),
            ServerId,
            hideNsfw: false,
            CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(unwatchedMovieId, item.Id);
        Assert.Contains(entities.ListCalls, call => call.Kind == "movie" && call.Played == false);
    }

    [Fact]
    public async Task LatestUnderUnwatchedMoviesUsesPlayedFilter() {
        var unwatchedMovieId = Guid.NewGuid();
        var watchedMovieId = Guid.NewGuid();
        var entities = new FakeEntityReadService();
        entities.ListByKind["movie"] = [
            Thumb(unwatchedMovieId, "movie", "Unwatched Movie"),
            Thumb(watchedMovieId, "movie", "Watched Movie")
        ];
        entities.PlayedById[unwatchedMovieId] = false;
        entities.PlayedById[watchedMovieId] = true;
        var catalog = new JellyfinCatalogService(entities, new FakeCollections());

        var result = await catalog.GetLatestAsync(
            JellyfinCatalogService.UnwatchedMoviesViewId,
            20,
            ServerId,
            hideNsfw: false,
            CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(unwatchedMovieId, item.Id);
        Assert.Contains(entities.ListCalls, call => call.Kind == "movie" && call.Played == false);
    }

    [Fact]
    public async Task BrowsingUnwatchedSeriesFiltersSeriesRowsNotEpisodes() {
        var unwatchedSeriesId = Guid.NewGuid();
        var watchedSeriesId = Guid.NewGuid();
        var entities = new FakeEntityReadService();
        entities.ListByKind["video-series"] = [
            Thumb(unwatchedSeriesId, "video-series", "Unwatched Show"),
            Thumb(watchedSeriesId, "video-series", "Watched Show")
        ];
        entities.PlayedById[unwatchedSeriesId] = false;
        entities.PlayedById[watchedSeriesId] = true;
        entities.Cards[unwatchedSeriesId] = new EntityCard {
            Id = unwatchedSeriesId,
            Kind = "video-series",
            Title = "Unwatched Show",
            ParentEntityId = null,
            SortOrder = null,
            Capabilities = [],
            ChildrenByKind = [],
            Relationships = []
        };
        var catalog = new JellyfinCatalogService(entities, new FakeCollections());

        var result = await catalog.GetItemsAsync(
            Query(parentId: JellyfinCatalogService.UnwatchedSeriesViewId),
            ServerId,
            hideNsfw: false,
            CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(unwatchedSeriesId, item.Id);
        Assert.Equal(JellyfinProtocol.ItemTypes.Series, item.Type);
        Assert.Contains(entities.ListCalls, call => call.Kind == "video-series" && call.Played == false);
        Assert.DoesNotContain(entities.ListCalls, call => call.Kind == "video");
    }

    [Fact]
    public async Task UnwatchedLibraryViewIdsResolveAsCollectionFolders() {
        var catalog = new JellyfinCatalogService(new FakeEntityReadService(), new FakeCollections());

        var movies = await catalog.GetItemAsync(
            JellyfinCatalogService.UnwatchedMoviesViewId,
            ServerId,
            hideNsfw: false,
            CancellationToken.None);
        var series = await catalog.GetItemAsync(
            JellyfinCatalogService.UnwatchedSeriesViewId,
            ServerId,
            hideNsfw: false,
            CancellationToken.None);

        Assert.NotNull(movies);
        Assert.Equal("Unwatched Movies", movies!.Name);
        Assert.Equal(JellyfinProtocol.ItemTypes.CollectionFolder, movies.Type);
        Assert.NotNull(series);
        Assert.Equal("Unwatched Series", series!.Name);
        Assert.Equal(JellyfinProtocol.ItemTypes.CollectionFolder, series.Type);
    }

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

    [Fact]
    public async Task MusicViewListsArtistsAsFolders() {
        var artistId = Guid.NewGuid();
        var entities = new FakeEntityReadService();
        entities.ListByKind["music-artist"] = [Thumb(artistId, "music-artist", "A Band")];
        var catalog = new JellyfinCatalogService(entities, new FakeCollections());

        var result = await catalog.GetItemsAsync(
            Query(parentId: JellyfinCatalogService.MusicViewId),
            ServerId,
            hideNsfw: false,
            CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(artistId, item.Id);
        Assert.Equal(JellyfinProtocol.ItemTypes.MusicArtist, item.Type);
        Assert.True(item.IsFolder);
    }

    [Fact]
    public async Task BrowsingArtistReturnsAlbumsTaggedWithAlbumArtist() {
        var artistId = Guid.NewGuid();
        var albumId = Guid.NewGuid();
        var entities = new FakeEntityReadService();
        entities.Cards[artistId] = MusicCard(artistId, "music-artist", "A Band", parentId: null,
            children: [new EntityGroup("audio-library", "Albums", [MusicThumb(albumId, "audio-library", "First Album", artistId, sortOrder: 0)])]);
        entities.Cards[albumId] = MusicCard(albumId, "audio-library", "First Album", parentId: artistId, children: []);
        var catalog = new JellyfinCatalogService(entities, new FakeCollections());

        var result = await catalog.GetItemsAsync(
            Query(parentId: artistId),
            ServerId,
            hideNsfw: false,
            CancellationToken.None);

        var album = Assert.Single(result.Items);
        Assert.Equal(albumId, album.Id);
        Assert.Equal(JellyfinProtocol.ItemTypes.MusicAlbum, album.Type);
        Assert.Equal("A Band", album.AlbumArtist);
        Assert.Equal(artistId, Assert.Single(album.ArtistItems!).Id);
    }

    [Fact]
    public async Task BrowsingAlbumReturnsTracksWithAlbumArtistAndTrackNumber() {
        var artistId = Guid.NewGuid();
        var albumId = Guid.NewGuid();
        var trackId = Guid.NewGuid();
        var entities = new FakeEntityReadService();
        entities.Cards[artistId] = MusicCard(artistId, "music-artist", "A Band", parentId: null, children: []);
        entities.Cards[albumId] = MusicCard(albumId, "audio-library", "First Album", parentId: artistId,
            children: [new EntityGroup("audio-track", "Tracks", [MusicThumb(trackId, "audio-track", "Opening Track", albumId, sortOrder: 0)])]);
        var catalog = new JellyfinCatalogService(entities, new FakeCollections());

        var result = await catalog.GetItemsAsync(
            Query(parentId: albumId),
            ServerId,
            hideNsfw: false,
            CancellationToken.None);

        var track = Assert.Single(result.Items);
        Assert.Equal(trackId, track.Id);
        Assert.Equal(JellyfinProtocol.ItemTypes.Audio, track.Type);
        Assert.Equal(JellyfinProtocol.MediaTypes.Audio, track.MediaType);
        Assert.Equal("First Album", track.Album);
        Assert.Equal(albumId, track.AlbumId);
        Assert.Equal("A Band", track.AlbumArtist);
        Assert.Equal(1, track.IndexNumber); // 0-based album sort order projects to a 1-based track number
        Assert.Null(track.VideoType); // audio is not a video file
    }

    private static EntityCard MusicCard(
        Guid id,
        string kind,
        string title,
        Guid? parentId,
        IReadOnlyList<EntityGroup> children) =>
        new() {
            Id = id,
            Kind = kind,
            Title = title,
            ParentEntityId = parentId,
            SortOrder = null,
            Capabilities = [],
            ChildrenByKind = children,
            Relationships = []
        };

    private static EntityThumbnail MusicThumb(Guid id, string kind, string title, Guid parentId, int sortOrder) =>
        new(
            id,
            kind,
            title,
            ParentEntityId: parentId,
            SortOrder: sortOrder,
            CoverUrl: "/assets/cover.jpg",
            CoverThumbUrl: null,
            HoverKind: "none",
            HoverUrl: null,
            HoverImages: [],
            Meta: [new EntityThumbnailMeta("duration", "03:20")],
            Rating: null,
            IsFavorite: false,
            IsNsfw: false,
            IsOrganized: true);

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
        public Dictionary<Guid, bool> PlayedById { get; } = new();
        public List<ListCall> ListCalls { get; } = [];
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
            string? status = null,
            string? bookType = null,
            string? bookFormat = null,
            bool? nsfw = null,
            bool? hasFile = null,
            bool? played = null,
            bool? orphaned = null) {
            ListCalls.Add(new ListCall(kind, referencedBy, played));
            IReadOnlyList<EntityThumbnail> items;
            if (referencedBy is { } personId) {
                ReferencedByCalls.Add(personId);
                items = ReferencedBy.GetValueOrDefault(personId) ?? [];
            } else {
                items = ListByKind.GetValueOrDefault(kind ?? string.Empty) ?? [];
            }

            if (played is { } playedFilter) {
                items = items.Where(item =>
                    PlayedById.TryGetValue(item.Id, out var isPlayed)
                        ? isPlayed == playedFilter
                        : !playedFilter).ToArray();
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

        public sealed record ListCall(string? Kind, Guid? ReferencedBy, bool? Played);
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
