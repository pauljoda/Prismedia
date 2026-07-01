using System.Text.Json;
using Prismedia.Application.Collections;
using Prismedia.Application.Entities;
using Prismedia.Application.Jellyfin;
using Prismedia.Contracts.Collections;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Jellyfin;
using Prismedia.Domain.Entities;

namespace Prismedia.Api.Tests;

/// <summary>
/// Unit coverage for Jellyfin catalog projection behaviours: large browse lists stay lean, playback
/// shelves include full source data for strict clients, and related collection/filmography rows stay
/// complete.
/// </summary>
public sealed class JellyfinCatalogServiceTests {
    private const string ServerId = "0123456789abcdef0123456789abcdef";
    private static readonly JsonSerializerOptions JellyfinJson = new(JsonSerializerDefaults.Web);

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
    public async Task UserViewsReturnCollectionsAsRootBoxSetsWithoutSyntheticCollectionsFolder() {
        var collectionId = Guid.NewGuid();
        const string coverPath = "/assets/library/collection-member.jpg";
        var entities = new FakeEntityReadService();
        entities.ListByKind["collection"] = [Thumb(collectionId, EntityKind.Collection, "Favorites", coverUrl: null)];
        var collections = new FakeCollections();
        collections.Covers[collectionId] = coverPath;
        var catalog = new JellyfinCatalogService(entities, collections);

        var views = await catalog.GetUserViewsWithArtworkAsync(ServerId, hideNsfw: false, CancellationToken.None);

        Assert.DoesNotContain(views.Items, view =>
            view.Id == JellyfinCatalogService.CollectionsViewId &&
            view.Type == JellyfinProtocol.ItemTypes.CollectionFolder);
        var collection = Assert.Single(views.Items, view => view.Id == collectionId);
        Assert.Equal(JellyfinProtocol.ItemTypes.BoxSet, collection.Type);
        Assert.Equal(JellyfinProtocol.CollectionTypes.BoxSets, collection.CollectionType);
        Assert.True(collection.ImageTags.TryGetValue(JellyfinProtocol.ImageTypes.Primary, out _));
    }

    [Fact]
    public async Task UserViewsReturnAudioCollectionsAsPlaylists() {
        var collectionId = Guid.NewGuid();
        var trackId = Guid.NewGuid();
        var entities = new FakeEntityReadService();
        entities.ListByKind["collection"] = [Thumb(collectionId, EntityKind.Collection, "Mixtape", coverUrl: "/assets/library/mixtape.jpg")];
        var collections = new FakeCollections();
        collections.Items[collectionId] = [
            CollectionItem(collectionId, Thumb(trackId, EntityKind.AudioTrack, "Opening Track"))
        ];
        var catalog = new JellyfinCatalogService(entities, collections);

        var views = await catalog.GetUserViewsWithArtworkAsync(ServerId, hideNsfw: false, CancellationToken.None);

        var collection = Assert.Single(views.Items, view => view.Id == collectionId);
        Assert.Equal(JellyfinProtocol.ItemTypes.Playlist, collection.Type);
        Assert.Equal(JellyfinProtocol.MediaTypes.Audio, collection.MediaType);
        Assert.Null(collection.CollectionType);
        Assert.True(collection.IsFolder);
    }

    [Fact]
    public async Task UserViewsReturnNoCollectionRowsWhenThereAreNoVisibleCollections() {
        var hiddenCollectionId = Guid.NewGuid();
        var entities = new FakeEntityReadService();
        entities.ListByKind["collection"] = [Thumb(hiddenCollectionId, EntityKind.Collection, "Hidden Favorites", isNsfw: true)];
        var catalog = new JellyfinCatalogService(entities, new FakeCollections());

        var views = await catalog.GetUserViewsWithArtworkAsync(ServerId, hideNsfw: true, CancellationToken.None);

        Assert.DoesNotContain(views.Items, view =>
            view.Id == JellyfinCatalogService.CollectionsViewId ||
            view.Type == JellyfinProtocol.ItemTypes.BoxSet);
    }

    [Fact]
    public async Task RootBrowseReturnsCollectionRowsAlongsideStaticLibraries() {
        var collectionId = Guid.NewGuid();
        var entities = new FakeEntityReadService();
        entities.ListByKind["collection"] = [Thumb(collectionId, EntityKind.Collection, "Favorites")];
        var catalog = new JellyfinCatalogService(entities, new FakeCollections());

        var result = await catalog.GetItemsAsync(
            Query(parentId: null),
            ServerId,
            hideNsfw: false,
            CancellationToken.None);

        Assert.Contains(result.Items, item =>
            item.Id == JellyfinCatalogService.MoviesViewId &&
            item.Type == JellyfinProtocol.ItemTypes.CollectionFolder);
        Assert.Contains(result.Items, item =>
            item.Id == collectionId &&
            item.Type == JellyfinProtocol.ItemTypes.BoxSet);
        Assert.DoesNotContain(result.Items, item => item.Id == JellyfinCatalogService.CollectionsViewId);
    }

    [Fact]
    public async Task CatalogBrowseSerializesStrictClientTaxonomyArraysAsEmptyArraysWhenAbsent() {
        var videoId = Guid.NewGuid();
        var artistId = Guid.NewGuid();
        var entities = new FakeEntityReadService();
        entities.ListByKind[EntityKindRegistry.Video.Code] = [
            Thumb(videoId, EntityKind.Video, "Standalone Video") with { Genres = ["Adventure"] }
        ];
        entities.ListByKind[EntityKindRegistry.MusicArtist.Code] = [
            Thumb(artistId, EntityKind.MusicArtist, "Empty Artist")
        ];
        var catalog = new JellyfinCatalogService(entities, new FakeCollections());

        var root = await catalog.GetItemsAsync(Query(parentId: null), ServerId, hideNsfw: false, CancellationToken.None);
        using var rootJson = ToJsonDocument(root);
        var rootItem = JellyfinItems(rootJson).First();
        AssertStrictClientListFieldsAreArrays(rootItem);
        AssertEmptyStrictClientTaxonomyArrays(rootItem);

        var videos = await catalog.GetItemsAsync(Query(parentId: JellyfinCatalogService.VideosViewId), ServerId, hideNsfw: false, CancellationToken.None);
        using var videosJson = ToJsonDocument(videos);
        var videoItem = Assert.Single(JellyfinItems(videosJson));
        AssertStrictClientListFieldsAreArrays(videoItem);
        AssertEmptyStrictClientTaxonomyArrays(videoItem);

        var music = await catalog.GetItemsAsync(Query(parentId: JellyfinCatalogService.MusicViewId), ServerId, hideNsfw: false, CancellationToken.None);
        using var musicJson = ToJsonDocument(music);
        var musicItem = Assert.Single(JellyfinItems(musicJson));
        AssertStrictClientListFieldsAreArrays(musicItem);
        AssertEmptyStrictClientTaxonomyArrays(musicItem);
    }

    [Fact]
    public async Task BrowsingUnwatchedMoviesUsesPlayedFilter() {
        var unwatchedMovieId = Guid.NewGuid();
        var watchedMovieId = Guid.NewGuid();
        var entities = new FakeEntityReadService();
        entities.ListByKind["movie"] = [
            Thumb(unwatchedMovieId, EntityKind.Movie, "Unwatched Movie"),
            Thumb(watchedMovieId, EntityKind.Movie, "Watched Movie")
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
            Thumb(unwatchedMovieId, EntityKind.Movie, "Unwatched Movie"),
            Thumb(watchedMovieId, EntityKind.Movie, "Watched Movie")
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
    public async Task PlaybackShelvesHydratePlayableMediaSourcesFromDetail() {
        var videoId = Guid.NewGuid();
        var sourcePath = Path.Combine(Path.GetTempPath(), $"prismedia-jellyfin-source-{Guid.NewGuid():N}.mkv");
        await File.WriteAllBytesAsync(sourcePath, [1, 2, 3, 4, 5, 6]);
        try {
            var entities = new FakeEntityReadService();
            entities.ListByKind["video"] = [
                Thumb(videoId, EntityKind.Video, "Neighbours") with {
                    Progress = 0.42,
                    Meta = [
                        new EntityThumbnailMeta("duration", "42:00"),
                        new EntityThumbnailMeta("video", "1080p"),
                        new EntityThumbnailMeta("video", "H264"),
                        new EntityThumbnailMeta("video", "mkv")
                    ]
                }
            ];
            entities.Cards[videoId] = new EntityCard {
                Id = videoId,
                Kind = EntityKind.Video,
                Title = "Neighbours",
                ParentEntityId = null,
                SortOrder = null,
                Capabilities = [
                    new DescriptionCapability("Detail-only description should stay off shelf rows."),
                    new TechnicalCapability(TimeSpan.FromMinutes(42), 1920, 1080, 23.976, 4_000_000, 48_000, 2, "h264", "mkv", "matroska"),
                    new FilesCapability([new Contracts.Entities.EntityFile("source", sourcePath, "video/x-matroska")]),
                    new PlaybackCapability(0, 0, 0, 60, DateTimeOffset.Parse("2026-06-17T18:42:00Z"), null)
                ],
                ChildrenByKind = [],
                Relationships = []
            };
            var catalog = new JellyfinCatalogService(entities, new FakeCollections());

            var resume = await catalog.GetResumeAsync(0, 10, ServerId, hideNsfw: false, CancellationToken.None);
            var latest = await catalog.GetLatestAsync(null, 10, ServerId, hideNsfw: false, CancellationToken.None);

            AssertHydratedPlayableShelfItem(Assert.Single(resume.Items), sourcePath);
            AssertHydratedPlayableShelfItem(Assert.Single(latest.Items), sourcePath);
            Assert.Contains(entities.DetailCalls, id => id == videoId);
        } finally {
            File.Delete(sourcePath);
        }
    }

    [Fact]
    public async Task BrowsingUnwatchedSeriesFiltersSeriesRowsNotEpisodes() {
        var unwatchedSeriesId = Guid.NewGuid();
        var watchedSeriesId = Guid.NewGuid();
        var entities = new FakeEntityReadService();
        entities.ListByKind["video-series"] = [
            Thumb(unwatchedSeriesId, EntityKind.VideoSeries, "Unwatched Show"),
            Thumb(watchedSeriesId, EntityKind.VideoSeries, "Watched Show")
        ];
        entities.PlayedById[unwatchedSeriesId] = false;
        entities.PlayedById[watchedSeriesId] = true;
        entities.Cards[unwatchedSeriesId] = new EntityCard {
            Id = unwatchedSeriesId,
            Kind = EntityKind.VideoSeries,
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
        entities.ListByKind["movie"] = [Thumb(movieId, EntityKind.Movie, "Some Movie")];
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
        entities.ListByKind["video-series"] = [Thumb(seriesId, EntityKind.VideoSeries, "A Show")];
        entities.Cards[seriesId] = new EntityCard {
            Id = seriesId,
            Kind = EntityKind.VideoSeries,
            Title = "A Show",
            ParentEntityId = null,
            SortOrder = null,
            Capabilities = [],
            ChildrenByKind = [new EntityGroup(EntityKind.VideoSeason, "Seasons", [Thumb(seasonId, EntityKind.VideoSeason, "Season 1")])],
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
    public async Task SeriesWithDirectEpisodesAndNoSeasonsReturnsFallbackSeasonNamedForSeries() {
        var seriesId = Guid.NewGuid();
        var episodeId = Guid.NewGuid();
        var entities = new FakeEntityReadService();
        entities.Cards[seriesId] = Card(
            seriesId,
            EntityKind.VideoSeries,
            "Direct Show",
            parentId: null,
            children: [new EntityGroup(EntityKind.Video, "Episodes", [Thumb(episodeId, EntityKind.Video, "Episode 1", parentId: seriesId)])]);
        var catalog = new JellyfinCatalogService(entities, new FakeCollections());

        var result = await catalog.GetItemsAsync(
            Query(parentId: seriesId, includeItemTypes: [JellyfinProtocol.ItemTypes.Season]),
            ServerId,
            hideNsfw: false,
            CancellationToken.None);

        var season = Assert.Single(result.Items);
        Assert.NotEqual(seriesId, season.Id);
        Assert.Equal("Direct Show", season.Name);
        Assert.Equal(JellyfinProtocol.ItemTypes.Season, season.Type);
        Assert.Equal(seriesId, season.ParentId);
        Assert.Equal(seriesId, season.SeriesId);
        Assert.Equal("Direct Show", season.SeriesName);
        Assert.Equal(1, season.ChildCount);
    }

    [Fact]
    public async Task FallbackSeasonListsDirectEpisodesWhenRequestedBySeasonId() {
        var seriesId = Guid.NewGuid();
        var episodeId = Guid.NewGuid();
        var entities = new FakeEntityReadService();
        entities.Cards[seriesId] = Card(
            seriesId,
            EntityKind.VideoSeries,
            "Direct Show",
            parentId: null,
            children: [new EntityGroup(EntityKind.Video, "Episodes", [Thumb(episodeId, EntityKind.Video, "Episode 1", parentId: seriesId)])]);
        var catalog = new JellyfinCatalogService(entities, new FakeCollections());
        var seasons = await catalog.GetItemsAsync(
            Query(parentId: seriesId, includeItemTypes: [JellyfinProtocol.ItemTypes.Season]),
            ServerId,
            hideNsfw: false,
            CancellationToken.None);
        var fallbackSeasonId = Assert.Single(seasons.Items).Id;

        var result = await catalog.GetItemsAsync(
            Query(parentId: fallbackSeasonId, includeItemTypes: [JellyfinProtocol.ItemTypes.Episode]),
            ServerId,
            hideNsfw: false,
            CancellationToken.None);

        var episode = Assert.Single(result.Items);
        Assert.Equal(episodeId, episode.Id);
        Assert.Equal(JellyfinProtocol.ItemTypes.Episode, episode.Type);
        Assert.Equal(fallbackSeasonId, episode.ParentId);
        Assert.Equal(seriesId, episode.SeriesId);
        Assert.Equal("Direct Show", episode.SeriesName);
        Assert.Equal(fallbackSeasonId, episode.SeasonId);
        Assert.Equal("Direct Show", episode.SeasonName);
    }

    [Fact]
    public async Task RecursiveSeriesEpisodesWithoutSeasonIdUseFallbackSeasonContext() {
        var seriesId = Guid.NewGuid();
        var firstEpisodeId = Guid.NewGuid();
        var secondEpisodeId = Guid.NewGuid();
        var entities = new FakeEntityReadService();
        entities.Cards[seriesId] = Card(
            seriesId,
            EntityKind.VideoSeries,
            "Direct Show",
            parentId: null,
            children: [new EntityGroup(EntityKind.Video, "Episodes", [
                Thumb(firstEpisodeId, EntityKind.Video, "Episode 1", parentId: seriesId, sortOrder: 0),
                Thumb(secondEpisodeId, EntityKind.Video, "Episode 2", parentId: seriesId, sortOrder: 1)
            ])]);
        var catalog = new JellyfinCatalogService(entities, new FakeCollections());
        var seasons = await catalog.GetItemsAsync(
            Query(parentId: seriesId, includeItemTypes: [JellyfinProtocol.ItemTypes.Season]),
            ServerId,
            hideNsfw: false,
            CancellationToken.None);
        var fallbackSeasonId = Assert.Single(seasons.Items).Id;

        var result = await catalog.GetItemsAsync(
            Query(parentId: seriesId, recursive: true, includeItemTypes: [JellyfinProtocol.ItemTypes.Episode]),
            ServerId,
            hideNsfw: false,
            CancellationToken.None);

        Assert.Collection(result.Items,
            first => {
                Assert.Equal(firstEpisodeId, first.Id);
                Assert.Equal(fallbackSeasonId, first.ParentId);
                Assert.Equal(seriesId, first.SeriesId);
                Assert.Equal("Direct Show", first.SeriesName);
                Assert.Equal(fallbackSeasonId, first.SeasonId);
                Assert.Equal("Direct Show", first.SeasonName);
                Assert.Equal(1, first.ParentIndexNumber);
                Assert.Equal(1, first.IndexNumber);
            },
            second => {
                Assert.Equal(secondEpisodeId, second.Id);
                Assert.Equal(fallbackSeasonId, second.ParentId);
                Assert.Equal(seriesId, second.SeriesId);
                Assert.Equal("Direct Show", second.SeriesName);
                Assert.Equal(fallbackSeasonId, second.SeasonId);
                Assert.Equal("Direct Show", second.SeasonName);
                Assert.Equal(1, second.ParentIndexNumber);
                Assert.Equal(2, second.IndexNumber);
            });
    }

    [Fact]
    public async Task DirectEpisodePrimaryImageTagResolvesToServedImageAsset() {
        var seriesId = Guid.NewGuid();
        var episodeId = Guid.NewGuid();
        const string coverPath = "/assets/videos/direct-episode.jpg";
        var entities = new FakeEntityReadService();
        var episode = Thumb(episodeId, EntityKind.Video, "Episode 1", coverUrl: coverPath, parentId: seriesId);
        entities.Cards[seriesId] = Card(
            seriesId,
            EntityKind.VideoSeries,
            "Direct Show",
            parentId: null,
            children: [new EntityGroup(EntityKind.Video, "Episodes", [episode])]);
        entities.Cards[episodeId] = Card(episodeId, EntityKind.Video, "Episode 1", seriesId, children: []);
        entities.Thumbnails[episodeId] = episode;
        var catalog = new JellyfinCatalogService(entities, new FakeCollections());
        var seasons = await catalog.GetItemsAsync(
            Query(parentId: seriesId, includeItemTypes: [JellyfinProtocol.ItemTypes.Season]),
            ServerId,
            hideNsfw: false,
            CancellationToken.None);
        var fallbackSeasonId = Assert.Single(seasons.Items).Id;

        var episodes = await catalog.GetItemsAsync(
            Query(parentId: fallbackSeasonId, includeItemTypes: [JellyfinProtocol.ItemTypes.Episode]),
            ServerId,
            hideNsfw: false,
            CancellationToken.None);
        var listedEpisode = Assert.Single(episodes.Items);
        Assert.True(listedEpisode.ImageTags!.TryGetValue("Primary", out var tag));

        var asset = await catalog.GetImageAssetAsync(episodeId, "Primary", null, hideNsfw: false, CancellationToken.None);

        Assert.NotNull(asset);
        Assert.Equal(coverPath, asset!.Path);
        Assert.Equal(tag, asset.ImageTag);
    }

    [Fact]
    public async Task DirectEpisodeDetailUsesFallbackSeasonContextWhenSeriesHasNoSeasons() {
        var seriesId = Guid.NewGuid();
        var episodeId = Guid.NewGuid();
        var entities = new FakeEntityReadService();
        var episode = Thumb(episodeId, EntityKind.Video, "Episode 1", parentId: seriesId, sortOrder: 0);
        entities.Cards[seriesId] = Card(
            seriesId,
            EntityKind.VideoSeries,
            "Direct Show",
            parentId: null,
            children: [new EntityGroup(EntityKind.Video, "Episodes", [episode])]);
        entities.Cards[episodeId] = Card(episodeId, EntityKind.Video, "Episode 1", seriesId, children: []) with {
            SortOrder = 0
        };
        var catalog = new JellyfinCatalogService(entities, new FakeCollections());
        var seasons = await catalog.GetItemsAsync(
            Query(parentId: seriesId, includeItemTypes: [JellyfinProtocol.ItemTypes.Season]),
            ServerId,
            hideNsfw: false,
            CancellationToken.None);
        var fallbackSeasonId = Assert.Single(seasons.Items).Id;

        var detail = await catalog.GetItemAsync(episodeId, ServerId, hideNsfw: false, CancellationToken.None);

        Assert.NotNull(detail);
        Assert.Equal(fallbackSeasonId, detail!.ParentId);
        Assert.Equal(seriesId, detail.SeriesId);
        Assert.Equal("Direct Show", detail.SeriesName);
        Assert.Equal(fallbackSeasonId, detail.SeasonId);
        Assert.Equal("Direct Show", detail.SeasonName);
        Assert.Equal(1, detail.ParentIndexNumber);
        Assert.Equal(1, detail.IndexNumber);
    }

    [Fact]
    public async Task SeriesWithRealSeasonDoesNotReturnFallbackSeason() {
        var seriesId = Guid.NewGuid();
        var seasonId = Guid.NewGuid();
        var episodeId = Guid.NewGuid();
        var entities = new FakeEntityReadService();
        entities.Cards[seriesId] = Card(
            seriesId,
            EntityKind.VideoSeries,
            "Seasoned Show",
            parentId: null,
            children: [
                new EntityGroup(EntityKind.VideoSeason, "Seasons", [Thumb(seasonId, EntityKind.VideoSeason, "Season 1", parentId: seriesId)]),
                new EntityGroup(EntityKind.Video, "Episodes", [Thumb(episodeId, EntityKind.Video, "Episode 1", parentId: seriesId)])
            ]);
        entities.Cards[seasonId] = Card(seasonId, EntityKind.VideoSeason, "Season 1", seriesId, children: []);
        var catalog = new JellyfinCatalogService(entities, new FakeCollections());

        var result = await catalog.GetItemsAsync(
            Query(parentId: seriesId, includeItemTypes: [JellyfinProtocol.ItemTypes.Season]),
            ServerId,
            hideNsfw: false,
            CancellationToken.None);

        var season = Assert.Single(result.Items);
        Assert.Equal(seasonId, season.Id);
        Assert.Equal("Season 1", season.Name);
    }

    [Fact]
    public async Task FallbackSeasonIsHiddenWhenDirectEpisodesAreFilteredByProfile() {
        var seriesId = Guid.NewGuid();
        var episodeId = Guid.NewGuid();
        var entities = new FakeEntityReadService();
        entities.Cards[seriesId] = Card(
            seriesId,
            EntityKind.VideoSeries,
            "SFW Profile Show",
            parentId: null,
            children: [new EntityGroup(EntityKind.Video, "Episodes", [Thumb(episodeId, EntityKind.Video, "Hidden Episode", parentId: seriesId, isNsfw: true)])]);
        var catalog = new JellyfinCatalogService(entities, new FakeCollections());

        var result = await catalog.GetItemsAsync(
            Query(parentId: seriesId, includeItemTypes: [JellyfinProtocol.ItemTypes.Season]),
            ServerId,
            hideNsfw: true,
            CancellationToken.None);

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task CollectionWithoutOwnCoverGetsRepresentativePrimaryImage() {
        var collectionId = Guid.NewGuid();
        const string coverPath = "/assets/library/member-poster.jpg";
        var entities = new FakeEntityReadService();
        entities.ListByKind["collection"] = [Thumb(collectionId, EntityKind.Collection, "Favourites", coverUrl: null)];
        entities.Cards[collectionId] = new EntityCard {
            Id = collectionId,
            Kind = EntityKind.Collection,
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
        entities.ListByKind["movie"] = [Thumb(movieId, EntityKind.Movie, "Recent Movie", coverUrl: coverPath)];
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
        entities.ReferencedBy[personId] = [Thumb(movieId, EntityKind.Movie, "Their Movie")];
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
            Kind = EntityKind.Person,
            Title = "A Performer",
            ParentEntityId = null,
            SortOrder = null,
            Capabilities = [],
            ChildrenByKind = [],
            Relationships = []
        };
        entities.ReferencedBy[personId] = [Thumb(seriesId, EntityKind.VideoSeries, "Their Show")];
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
            Kind = EntityKind.Person,
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
        entities.ListByKind["music-artist"] = [Thumb(artistId, EntityKind.MusicArtist, "A Band")];
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
        entities.Cards[artistId] = MusicCard(artistId, EntityKind.MusicArtist, "A Band", parentId: null,
            children: [new EntityGroup(EntityKind.AudioLibrary, "Albums", [MusicThumb(albumId, EntityKind.AudioLibrary, "First Album", artistId, sortOrder: 0)])]);
        entities.Cards[albumId] = MusicCard(albumId, EntityKind.AudioLibrary, "First Album", parentId: artistId, children: []);
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
        entities.Cards[artistId] = MusicCard(artistId, EntityKind.MusicArtist, "A Band", parentId: null, children: []);
        entities.Cards[albumId] = MusicCard(albumId, EntityKind.AudioLibrary, "First Album", parentId: artistId,
            children: [new EntityGroup(EntityKind.AudioTrack, "Tracks", [MusicThumb(trackId, EntityKind.AudioTrack, "Opening Track", albumId, sortOrder: 0)])]);
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

    [Fact]
    public async Task BrowsingAudioCollectionFlattensAlbumMembersToPlaylistTracks() {
        var collectionId = Guid.NewGuid();
        var artistId = Guid.NewGuid();
        var albumId = Guid.NewGuid();
        var trackId = Guid.NewGuid();
        var entities = new FakeEntityReadService();
        entities.Cards[collectionId] = Card(collectionId, EntityKind.Collection, "Mixtape", parentId: null, children: []);
        entities.Cards[artistId] = MusicCard(artistId, EntityKind.MusicArtist, "A Band", parentId: null, children: []);
        entities.Cards[albumId] = MusicCard(albumId, EntityKind.AudioLibrary, "First Album", parentId: artistId,
            children: [new EntityGroup(EntityKind.AudioTrack, "Tracks", [MusicThumb(trackId, EntityKind.AudioTrack, "Opening Track", albumId, sortOrder: 0)])]);
        var collections = new FakeCollections();
        collections.Items[collectionId] = [
            CollectionItem(collectionId, Thumb(albumId, EntityKind.AudioLibrary, "First Album", parentId: artistId))
        ];
        var catalog = new JellyfinCatalogService(entities, collections);

        var result = await catalog.GetItemsAsync(
            Query(parentId: collectionId),
            ServerId,
            hideNsfw: false,
            CancellationToken.None);

        var track = Assert.Single(result.Items);
        Assert.Equal(trackId, track.Id);
        Assert.Equal(JellyfinProtocol.ItemTypes.Audio, track.Type);
        Assert.Equal(collectionId, track.ParentId);
        Assert.Equal("First Album", track.Album);
        Assert.Equal(albumId, track.AlbumId);
        Assert.Equal("A Band", track.AlbumArtist);
    }

    [Fact]
    public async Task BrowsingAlbumAdvertisesAlbumArtworkForTracksBeforeLogoFallback() {
        var artistId = Guid.NewGuid();
        var albumId = Guid.NewGuid();
        var trackId = Guid.NewGuid();
        const string albumCover = "/assets/music/album.jpg";
        var entities = new FakeEntityReadService();
        entities.Cards[artistId] = MusicCard(artistId, EntityKind.MusicArtist, "A Band", parentId: null, children: []);
        entities.Cards[albumId] = MusicCard(albumId, EntityKind.AudioLibrary, "First Album", parentId: artistId,
            children: [new EntityGroup(EntityKind.AudioTrack, "Tracks", [MusicThumb(trackId, EntityKind.AudioTrack, "Opening Track", albumId, sortOrder: 0, coverUrl: null)])]);
        entities.Thumbnails[albumId] = Thumb(albumId, EntityKind.AudioLibrary, "First Album", coverUrl: albumCover, parentId: artistId);
        var catalog = new JellyfinCatalogService(entities, new FakeCollections());

        var result = await catalog.GetItemsAsync(
            Query(parentId: albumId),
            ServerId,
            hideNsfw: false,
            CancellationToken.None);

        var track = Assert.Single(result.Items);
        Assert.True(track.ImageTags.TryGetValue(JellyfinProtocol.ImageTypes.Primary, out var primaryTag));
        Assert.Equal(track.AlbumPrimaryImageTag, primaryTag);
    }

    [Fact]
    public async Task TrackPrimaryImageEndpointFallsBackToAlbumArtworkThenLogoWithoutArtistArtwork() {
        var artistId = Guid.NewGuid();
        var albumId = Guid.NewGuid();
        var trackId = Guid.NewGuid();
        const string albumCover = "/assets/music/album.jpg";
        var entities = new FakeEntityReadService();
        entities.Cards[trackId] = MusicCard(trackId, EntityKind.AudioTrack, "Opening Track", parentId: albumId, children: []);
        entities.Cards[albumId] = MusicCard(albumId, EntityKind.AudioLibrary, "First Album", parentId: artistId, children: []);
        entities.Cards[artistId] = MusicCard(artistId, EntityKind.MusicArtist, "A Band", parentId: null, children: []);
        entities.Thumbnails[trackId] = Thumb(trackId, EntityKind.AudioTrack, "Opening Track", coverUrl: null, parentId: albumId);
        entities.Thumbnails[albumId] = Thumb(albumId, EntityKind.AudioLibrary, "First Album", coverUrl: albumCover, parentId: artistId);
        entities.Thumbnails[artistId] = Thumb(artistId, EntityKind.MusicArtist, "A Band", coverUrl: "/assets/music/artist.jpg");
        var catalog = new JellyfinCatalogService(entities, new FakeCollections());

        var albumAsset = await catalog.GetImageAssetAsync(trackId, JellyfinProtocol.ImageTypes.Primary, null, hideNsfw: false, CancellationToken.None);

        Assert.NotNull(albumAsset);
        Assert.Equal(albumCover, albumAsset!.Path);

        entities.Thumbnails[albumId] = Thumb(albumId, EntityKind.AudioLibrary, "First Album", coverUrl: null, parentId: artistId);
        var logoAsset = await catalog.GetImageAssetAsync(trackId, JellyfinProtocol.ImageTypes.Primary, null, hideNsfw: false, CancellationToken.None);

        Assert.NotNull(logoAsset);
        Assert.Equal("/brand/prismedia-logo.png", logoAsset!.Path);
        Assert.NotEqual("/assets/music/artist.jpg", logoAsset.Path);
    }

    private static EntityCard Card(
        Guid id,
        EntityKind kind,
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

    private static EntityCard MusicCard(
        Guid id,
        EntityKind kind,
        string title,
        Guid? parentId,
        IReadOnlyList<EntityGroup> children) =>
        Card(id, kind, title, parentId, children);

    private static EntityThumbnail MusicThumb(Guid id, EntityKind kind, string title, Guid parentId, int sortOrder, string? coverUrl = "/assets/cover.jpg") =>
        new(
            id,
            kind,
            title,
            ParentEntityId: parentId,
            SortOrder: sortOrder,
            CoverUrl: coverUrl,
            CoverThumbUrl: null,
            HoverKind: ThumbnailHoverKind.None,
            HoverUrl: null,
            HoverImages: [],
            Meta: [new EntityThumbnailMeta("duration", "03:20")],
            Rating: null,
            IsFavorite: false,
            IsNsfw: false,
            IsOrganized: true);

    private static JellyfinItemQuery Query(
        Guid? parentId = null,
        IReadOnlyList<Guid>? personIds = null,
        bool recursive = false,
        IReadOnlyList<string>? includeItemTypes = null) =>
        new(
            parentId,
            [],
            Recursive: recursive,
            SearchTerm: null,
            IncludeItemTypes: includeItemTypes ?? [],
            StartIndex: 0,
            Limit: null,
            SortBy: null,
            SortOrder: null,
            IsFavorite: null,
            IsPlayed: null,
            PersonIds: personIds ?? []);

    private static EntityThumbnail Thumb(
        Guid id,
        EntityKind kind,
        string title,
        string? coverUrl = "/assets/cover.jpg",
        Guid? parentId = null,
        bool isNsfw = false,
        int? sortOrder = null) =>
        new(
            id,
            kind,
            title,
            ParentEntityId: parentId,
            SortOrder: sortOrder,
            coverUrl,
            CoverThumbUrl: null,
            HoverKind: ThumbnailHoverKind.None,
            HoverUrl: null,
            HoverImages: [],
            Meta: [new EntityThumbnailMeta("duration", "01:30")],
            Rating: null,
            IsFavorite: false,
            IsNsfw: isNsfw,
            IsOrganized: true);

    private static CollectionItemDetail CollectionItem(Guid collectionId, EntityThumbnail entity) =>
        new(
            Guid.NewGuid(),
            collectionId,
            entity.Kind,
            entity.Id,
            CollectionItemSource.Manual,
            0,
            DateTimeOffset.UtcNow,
            entity);

    private static JsonDocument ToJsonDocument<T>(T value) =>
        JsonDocument.Parse(JsonSerializer.Serialize(value, JellyfinJson));

    private static JsonElement.ArrayEnumerator JellyfinItems(JsonDocument document) {
        // prism-vocab: external Jellyfin JSON result field asserted at the wire boundary.
        return document.RootElement.GetProperty("Items").EnumerateArray();
    }

    private static void AssertHydratedPlayableShelfItem(JellyfinBaseItemDto item, string sourcePath) {
        Assert.Equal(sourcePath, item.Path);
        Assert.Null(item.Overview);
        Assert.Contains(item.MediaStreams, stream => stream.Type == JellyfinProtocol.MediaTypes.Audio);
        var source = Assert.Single(item.MediaSources);
        Assert.Equal(sourcePath, source.Path);
        Assert.Equal(new FileInfo(sourcePath).Length, source.Size);
        Assert.Contains(source.MediaStreams, stream => stream.Type == JellyfinProtocol.MediaTypes.Audio);
        Assert.Equal(1, source.DefaultAudioStreamIndex);
    }

    private static void AssertEmptyStrictClientTaxonomyArrays(JsonElement item) {
        // prism-vocab: external Jellyfin JSON array fields asserted at the wire boundary.
        foreach (var field in new[] { "Genres", "GenreItems", "Tags" }) {
            var property = AssertJsonArrayField(item, field);
            Assert.Equal(0, property.GetArrayLength());
        }
    }

    private static void AssertStrictClientListFieldsAreArrays(JsonElement item) {
        // prism-vocab: external Jellyfin JSON array fields asserted at the wire boundary.
        foreach (var field in new[] {
            "Genres",
            "GenreItems",
            "Tags",
            "People",
            "Studios",
            "ExternalUrls",
            "RemoteTrailers",
            "Taglines",
            "ProductionLocations",
            "AlbumArtists",
            "Artists",
            "ArtistItems",
            "ParentBackdropImageTags",
            "BackdropImageTags",
            "MediaSources",
            "MediaStreams",
            "Chapters"
        }) {
            AssertJsonArrayField(item, field);
        }
    }

    private static JsonElement AssertJsonArrayField(JsonElement item, string field) {
        Assert.True(item.TryGetProperty(field, out var property), $"Missing Jellyfin field {field}.");
        Assert.Equal(JsonValueKind.Array, property.ValueKind);
        return property;
    }

    private sealed class FakeEntityReadService : IEntityReadService {
        public Dictionary<string, IReadOnlyList<EntityThumbnail>> ListByKind { get; } = new();
        public Dictionary<Guid, IReadOnlyList<EntityThumbnail>> ReferencedBy { get; } = new();
        public Dictionary<Guid, EntityCard> Cards { get; } = new();
        public Dictionary<Guid, EntityThumbnail> Thumbnails { get; } = new();
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
            bool? orphaned = null,
        bool? wanted = null) {
            ListCalls.Add(new ListCall(kind, referencedBy, played));
            IReadOnlyList<EntityThumbnail> items;
            if (referencedBy is { } personId) {
                ReferencedByCalls.Add(personId);
                items = ReferencedBy.GetValueOrDefault(personId) ?? [];
            } else {
                items = ListByKind.GetValueOrDefault(kind ?? string.Empty) ?? [];
            }

            if (hideNsfw == true) {
                items = items.Where(item => !item.IsNsfw).ToArray();
            }

            if (nsfw is { } nsfwFilter) {
                items = items.Where(item => item.IsNsfw == nsfwFilter).ToArray();
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
            Task.FromResult(new EntityThumbnailBatchResponse(ids
                .Select(id => Thumbnails.GetValueOrDefault(id))
                .Where(item => item is not null && (!hideNsfw || !item.IsNsfw))
                .Select(item => item!)
                .ToArray()));

        public Task<IEntityCard?> GetDetailAsync(Guid id, string kind, bool hideNsfw, CancellationToken cancellationToken) {
            DetailCalls.Add(id);
            return Task.FromResult<IEntityCard?>(Cards.GetValueOrDefault(id));
        }

        public sealed record ListCall(string? Kind, Guid? ReferencedBy, bool? Played);
    }

    private sealed class FakeCollections : ICollectionItemReadService {
        public Dictionary<Guid, string> Covers { get; } = new();
        public Dictionary<Guid, IReadOnlyList<CollectionItemDetail>> Items { get; } = new();

        public Task<CollectionItemsResponse> ListItemsAsync(
            Guid collectionId,
            bool hideNsfw,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CollectionItemsResponse(
                (Items.GetValueOrDefault(collectionId) ?? [])
                .Where(item => !hideNsfw || !item.Entity.IsNsfw)
                .ToArray()));

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
