using System.Security.Cryptography;
using System.Text;
using Prismedia.Application.Collections;
using Prismedia.Application.Entities;
using Prismedia.Contracts.Collections;
using Prismedia.Domain.Entities;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Jellyfin;
using Prismedia.Contracts.Media;
using Prismedia.Contracts.Series;
using Prismedia.Contracts.Videos;

namespace Prismedia.Application.Jellyfin;

/// <summary>Maps Prismedia's video-first library model to clean-room Jellyfin-compatible DTOs.</summary>
public sealed partial class JellyfinCatalogService {
    public static readonly Guid RootId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    public static readonly Guid VideosViewId = Guid.Parse("10000000-0000-0000-0000-000000000002");
    public static readonly Guid SeriesViewId = Guid.Parse("10000000-0000-0000-0000-000000000003");
    public static readonly Guid CollectionsViewId = Guid.Parse("10000000-0000-0000-0000-000000000004");
    public static readonly Guid MoviesViewId = Guid.Parse("10000000-0000-0000-0000-000000000005");
    public static readonly Guid MusicViewId = Guid.Parse("10000000-0000-0000-0000-000000000006");
    public static readonly Guid UnwatchedMoviesViewId = Guid.Parse("10000000-0000-0000-0000-000000000007");
    public static readonly Guid UnwatchedSeriesViewId = Guid.Parse("10000000-0000-0000-0000-000000000008");
    private const string PrismediaLogoImagePath = "/brand/prismedia-logo.png";
    private static readonly Guid FallbackSeasonIdMask = Guid.Parse("9f37a1c4-7211-4c37-9c20-93258a57f001");

    /// <summary>The fixed top-level library views, entity kind, and optional forced browse filters.</summary>
    private static readonly LibraryViewDefinition[] RootLibraryViews =
    [
        new(MoviesViewId, "Movies", JellyfinProtocol.CollectionTypes.Movies, "movie", null),
        new(UnwatchedMoviesViewId, "Unwatched Movies", JellyfinProtocol.CollectionTypes.Movies, "movie", false),
        new(VideosViewId, "Videos", JellyfinProtocol.CollectionTypes.HomeVideos, "video", null),
        new(SeriesViewId, "Series", JellyfinProtocol.CollectionTypes.Shows, "video-series", null),
        new(UnwatchedSeriesViewId, "Unwatched Series", JellyfinProtocol.CollectionTypes.Shows, "video-series", false),
        new(MusicViewId, "Music", JellyfinProtocol.CollectionTypes.Music, "music-artist", null)
    ];
    private static readonly LibraryViewDefinition CollectionsLibraryView =
        new(CollectionsViewId, "Collections", JellyfinProtocol.CollectionTypes.BoxSets, "collection", null);

    private const int MaxBrowseItems = 5000;
    private static readonly string[] PremiereDatePriority = [
        "premiere",
        "release",
        "released",
        "air",
        "aired",
        "first-aired",
        "published",
        "date",
        "birth",
        "career-start"
    ];

    private readonly IEntityReadService _entities;
    private readonly ICollectionItemReadService _collections;

    public JellyfinCatalogService(IEntityReadService entities, ICollectionItemReadService collections) {
        _entities = entities;
        _collections = collections;
    }

    /// <summary>
    /// Returns the Jellyfin user views without artwork.
    /// Used where only the view names/ids are needed (e.g. grouping options).
    /// </summary>
    public JellyfinQueryResult<JellyfinBaseItemDto> GetUserViews(string serverId) {
        var views = RootLibraryViews
            .Select(view => VirtualFolder(view.Id, view.Name, view.CollectionType, serverId))
            .ToArray();
        return new JellyfinQueryResult<JellyfinBaseItemDto>(views, views.Length, 0);
    }

    /// <summary>
    /// Returns the Jellyfin user views with a representative poster on each tile, resolved from the
    /// most recently added item in that library so clients render real artwork rather than a folder
    /// icon. Used for the surfaces clients actually display (UserViews, root browse, single view).
    /// </summary>
    public async Task<JellyfinQueryResult<JellyfinBaseItemDto>> GetUserViewsWithArtworkAsync(
        string serverId,
        bool hideNsfw,
        CancellationToken cancellationToken) =>
        await GetUserViewsWithArtworkAsync(serverId, JellyfinContentVisibility.FromHideNsfw(hideNsfw), cancellationToken);

    /// <summary>
    /// Returns the Jellyfin user views with a representative poster on each tile, resolved from the
    /// most recently added item in that library so clients render real artwork rather than a folder
    /// icon. Used for the surfaces clients actually display (UserViews, root browse, single view).
    /// </summary>
    public async Task<JellyfinQueryResult<JellyfinBaseItemDto>> GetUserViewsWithArtworkAsync(
        string serverId,
        JellyfinContentVisibility visibility,
        CancellationToken cancellationToken) {
        var views = new List<JellyfinBaseItemDto>(RootLibraryViews.Length);
        foreach (var view in RootLibraryViews) {
            var cover = await ResolveViewCoverPathAsync(view, visibility, cancellationToken);
            int? childCount = null;
            int? recursiveItemCount = null;
            // Advertise content counts for the Music library so music clients know it is browsable:
            // child count is the artist count, recursive count is the total track count.
            if (view.Id == MusicViewId) {
                childCount = await CountOfKindAsync("music-artist", visibility, cancellationToken);
                recursiveItemCount = await CountOfKindAsync("audio-track", visibility, cancellationToken);
            }

            views.Add(VirtualFolder(view.Id, view.Name, view.CollectionType, serverId, cover, childCount, recursiveItemCount));
        }

        views.AddRange(await RootCollectionsAsync(serverId, visibility, cancellationToken));
        return new JellyfinQueryResult<JellyfinBaseItemDto>(views, views.Count, 0);
    }

    /// <summary>Returns visible collection entities as root-level Jellyfin box sets.</summary>
    private async Task<IReadOnlyList<JellyfinBaseItemDto>> RootCollectionsAsync(
        string serverId,
        JellyfinContentVisibility visibility,
        CancellationToken cancellationToken) {
        var collections = await FetchAllOfKindAsync(CollectionsLibraryView.Kind, visibility, cancellationToken);
        collections = await FillCollectionCoversAsync(collections, visibility, cancellationToken);
        return await MapCollectionThumbnailsAsync(collections, serverId, visibility, cancellationToken);
    }

    /// <summary>
    /// Resolves a representative cover path for a library view by scanning the most recently added
    /// items of its kind for the first one that carries artwork. Collections fall back to a member
    /// cover (the box set rarely has its own). Returns null when nothing in the view has a cover.
    /// </summary>
    private async Task<string?> ResolveViewCoverPathAsync(
        LibraryViewDefinition view,
        JellyfinContentVisibility visibility,
        CancellationToken cancellationToken) {
        if (!visibility.AllowsAny) {
            return null;
        }

        var response = await _entities.ListAsync(
            view.Kind,
            null,
            null,
            visibility.HideNsfw,
            16,
            cancellationToken,
            sort: "added",
            sortDir: "desc",
            played: view.ForcedPlayed,
            nsfw: visibility.NsfwFilter, wanted: false);
        foreach (var thumbnail in response.Items) {
            // The Videos view shows standalone videos only, so prefer a top-level poster for its tile.
            if (view.Kind == "video" && thumbnail.ParentEntityId is not null) {
                continue;
            }

            if (thumbnail.CoverUrl is not null) {
                return thumbnail.CoverUrl;
            }

            if (view.Kind == "collection" &&
                await ResolveCollectionCoverPathAsync(thumbnail.Id, visibility, cancellationToken) is { } memberCover) {
                return memberCover;
            }
        }

        return null;
    }

    /// <summary>Returns Jellyfin's synthetic root folder.</summary>
    public JellyfinBaseItemDto GetRoot(string serverId) =>
        new() {
            Id = RootId,
            Name = "Prismedia",
            ServerId = serverId,
            Type = "AggregateFolder",
            CollectionType = "root",
            IsFolder = true,
            ChildCount = RootLibraryViews.Length,
            RecursiveItemCount = RootLibraryViews.Length,
            Etag = EtagFor(RootId, "root"),
            UserData = UserDataFor(RootId, isFavorite: false, playback: null)
        };

    /// <summary>Browses Jellyfin-compatible items.</summary>
    public async Task<JellyfinQueryResult<JellyfinBaseItemDto>> GetItemsAsync(
        JellyfinItemQuery query,
        string serverId,
        bool hideNsfw,
        CancellationToken cancellationToken) =>
        await GetItemsAsync(query, serverId, JellyfinContentVisibility.FromHideNsfw(hideNsfw), cancellationToken);

    /// <summary>Browses Jellyfin-compatible items.</summary>
    public async Task<JellyfinQueryResult<JellyfinBaseItemDto>> GetItemsAsync(
        JellyfinItemQuery query,
        string serverId,
        JellyfinContentVisibility visibility,
        CancellationToken cancellationToken) {
        IReadOnlyList<JellyfinBaseItemDto> items;
        if (query.Ids.Count > 0) {
            items = await ItemsByIdAsync(query.Ids, serverId, visibility, cancellationToken);
        } else if (query.PersonIds.Count > 0) {
            items = await FilmographyAsync(query.PersonIds[0], query, serverId, visibility, cancellationToken);
        } else if (query.Recursive && RequestsMusicTypes(query.IncludeItemTypes) &&
            await RecursiveMusicItemsAsync(query, serverId, visibility, cancellationToken) is { } musicItems) {
            // Music clients fetch flat library lists with Recursive=true (e.g. all albums or all songs),
            // either globally or scoped to the Music view/an artist. The structural-children browse
            // below only yields a parent's immediate children, so these recursive queries are served
            // by flattening the artist/album/track tree to the requested level.
            items = musicItems;
        } else if (query.ParentId is null || query.ParentId == RootId) {
            items = (await GetUserViewsWithArtworkAsync(serverId, visibility, cancellationToken)).Items;
        } else {
            items = await ChildrenOfAsync(query.ParentId.Value, query, serverId, visibility, cancellationToken);
        }

        items = ApplyItemTypeFilter(items, query.IncludeItemTypes);
        items = ApplyPlayedFilter(items, query.IsPlayed);
        var total = items.Count;
        var start = Math.Clamp(query.StartIndex, 0, total);
        var limit = Math.Clamp(query.Limit ?? total, 0, total);
        // Playable rows (movies, videos, episodes) are returned straight from the thumbnail
        // projection — it already carries poster, runtime, container, a synthetic media source, and
        // watched/resume state. Re-hydrating every playable row with its full detail graph was the
        // dominant cost that made Infuse library browsing slow; full detail (overview, people,
        // genres, chapters, real streams) is reserved for the single-item fetch (GetItemAsync).
        // Only folder containers are hydrated here, and only for their structural child counts.
        var page = await HydrateFolderContainersAsync(
            items.Skip(start).Take(limit).ToArray(),
            serverId,
            visibility,
            cancellationToken);
        return new JellyfinQueryResult<JellyfinBaseItemDto>(page, total, start);
    }

    /// <summary>Gets one Jellyfin-compatible item by id.</summary>
    public async Task<JellyfinBaseItemDto?> GetItemAsync(
        Guid id,
        string serverId,
        bool hideNsfw,
        CancellationToken cancellationToken) =>
        await GetItemAsync(id, serverId, JellyfinContentVisibility.FromHideNsfw(hideNsfw), cancellationToken);

    /// <summary>Gets one Jellyfin-compatible item by id.</summary>
    public async Task<JellyfinBaseItemDto?> GetItemAsync(
        Guid id,
        string serverId,
        JellyfinContentVisibility visibility,
        CancellationToken cancellationToken) {
        if (id == RootId) {
            return GetRoot(serverId);
        }

        foreach (var view in AllLibraryViews) {
            if (view.Id == id) {
                var cover = await ResolveViewCoverPathAsync(view, visibility, cancellationToken);
                return VirtualFolder(view.Id, view.Name, view.CollectionType, serverId, cover);
            }
        }

        var entity = await GetDetailedCardAsync(id, visibility, cancellationToken);
        if (entity is null) {
            return null;
        }

        // When an episode is fetched directly (detail page) rather than listed under its parent,
        // resolve its series/season context so the parent artwork (backdrop, logo, series poster)
        // and SeriesId/SeasonId are populated — Infuse renders the episode hero from the parent
        // backdrop, so without this the detail page shows a blank backdrop.
        var context = await ResolveStandaloneContextAsync(entity, visibility, cancellationToken);
        return await MapDetailAsync(entity, serverId, context, visibility, cancellationToken);
    }

    /// <summary>
    /// Maps a detail card to a Jellyfin item, loading the playable video child for a movie so its
    /// technical metadata, streams, and source path populate while the movie folder supplies artwork.
    /// </summary>
    private async Task<JellyfinBaseItemDto> MapDetailAsync(
        IEntityCard detail,
        string serverId,
        ItemContext? context,
        JellyfinContentVisibility visibility,
        CancellationToken cancellationToken) {
        if (detail.Kind == EntityKind.Collection) {
            return await MapCollectionDetailAsync(detail, serverId, context, visibility, cancellationToken);
        }

        if (detail.Kind != EntityKind.Movie) {
            return MapCard(detail, serverId, context);
        }

        var childVideoId = detail.ChildrenByKind
            .SelectMany(group => VisibleEntities(group.Entities, visibility))
            .FirstOrDefault(child => child.Kind == EntityKind.Video)?.Id;
        var playableChild = childVideoId is { } id
            ? await GetDetailedCardAsync(id, visibility, cancellationToken)
            : null;
        return MapCard(detail, serverId, context, playableChild);
    }

    /// <summary>
    /// Builds parent context for an item fetched on its own, by walking up to its structural parent:
    /// series/season for episodes, and album (plus album artist) for tracks and albums. Returns null
    /// for items that carry no parent context (movies, top-level artists).
    /// </summary>
    private async Task<ItemContext?> ResolveStandaloneContextAsync(
        IEntityCard entity,
        JellyfinContentVisibility visibility,
        CancellationToken cancellationToken) {
        var resolvesParent =
            entity.Kind == EntityKind.Video ||
            entity.Kind == EntityKind.AudioTrack ||
            entity.Kind == EntityKind.AudioLibrary;
        if (!resolvesParent || entity.ParentEntityId is not { } parentId) {
            return null;
        }

        var parent = await GetVisibleCardAsync(parentId, visibility, cancellationToken);
        if (parent is null) {
            return null;
        }

        if (entity.Kind == EntityKind.Video &&
            parent.Kind == EntityKind.VideoSeries) {
            var children = parent.ChildrenByKind
                .SelectMany(group => VisibleEntities(group.Entities, visibility))
                .Where(child => child.ParentEntityId == parent.Id)
                .ToArray();
            if (!children.Any(child => child.Kind == EntityKind.VideoSeason)) {
                var directEpisodes = DirectSeriesEpisodeThumbnails(parent, children);
                var episodeIndex = directEpisodes
                    .Select((episode, index) => (episode, index))
                    .FirstOrDefault(entry => entry.episode.Id == entity.Id);
                if (episodeIndex.episode is not null) {
                    return FallbackSeasonContextFor(
                        parent,
                        FallbackEpisodeIndexNumber(episodeIndex.episode.SortOrder, episodeIndex.index));
                }
            }
        }

        return await ParentContextForAsync(parent, visibility, cancellationToken);
    }

    /// <summary>Returns recently added playable videos.</summary>
    public async Task<JellyfinQueryResult<JellyfinBaseItemDto>> GetLatestAsync(
        Guid? parentId,
        int limit,
        string serverId,
        bool hideNsfw,
        CancellationToken cancellationToken) =>
        await GetLatestAsync(parentId, limit, serverId, JellyfinContentVisibility.FromHideNsfw(hideNsfw), cancellationToken);

    /// <summary>Returns recently added playable videos.</summary>
    public async Task<JellyfinQueryResult<JellyfinBaseItemDto>> GetLatestAsync(
        Guid? parentId,
        int limit,
        string serverId,
        JellyfinContentVisibility visibility,
        CancellationToken cancellationToken) {
        if (!visibility.AllowsAny) {
            return new JellyfinQueryResult<JellyfinBaseItemDto>([], 0, 0);
        }

        if (parentId is { } id && ViewById(id) is { } view) {
            var viewResponse = await _entities.ListAsync(
                view.Kind,
                null,
                null,
                visibility.HideNsfw,
                Math.Clamp(limit, 1, 100),
                cancellationToken,
                sort: "added",
                sortDir: "desc",
                played: view.ForcedPlayed,
                nsfw: visibility.NsfwFilter, wanted: false);
            var viewItems = viewResponse.Items;
            if (view.Id == VideosViewId) {
                viewItems = viewItems.Where(item => item.ParentEntityId is null).ToArray();
            } else if (view.Id == CollectionsViewId) {
                viewItems = await FillCollectionCoversAsync(viewItems, visibility, cancellationToken);
            }

            var mapped = view.Id == CollectionsViewId
                ? await MapCollectionThumbnailsAsync(viewItems, serverId, visibility, cancellationToken)
                : await MapPlaybackShelfItemsAsync(viewItems, serverId, visibility, cancellationToken);
            return new JellyfinQueryResult<JellyfinBaseItemDto>(mapped, viewResponse.TotalCount, 0);
        }

        var response = await _entities.ListAsync(
            "video",
            null,
            null,
            visibility.HideNsfw,
            Math.Clamp(limit, 1, 100),
            cancellationToken,
            sort: "added",
            sortDir: "desc",
            nsfw: visibility.NsfwFilter, wanted: false);
        var items = await MapPlaybackShelfItemsAsync(response.Items, serverId, visibility, cancellationToken);
        return new JellyfinQueryResult<JellyfinBaseItemDto>(items, response.TotalCount, 0);
    }

    /// <summary>Returns in-progress videos for Jellyfin resume shelves.</summary>
    public async Task<JellyfinQueryResult<JellyfinBaseItemDto>> GetResumeAsync(
        int startIndex,
        int limit,
        string serverId,
        bool hideNsfw,
        CancellationToken cancellationToken) =>
        await GetResumeAsync(startIndex, limit, serverId, JellyfinContentVisibility.FromHideNsfw(hideNsfw), cancellationToken);

    /// <summary>Returns in-progress videos for Jellyfin resume shelves.</summary>
    public async Task<JellyfinQueryResult<JellyfinBaseItemDto>> GetResumeAsync(
        int startIndex,
        int limit,
        string serverId,
        JellyfinContentVisibility visibility,
        CancellationToken cancellationToken) {
        if (!visibility.AllowsAny) {
            return new JellyfinQueryResult<JellyfinBaseItemDto>([], 0, 0);
        }

        var response = await _entities.ListAsync(
            "video",
            null,
            null,
            visibility.HideNsfw,
            Math.Clamp(startIndex + limit, 1, 500),
            cancellationToken,
            sort: "last-played",
            sortDir: "desc",
            status: "in-progress",
            nsfw: visibility.NsfwFilter, wanted: false);
        var items = await MapPlaybackShelfItemsAsync(response.Items, serverId, visibility, cancellationToken);
        var total = items.Count;
        var start = Math.Clamp(startIndex, 0, total);
        return new JellyfinQueryResult<JellyfinBaseItemDto>(items.Skip(start).Take(limit).ToArray(), total, start);
    }

    /// <summary>
    /// Returns "Next Up" episodes for Jellyfin show shelves. v1 surfaces in-progress episodes
    /// (the internal Continue projection scoped to series content); next-unwatched-after-completed
    /// derivation is a planned enhancement. Movies are excluded — they belong to the resume shelf.
    /// </summary>
    public async Task<JellyfinQueryResult<JellyfinBaseItemDto>> GetNextUpAsync(
        int startIndex,
        int limit,
        string serverId,
        bool hideNsfw,
        CancellationToken cancellationToken) =>
        await GetNextUpAsync(startIndex, limit, serverId, JellyfinContentVisibility.FromHideNsfw(hideNsfw), cancellationToken);

    /// <summary>
    /// Returns "Next Up" episodes for Jellyfin show shelves. v1 surfaces in-progress episodes
    /// (the internal Continue projection scoped to series content); next-unwatched-after-completed
    /// derivation is a planned enhancement. Movies are excluded — they belong to the resume shelf.
    /// </summary>
    public async Task<JellyfinQueryResult<JellyfinBaseItemDto>> GetNextUpAsync(
        int startIndex,
        int limit,
        string serverId,
        JellyfinContentVisibility visibility,
        CancellationToken cancellationToken) {
        if (!visibility.AllowsAny) {
            return new JellyfinQueryResult<JellyfinBaseItemDto>([], 0, 0);
        }

        var response = await _entities.ListAsync(
            "video",
            null,
            null,
            visibility.HideNsfw,
            Math.Clamp(startIndex + limit, 1, 500),
            cancellationToken,
            sort: "last-played",
            sortDir: "desc",
            status: "in-progress",
            nsfw: visibility.NsfwFilter, wanted: false);
        var episodeThumbnails = response.Items
            .Where(item => JellyfinType(item.Kind, item.ParentEntityId).Equals(JellyfinProtocol.ItemTypes.Episode, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var episodes = await MapPlaybackShelfItemsAsync(episodeThumbnails, serverId, visibility, cancellationToken);
        var total = episodes.Count;
        var start = Math.Clamp(startIndex, 0, total);
        return new JellyfinQueryResult<JellyfinBaseItemDto>(episodes.Skip(start).Take(limit).ToArray(), total, start);
    }

    /// <summary>Lists image metadata for one item.</summary>
    public async Task<IReadOnlyList<JellyfinImageInfo>> GetImageInfosAsync(
        Guid id,
        bool hideNsfw,
        CancellationToken cancellationToken) =>
        await GetImageInfosAsync(id, JellyfinContentVisibility.FromHideNsfw(hideNsfw), cancellationToken);

    /// <summary>Lists image metadata for one item.</summary>
    public async Task<IReadOnlyList<JellyfinImageInfo>> GetImageInfosAsync(
        Guid id,
        JellyfinContentVisibility visibility,
        CancellationToken cancellationToken) {
        // Library views are synthetic ids with no entity row; advertise their representative poster.
        if (AllLibraryViews.Any(view => view.Id == id)) {
            return ViewById(id) is { } view &&
                   await ResolveViewCoverPathAsync(view, visibility, cancellationToken) is { } viewCover
                ? [new JellyfinImageInfo(JellyfinProtocol.ImageTypes.Primary, 0, EtagFor(id, viewCover))]
                : [];
        }

        var entity = await GetVisibleCardAsync(id, visibility, cancellationToken);
        if (entity is null) {
            return [];
        }

        var images = ImageAssets(entity.Capabilities);
        var indexesByType = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var infos = images
            .Select(asset => {
                var type = JellyfinImageType(asset.Kind);
                indexesByType.TryGetValue(type, out var index);
                indexesByType[type] = index + 1;
                return new JellyfinImageInfo(type, index, EtagFor(id, asset.Path));
            })
            .ToList();

        if (!indexesByType.ContainsKey(JellyfinProtocol.ImageTypes.Primary) &&
            await ResolveEntityPrimaryCoverPathAsync(entity, visibility, cancellationToken) is { } primaryPath) {
            infos.Insert(0, new JellyfinImageInfo(JellyfinProtocol.ImageTypes.Primary, 0, EtagFor(id, primaryPath)));
            indexesByType[JellyfinProtocol.ImageTypes.Primary] = 1;
        }

        // A collection rarely carries its own poster file; advertise a representative member cover
        // as its Primary image so clients know an image is available to request.
        if (!indexesByType.ContainsKey(JellyfinProtocol.ImageTypes.Primary) &&
            entity.Kind == EntityKind.Collection &&
            await ResolveCollectionCoverPathAsync(id, visibility, cancellationToken) is { } coverPath) {
            infos.Insert(0, new JellyfinImageInfo(JellyfinProtocol.ImageTypes.Primary, 0, EtagFor(id, coverPath)));
        }

        return infos;
    }

    /// <summary>Resolves one item image asset by Jellyfin image type.</summary>
    public async Task<JellyfinImageAsset?> GetImageAssetAsync(
        Guid id,
        string imageType,
        int? imageIndex,
        bool hideNsfw,
        CancellationToken cancellationToken) =>
        await GetImageAssetAsync(id, imageType, imageIndex, JellyfinContentVisibility.FromHideNsfw(hideNsfw), cancellationToken);

    /// <summary>Resolves one item image asset by Jellyfin image type.</summary>
    public async Task<JellyfinImageAsset?> GetImageAssetAsync(
        Guid id,
        string imageType,
        int? imageIndex,
        JellyfinContentVisibility visibility,
        CancellationToken cancellationToken) {
        // Library views are synthetic ids with no entity row; serve their representative poster.
        if (AllLibraryViews.Any(view => view.Id == id)) {
            return imageType.Equals(JellyfinProtocol.ImageTypes.Primary, StringComparison.OrdinalIgnoreCase) &&
                   ViewById(id) is { } view &&
                   await ResolveViewCoverPathAsync(view, visibility, cancellationToken) is { } viewCover
                ? new JellyfinImageAsset(viewCover, MimeTypeForPath(viewCover), JellyfinProtocol.ImageTypes.Primary, EtagFor(id, viewCover))
                : null;
        }

        var entity = await GetVisibleCardAsync(id, visibility, cancellationToken);
        if (entity is null) {
            return null;
        }

        var assets = ImageAssets(entity.Capabilities)
            .Where(asset => JellyfinImageType(asset.Kind).Equals(imageType, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var index = Math.Max(imageIndex ?? 0, 0);
        var asset = assets.ElementAtOrDefault(index);
        if (asset is null) {
            if (index == 0 &&
                imageType.Equals(JellyfinProtocol.ImageTypes.Primary, StringComparison.OrdinalIgnoreCase) &&
                await ResolveEntityPrimaryImageAssetAsync(entity, visibility, cancellationToken) is { } primary) {
                return primary;
            }

            // Serve a collection's representative member cover as its Primary image when it has no
            // poster of its own — matching the tag advertised by the browse/list projection.
            if (entity.Kind == EntityKind.Collection &&
                imageType.Equals(JellyfinProtocol.ImageTypes.Primary, StringComparison.OrdinalIgnoreCase) &&
                await ResolveCollectionCoverPathAsync(id, visibility, cancellationToken) is { } coverPath) {
                return new JellyfinImageAsset(
                    coverPath,
                    MimeTypeForPath(coverPath),
                    JellyfinProtocol.ImageTypes.Primary,
                    EtagFor(id, coverPath));
            }

            return null;
        }

        return new JellyfinImageAsset(
            asset.Path,
            asset.MimeType ?? MimeTypeForPath(asset.Path),
            JellyfinImageType(asset.Kind),
            EtagFor(id, asset.Path));
    }

    private async Task<string?> ResolveEntityPrimaryCoverPathAsync(
        IEntityCard entity,
        JellyfinContentVisibility visibility,
        CancellationToken cancellationToken) {
        var primary = await ResolveEntityPrimaryImageAssetAsync(entity, visibility, cancellationToken);
        return primary?.Path;
    }

    private async Task<JellyfinImageAsset?> ResolveEntityPrimaryImageAssetAsync(
        IEntityCard entity,
        JellyfinContentVisibility visibility,
        CancellationToken cancellationToken) {
        var imageCapability = entity.Capabilities.OfType<ImagesCapability>().FirstOrDefault();
        if (PrimaryImageAsset(ImageAssets(entity.Capabilities), imageCapability) is { } primary) {
            return new JellyfinImageAsset(
                primary.Path,
                primary.MimeType ?? MimeTypeForPath(primary.Path),
                JellyfinProtocol.ImageTypes.Primary,
                EtagFor(entity.Id, primary.Path));
        }

        var thumbnails = await _entities.GetThumbnailsAsync([entity.Id], visibility.HideNsfw, cancellationToken);
        var thumbnail = thumbnails.Items.FirstOrDefault(item => item.Id == entity.Id);
        if (thumbnail is not null && visibility.Allows(thumbnail) && !string.IsNullOrWhiteSpace(thumbnail.CoverUrl)) {
            return new JellyfinImageAsset(
                thumbnail.CoverUrl,
                MimeTypeForPath(thumbnail.CoverUrl),
                JellyfinProtocol.ImageTypes.Primary,
                EtagFor(entity.Id, thumbnail.CoverUrl));
        }

        if (entity.Kind == EntityKind.AudioTrack && entity.ParentEntityId is { } albumId) {
            var albumThumbnails = await _entities.GetThumbnailsAsync([albumId], visibility.HideNsfw, cancellationToken);
            var album = albumThumbnails.Items.FirstOrDefault(item => item.Id == albumId);
            if (album is not null && visibility.Allows(album) && !string.IsNullOrWhiteSpace(album.CoverUrl)) {
                return new JellyfinImageAsset(
                    album.CoverUrl,
                    MimeTypeForPath(album.CoverUrl),
                    JellyfinProtocol.ImageTypes.Primary,
                    EtagFor(album.Id, album.CoverUrl));
            }
        }

        if (IsMusic(entity.Kind)) {
            return new JellyfinImageAsset(
                PrismediaLogoImagePath,
                MediaContentTypes.ImagePng,
                JellyfinProtocol.ImageTypes.Primary,
                EtagFor(entity.Id, PrismediaLogoImagePath));
        }

        return null;
    }

    private async Task<string?> ResolveCollectionCoverPathAsync(
        Guid id,
        JellyfinContentVisibility visibility,
        CancellationToken cancellationToken) {
        if (!visibility.AllowSfw) {
            return null;
        }

        var covers = await _collections.ResolveCoverPathsAsync([id], visibility.HideNsfw, cancellationToken);
        return covers.TryGetValue(id, out var coverPath) ? coverPath : null;
    }

    private async Task<IReadOnlyList<JellyfinBaseItemDto>> ChildrenOfAsync(
        Guid parentId,
        JellyfinItemQuery query,
        string serverId,
        JellyfinContentVisibility visibility,
        CancellationToken cancellationToken) {
        if (ViewById(parentId) is { } view) {
            var thumbnails = await FetchAllThumbnailsAsync(
                view.Kind,
                query,
                visibility,
                cancellationToken,
                view.ForcedPlayed);
            if (view.Id == VideosViewId) {
                thumbnails = thumbnails.Where(item => item.ParentEntityId is null).ToArray();
            } else if (view.Id == CollectionsViewId) {
                thumbnails = await FillCollectionCoversAsync(thumbnails, visibility, cancellationToken);
                return await MapCollectionThumbnailsAsync(thumbnails, serverId, visibility, cancellationToken);
            }

            return thumbnails.Select(item => MapThumbnail(item, serverId)).ToArray();
        }

        var parent = await GetVisibleCardAsync(parentId, visibility, cancellationToken);
        if (parent is null && TrySeriesIdFromFallbackSeasonId(parentId, out var fallbackSeriesId)) {
            return await FallbackSeasonChildrenAsync(fallbackSeriesId, query, serverId, visibility, cancellationToken);
        }

        if (parent is null) {
            return [];
        }

        // Infuse navigates into a cast member by ParentId; surface that performer's titles rather
        // than the empty structural-children list a person entity would otherwise yield.
        if (parent.Kind == EntityKind.Person) {
            return await FilmographyAsync(parentId, query, serverId, visibility, cancellationToken);
        }

        if (parent.Kind == EntityKind.Collection) {
            var items = await _collections.ListItemsAsync(parentId, visibility.HideNsfw, cancellationToken);
            if (IsAudioCapableCollection(items.Items)) {
                return await CollectionPlaylistChildrenAsync(items.Items, parentId, serverId, visibility, cancellationToken);
            }

            return items.Items
                .Where(item => visibility.Allows(item.Entity))
                .Select(item => MapCollectionItem(item, serverId, parentId))
                .Where(item => item is not null)
                .Select(item => item!)
                .ToArray();
        }

        var context = await ParentContextForAsync(parent, visibility, cancellationToken);
        var childThumbnails = parent.ChildrenByKind
            .SelectMany(group => VisibleEntities(group.Entities, visibility))
            .Where(child => query.Recursive || child.ParentEntityId == parentId)
            .ToArray();

        if (!query.Recursive &&
            parent.Kind == EntityKind.VideoSeries &&
            RequestsItemType(query.IncludeItemTypes, JellyfinProtocol.ItemTypes.Season)) {
            var realSeasons = childThumbnails
                .Where(child => child.Kind == EntityKind.VideoSeason)
                .ToArray();
            if (realSeasons.Length == 0) {
                var directEpisodes = DirectSeriesEpisodeThumbnails(parent, childThumbnails);
                if (directEpisodes.Count > 0) {
                    return [FallbackSeasonFolder(parent, serverId, directEpisodes.Count)];
                }
            }
        }

        if (query.Recursive && parent.Kind == EntityKind.VideoSeries) {
            var realSeasons = childThumbnails
                .Where(child => child.Kind == EntityKind.VideoSeason)
                .ToArray();
            if (realSeasons.Length == 0) {
                var directEpisodes = DirectSeriesEpisodeThumbnails(parent, childThumbnails);
                if (directEpisodes.Count > 0) {
                    var fallbackSeasonId = FallbackSeasonIdFor(parent.Id);
                    var fallbackContext = FallbackSeasonContextFor(parent);
                    return directEpisodes
                        .Select((episode, index) => MapFallbackSeasonEpisodeThumbnail(
                            episode,
                            serverId,
                            fallbackSeasonId,
                            fallbackContext,
                            index))
                        .ToArray();
                }
            }

            var descendants = new List<JellyfinBaseItemDto>();
            foreach (var child in childThumbnails) {
                if (child.Kind != EntityKind.VideoSeason) {
                    descendants.Add(MapThumbnail(child, serverId, parentId, context));
                    continue;
                }

                var season = await GetVisibleCardAsync(child.Id, visibility, cancellationToken);
                if (season is null) {
                    continue;
                }

                var seasonContext = await ParentContextForAsync(season, visibility, cancellationToken);
                descendants.AddRange(season.ChildrenByKind
                    .SelectMany(group => VisibleEntities(group.Entities, visibility))
                    .Select(grandchild => MapThumbnail(grandchild, serverId, child.Id, seasonContext)));
            }

            return descendants;
        }

        return childThumbnails.Select(child => MapThumbnail(child, serverId, parentId, context)).ToArray();
    }

    private async Task<IReadOnlyList<JellyfinBaseItemDto>> FallbackSeasonChildrenAsync(
        Guid seriesId,
        JellyfinItemQuery query,
        string serverId,
        JellyfinContentVisibility visibility,
        CancellationToken cancellationToken) {
        var series = await GetVisibleCardAsync(seriesId, visibility, cancellationToken);
        if (series is null || series.Kind != EntityKind.VideoSeries) {
            return [];
        }

        var children = series.ChildrenByKind
            .SelectMany(group => VisibleEntities(group.Entities, visibility))
            .Where(child => child.ParentEntityId == seriesId)
            .ToArray();
        if (children.Any(child => child.Kind == EntityKind.VideoSeason)) {
            return [];
        }

        var episodes = DirectSeriesEpisodeThumbnails(series, children);
        if (episodes.Count == 0) {
            return [];
        }

        var context = FallbackSeasonContextFor(series);
        var seasonId = FallbackSeasonIdFor(seriesId);
        return episodes
            .Select((episode, index) => MapFallbackSeasonEpisodeThumbnail(episode, serverId, seasonId, context, index))
            .ToArray();
    }

    private static JellyfinBaseItemDto MapFallbackSeasonEpisodeThumbnail(
        EntityThumbnail episode,
        string serverId,
        Guid seasonId,
        ItemContext context,
        int index) =>
        MapThumbnail(episode, serverId, seasonId, context) with {
            IndexNumber = FallbackEpisodeIndexNumber(episode.SortOrder, index)
        };

    private static int FallbackEpisodeIndexNumber(int? sortOrder, int index) =>
        sortOrder is >= 0 ? sortOrder.Value + 1 : index + 1;

    private static IReadOnlyList<EntityThumbnail> DirectSeriesEpisodeThumbnails(
        IEntityCard series,
        IReadOnlyList<EntityThumbnail> children) =>
        children
            .Where(child =>
                child.ParentEntityId == series.Id &&
                child.Kind == EntityKind.Video)
            .ToArray();

    /// <summary>
    /// Hydrates only folder-container rows (series and seasons) so their structural child counts
    /// populate, leaving playable leaf rows on the cheap thumbnail projection. Series/season lists
    /// are small, so the targeted hydration here is not the cost the broad list hydration was.
    /// </summary>
    private async Task<IReadOnlyList<JellyfinBaseItemDto>> HydrateFolderContainersAsync(
        IReadOnlyList<JellyfinBaseItemDto> items,
        string serverId,
        JellyfinContentVisibility visibility,
        CancellationToken cancellationToken) {
        if (!items.Any(ShouldHydrateCatalogItem)) {
            return items;
        }

        var hydrated = new List<JellyfinBaseItemDto>(items.Count);
        foreach (var item in items) {
            if (!ShouldHydrateCatalogItem(item)) {
                hydrated.Add(item);
                continue;
            }

            var detail = await GetDetailedCardAsync(item.Id, visibility, cancellationToken);
            hydrated.Add(detail is null
                ? item
                : await MapDetailAsync(detail, serverId, ItemContext.From(item), visibility, cancellationToken));
        }

        return hydrated;
    }

    // Music artists/albums are served straight from the thumbnail projection (which carries
    // DateCreated, album-artist context, and the non-null fields strict clients require) — real
    // Jellyfin does not include child counts on album list items, so hydration is unnecessary and
    // would route them through the detail mapper, which lacks the added-date.
    private static bool ShouldHydrateCatalogItem(JellyfinBaseItemDto item) =>
        item.Type is JellyfinProtocol.ItemTypes.Series or JellyfinProtocol.ItemTypes.Season;

    private async Task<IReadOnlyList<JellyfinBaseItemDto>> MapPlaybackShelfItemsAsync(
        IReadOnlyList<EntityThumbnail> thumbnails,
        string serverId,
        JellyfinContentVisibility visibility,
        CancellationToken cancellationToken) {
        var items = new List<JellyfinBaseItemDto>(thumbnails.Count);
        foreach (var thumbnail in thumbnails) {
            if (!IsPlayable(thumbnail.Kind)) {
                items.Add(MapThumbnail(thumbnail, serverId));
                continue;
            }

            var detail = await GetDetailedCardAsync(thumbnail.Id, visibility, cancellationToken);
            if (detail is null) {
                items.Add(MapThumbnail(thumbnail, serverId));
                continue;
            }

            var shelfItem = MapThumbnail(thumbnail, serverId);
            var context = await ResolveStandaloneContextAsync(detail, visibility, cancellationToken);
            var detailedItem = await MapDetailAsync(detail, serverId, context, visibility, cancellationToken);
            items.Add(WithPlayableSource(shelfItem, detailedItem));
        }

        return items;
    }

    private static JellyfinBaseItemDto WithPlayableSource(
        JellyfinBaseItemDto shelfItem,
        JellyfinBaseItemDto detailedItem) =>
        shelfItem with {
            Path = detailedItem.Path ?? shelfItem.Path,
            Container = detailedItem.Container ?? shelfItem.Container,
            MediaSourceCount = detailedItem.MediaSourceCount ?? shelfItem.MediaSourceCount,
            RunTimeTicks = detailedItem.RunTimeTicks ?? shelfItem.RunTimeTicks,
            Width = detailedItem.Width,
            Height = detailedItem.Height,
            AspectRatio = detailedItem.AspectRatio,
            IsHD = detailedItem.IsHD,
            HasSubtitles = detailedItem.HasSubtitles,
            MediaSources = detailedItem.MediaSources.Count > 0 ? detailedItem.MediaSources : shelfItem.MediaSources,
            MediaStreams = detailedItem.MediaStreams.Count > 0 ? detailedItem.MediaStreams : shelfItem.MediaStreams
        };

    private async Task<IEntityCard?> GetDetailedCardAsync(
        Guid id,
        JellyfinContentVisibility visibility,
        CancellationToken cancellationToken) {
        var card = await GetVisibleCardAsync(id, visibility, cancellationToken);
        if (card is null) {
            return null;
        }

        var detail = await _entities.GetDetailAsync(id, EntityKindRegistry.ToCode(card.Kind), visibility.HideNsfw, cancellationToken);
        return detail is not null && visibility.Allows(detail) ? detail : card;
    }

    private async Task<IReadOnlyList<JellyfinBaseItemDto>> ItemsByIdAsync(
        IReadOnlyList<Guid> ids,
        string serverId,
        JellyfinContentVisibility visibility,
        CancellationToken cancellationToken) {
        var items = new List<JellyfinBaseItemDto>(ids.Count);
        foreach (var id in ids) {
            var item = await GetItemAsync(id, serverId, visibility, cancellationToken);
            if (item is not null) {
                items.Add(item);
            }
        }

        return items;
    }

    private static bool RequestsMusicTypes(IReadOnlyList<string> types) =>
        types.Any(type =>
            type.Equals(JellyfinProtocol.ItemTypes.MusicArtist, StringComparison.OrdinalIgnoreCase) ||
            type.Equals(JellyfinProtocol.ItemTypes.MusicAlbum, StringComparison.OrdinalIgnoreCase) ||
            type.Equals(JellyfinProtocol.ItemTypes.Audio, StringComparison.OrdinalIgnoreCase));

    private static bool RequestsItemType(IReadOnlyList<string> types, string itemType) =>
        types.Any(type => type.Equals(itemType, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Serves a recursive, flat music query (artists, albums, and/or songs) the way Jellyfin music
    /// clients expect: a single list of the requested item types across the whole library, or scoped
    /// to a music artist or album subtree. Albums and tracks are enriched with their album/artist
    /// references so the flat rows are self-describing. Returns null when the requested parent is a
    /// non-music subtree, so the caller falls back to the standard structural browse.
    /// </summary>
    private async Task<IReadOnlyList<JellyfinBaseItemDto>?> RecursiveMusicItemsAsync(
        JellyfinItemQuery query,
        string serverId,
        JellyfinContentVisibility visibility,
        CancellationToken cancellationToken) {
        var types = new HashSet<string>(query.IncludeItemTypes, StringComparer.OrdinalIgnoreCase);
        var wantArtists = types.Contains(JellyfinProtocol.ItemTypes.MusicArtist);
        var wantAlbums = types.Contains(JellyfinProtocol.ItemTypes.MusicAlbum);
        var wantSongs = types.Contains(JellyfinProtocol.ItemTypes.Audio);

        // Resolve scope: global for null/root/library-view parents, else the music entity's subtree.
        Guid? scopeArtist = null;
        Guid? scopeAlbum = null;
        if (query.ParentId is { } parentId && parentId != RootId && AllLibraryViews.All(view => view.Id != parentId)) {
            var parent = await GetVisibleCardAsync(parentId, visibility, cancellationToken);
            if (parent is null) {
                return [];
            }

            if (parent.Kind == EntityKind.MusicArtist) {
                scopeArtist = parentId;
            } else if (parent.Kind == EntityKind.AudioLibrary) {
                scopeAlbum = parentId;
            } else {
                return null; // non-music subtree — let the normal browse path handle it
            }
        }

        // Lookups (unfiltered by search) so albums/tracks can resolve their album-artist for context.
        var artistById = (await FetchAllOfKindAsync("music-artist", visibility, cancellationToken))
            .ToDictionary(artist => artist.Id);
        var albumById = (await FetchAllOfKindAsync("audio-library", visibility, cancellationToken))
            .ToDictionary(album => album.Id);

        var items = new List<JellyfinBaseItemDto>();

        if (wantArtists && scopeArtist is null && scopeAlbum is null) {
            foreach (var artist in await FetchAllThumbnailsAsync("music-artist", query, visibility, cancellationToken)) {
                items.Add(MapThumbnail(artist, serverId));
            }
        }

        if (wantAlbums && scopeAlbum is null) {
            foreach (var album in await FetchAllThumbnailsAsync("audio-library", query, visibility, cancellationToken)) {
                if (scopeArtist is { } artistScope && album.ParentEntityId != artistScope) {
                    continue;
                }

                items.Add(MapThumbnail(album, serverId, album.ParentEntityId, AlbumArtistContext(album, artistById)));
            }
        }

        if (wantSongs) {
            foreach (var song in await FetchAllThumbnailsAsync("audio-track", query, visibility, cancellationToken)) {
                var album = song.ParentEntityId is { } albumId && albumById.TryGetValue(albumId, out var found) ? found : null;
                if (scopeAlbum is { } albumScope && song.ParentEntityId != albumScope) {
                    continue;
                }

                if (scopeArtist is { } artistScope && album?.ParentEntityId != artistScope) {
                    continue;
                }

                items.Add(MapThumbnail(song, serverId, song.ParentEntityId, TrackContext(song, album, artistById)));
            }
        }

        return items;
    }

    private static ItemContext AlbumArtistContext(
        EntityThumbnail album,
        IReadOnlyDictionary<Guid, EntityThumbnail> artistById) {
        var artist = album.ParentEntityId is { } artistId && artistById.TryGetValue(artistId, out var found) ? found : null;
        return new ItemContext(
            null, null, null, null, null,
            ParentId: album.ParentEntityId,
            AlbumArtistId: artist?.Id,
            AlbumArtistName: artist?.Title);
    }

    private static ItemContext TrackContext(
        EntityThumbnail track,
        EntityThumbnail? album,
        IReadOnlyDictionary<Guid, EntityThumbnail> artistById) {
        var artist = album?.ParentEntityId is { } artistId && artistById.TryGetValue(artistId, out var found) ? found : null;
        // Tracks carry no embedded cover of their own, so point album art at the album's primary image
        // (same id+tag the album advertises) — clients render album covers for songs from this.
        var albumPrimaryTag = album?.CoverUrl is { } cover ? EtagFor(album.Id, cover) : null;
        return new ItemContext(
            null, null, null, null, null,
            ParentId: track.ParentEntityId,
            AlbumId: album?.Id,
            AlbumName: album?.Title,
            AlbumPrimaryImageTag: albumPrimaryTag,
            AlbumArtistId: artist?.Id,
            AlbumArtistName: artist?.Title);
    }

    /// <summary>Returns the total number of entities of a kind via a minimal list probe.</summary>
    private async Task<int> CountOfKindAsync(string kind, JellyfinContentVisibility visibility, CancellationToken cancellationToken) {
        if (!visibility.AllowsAny) {
            return 0;
        }

        var response = await _entities.ListAsync(kind, null, null, visibility.HideNsfw, 1, cancellationToken, nsfw: visibility.NsfwFilter, wanted: false);
        return response.TotalCount;
    }

    /// <summary>Lists every entity of a kind (paged internally), unfiltered by the request's search term.</summary>
    private async Task<IReadOnlyList<EntityThumbnail>> FetchAllOfKindAsync(
        string kind,
        JellyfinContentVisibility visibility,
        CancellationToken cancellationToken) {
        if (!visibility.AllowsAny) {
            return [];
        }

        var results = new List<EntityThumbnail>();
        string? cursor = null;
        do {
            var response = await _entities.ListAsync(kind, null, cursor, visibility.HideNsfw, 1000, cancellationToken, nsfw: visibility.NsfwFilter, wanted: false);
            results.AddRange(response.Items);
            cursor = response.NextCursor;
        } while (cursor is not null && results.Count < MaxBrowseItems);

        return results;
    }

    private async Task<IReadOnlyList<EntityThumbnail>> FetchAllThumbnailsAsync(
        string kind,
        JellyfinItemQuery query,
        JellyfinContentVisibility visibility,
        CancellationToken cancellationToken,
        bool? forcedPlayed = null) {
        if (!visibility.AllowsAny) {
            return [];
        }

        var results = new List<EntityThumbnail>();
        string? cursor = null;
        do {
            var response = await _entities.ListAsync(
                kind,
                query.SearchTerm,
                cursor,
                visibility.HideNsfw,
                1000,
                cancellationToken,
                sort: ToPrismediaSort(query.SortBy),
                sortDir: ToPrismediaSortDir(query.SortOrder),
                favorite: query.IsFavorite,
                played: forcedPlayed,
                nsfw: visibility.NsfwFilter, wanted: false);
            results.AddRange(response.Items);
            cursor = response.NextCursor;
        } while (cursor is not null && results.Count < MaxBrowseItems);

        return results;
    }

    /// <summary>
    /// Returns the titles a person appears in (their filmography), resolved through the reverse
    /// relationship index. <c>referencedBy</c> already drops a movie's playable child video in
    /// favour of the movie folder, so the result is movie folders, series, and standalone videos —
    /// never a movie's internal stream.
    /// </summary>
    private async Task<IReadOnlyList<JellyfinBaseItemDto>> FilmographyAsync(
        Guid personId,
        JellyfinItemQuery query,
        string serverId,
        JellyfinContentVisibility visibility,
        CancellationToken cancellationToken) {
        if (!visibility.AllowsAny) {
            return [];
        }

        var results = new List<EntityThumbnail>();
        string? cursor = null;
        do {
            var response = await _entities.ListAsync(
                null,
                query.SearchTerm,
                cursor,
                visibility.HideNsfw,
                1000,
                cancellationToken,
                referencedBy: personId,
                sort: ToPrismediaSort(query.SortBy),
                sortDir: ToPrismediaSortDir(query.SortOrder),
                nsfw: visibility.NsfwFilter, wanted: false);
            results.AddRange(response.Items);
            cursor = response.NextCursor;
        } while (cursor is not null && results.Count < MaxBrowseItems);

        return results.Select(item => MapThumbnail(item, serverId)).ToArray();
    }

    /// <summary>
    /// Fills in a representative cover for collection thumbnails that have none of their own, so
    /// clients that only render an entity's own artwork still show a poster for the box set.
    /// </summary>
    private async Task<IReadOnlyList<EntityThumbnail>> FillCollectionCoversAsync(
        IReadOnlyList<EntityThumbnail> thumbnails,
        JellyfinContentVisibility visibility,
        CancellationToken cancellationToken) {
        if (!visibility.AllowSfw) {
            return thumbnails;
        }

        var missing = thumbnails.Where(item => item.CoverUrl is null).Select(item => item.Id).ToArray();
        if (missing.Length == 0) {
            return thumbnails;
        }

        var covers = await _collections.ResolveCoverPathsAsync(missing, visibility.HideNsfw, cancellationToken);
        if (covers.Count == 0) {
            return thumbnails;
        }

        return thumbnails
            .Select(item => item.CoverUrl is null && covers.TryGetValue(item.Id, out var coverUrl)
                ? item with { CoverUrl = coverUrl }
                : item)
            .ToArray();
    }

    private async Task<JellyfinBaseItemDto> MapCollectionThumbnailAsync(
        EntityThumbnail item,
        string serverId,
        JellyfinContentVisibility visibility,
        CancellationToken cancellationToken) {
        var dto = MapThumbnail(item, serverId);
        var collectionItems = await _collections.ListItemsAsync(item.Id, visibility.HideNsfw, cancellationToken);
        return IsAudioCapableCollection(collectionItems.Items)
            ? AsPlaylistCollection(dto)
            : dto;
    }

    private async Task<IReadOnlyList<JellyfinBaseItemDto>> MapCollectionThumbnailsAsync(
        IReadOnlyList<EntityThumbnail> items,
        string serverId,
        JellyfinContentVisibility visibility,
        CancellationToken cancellationToken) {
        var mapped = new List<JellyfinBaseItemDto>(items.Count);
        foreach (var item in items) {
            mapped.Add(await MapCollectionThumbnailAsync(item, serverId, visibility, cancellationToken));
        }

        return mapped;
    }

    private async Task<JellyfinBaseItemDto> MapCollectionDetailAsync(
        IEntityCard item,
        string serverId,
        ItemContext? context,
        JellyfinContentVisibility visibility,
        CancellationToken cancellationToken) {
        var dto = MapCard(item, serverId, context);
        var collectionItems = await _collections.ListItemsAsync(item.Id, visibility.HideNsfw, cancellationToken);
        if (!IsAudioCapableCollection(collectionItems.Items)) {
            return dto;
        }

        var tracks = await CollectionPlaylistChildrenAsync(collectionItems.Items, item.Id, serverId, visibility, cancellationToken);
        return AsPlaylistCollection(dto, tracks.Count);
    }

    private async Task<IReadOnlyList<JellyfinBaseItemDto>> CollectionPlaylistChildrenAsync(
        IReadOnlyList<CollectionItemDetail> items,
        Guid collectionId,
        string serverId,
        JellyfinContentVisibility visibility,
        CancellationToken cancellationToken) {
        var result = new List<JellyfinBaseItemDto>();
        foreach (var item in items) {
            if (!visibility.Allows(item.Entity)) {
                continue;
            }

            if (item.Entity.Kind == EntityKind.AudioTrack) {
                var context = await TrackContextForThumbnailAsync(item.Entity, visibility, cancellationToken);
                result.Add(MapThumbnail(item.Entity, serverId, collectionId, context));
            } else if (item.Entity.Kind == EntityKind.AudioLibrary) {
                result.AddRange(await CollectionAlbumTracksAsync(item.Entity.Id, collectionId, serverId, visibility, cancellationToken));
            } else if (item.Entity.Kind == EntityKind.MusicArtist) {
                result.AddRange(await CollectionArtistTracksAsync(item.Entity.Id, collectionId, serverId, visibility, cancellationToken));
            }
        }

        return result;
    }

    private async Task<IReadOnlyList<JellyfinBaseItemDto>> CollectionArtistTracksAsync(
        Guid artistId,
        Guid collectionId,
        string serverId,
        JellyfinContentVisibility visibility,
        CancellationToken cancellationToken) {
        var artist = await GetVisibleCardAsync(artistId, visibility, cancellationToken);
        if (artist is null || artist.Kind != EntityKind.MusicArtist) {
            return [];
        }

        var result = new List<JellyfinBaseItemDto>();
        var albums = artist.ChildrenByKind
            .SelectMany(group => VisibleEntities(group.Entities, visibility))
            .Where(child => child.Kind == EntityKind.AudioLibrary)
            .OrderBy(child => child.SortOrder ?? int.MaxValue)
            .ThenBy(child => child.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var album in albums) {
            result.AddRange(await CollectionAlbumTracksAsync(album.Id, collectionId, serverId, visibility, cancellationToken));
        }

        return result;
    }

    private async Task<IReadOnlyList<JellyfinBaseItemDto>> CollectionAlbumTracksAsync(
        Guid albumId,
        Guid collectionId,
        string serverId,
        JellyfinContentVisibility visibility,
        CancellationToken cancellationToken) {
        var album = await GetVisibleCardAsync(albumId, visibility, cancellationToken);
        if (album is null || album.Kind != EntityKind.AudioLibrary) {
            return [];
        }

        var context = await ParentContextForAsync(album, visibility, cancellationToken);
        return album.ChildrenByKind
            .SelectMany(group => VisibleEntities(group.Entities, visibility))
            .Where(child => child.Kind == EntityKind.AudioTrack)
            .OrderBy(child => child.SortOrder ?? int.MaxValue)
            .ThenBy(child => child.Title, StringComparer.OrdinalIgnoreCase)
            .Select(track => MapThumbnail(track, serverId, collectionId, context))
            .ToArray();
    }

    private async Task<ItemContext?> TrackContextForThumbnailAsync(
        EntityThumbnail track,
        JellyfinContentVisibility visibility,
        CancellationToken cancellationToken) {
        if (track.ParentEntityId is not { } albumId) {
            return null;
        }

        var album = await GetVisibleCardAsync(albumId, visibility, cancellationToken);
        return album?.Kind == EntityKind.AudioLibrary
            ? await ParentContextForAsync(album, visibility, cancellationToken)
            : null;
    }

    private static bool IsAudioCapableCollection(IReadOnlyList<CollectionItemDetail> items) =>
        items.Any(item => item.Entity.Kind is EntityKind.AudioTrack or EntityKind.AudioLibrary or EntityKind.MusicArtist);

    private static JellyfinBaseItemDto? MapCollectionItem(CollectionItemDetail item, string serverId, Guid collectionId) =>
        item.Entity.Kind is EntityKind.Video or EntityKind.Movie or EntityKind.VideoSeries or EntityKind.VideoSeason
            ? MapThumbnail(item.Entity, serverId, collectionId)
            : null;

    private static IEnumerable<LibraryViewDefinition> AllLibraryViews =>
        RootLibraryViews.Append(CollectionsLibraryView);

    private static LibraryViewDefinition? ViewById(Guid id) =>
        AllLibraryViews.FirstOrDefault(view => view.Id == id);

    private static Guid FallbackSeasonIdFor(Guid seriesId) =>
        XorGuids(seriesId, FallbackSeasonIdMask);

    private static bool TrySeriesIdFromFallbackSeasonId(Guid seasonId, out Guid seriesId) {
        seriesId = XorGuids(seasonId, FallbackSeasonIdMask);
        return true;
    }

    private static Guid XorGuids(Guid left, Guid right) {
        var leftBytes = left.ToByteArray();
        var rightBytes = right.ToByteArray();
        for (var index = 0; index < leftBytes.Length; index++) {
            leftBytes[index] ^= rightBytes[index];
        }

        return new Guid(leftBytes);
    }

    private async Task<EntityCard?> GetVisibleCardAsync(
        Guid id,
        JellyfinContentVisibility visibility,
        CancellationToken cancellationToken) {
        if (!visibility.AllowsAny) {
            return null;
        }

        var card = await _entities.GetAsync(id, visibility.HideNsfw, cancellationToken);
        return card is not null && visibility.Allows(card) ? card : null;
    }

    private static IReadOnlyList<EntityThumbnail> VisibleEntities(
        IReadOnlyList<EntityThumbnail> items,
        JellyfinContentVisibility visibility) =>
        items.Where(visibility.Allows).ToArray();

    private sealed record LibraryViewDefinition(
        Guid Id,
        string Name,
        string CollectionType,
        string Kind,
        bool? ForcedPlayed);

}
