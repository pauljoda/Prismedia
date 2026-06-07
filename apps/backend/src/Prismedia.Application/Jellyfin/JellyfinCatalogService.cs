using System.Security.Cryptography;
using System.Text;
using Prismedia.Application.Collections;
using Prismedia.Application.Entities;
using Prismedia.Contracts.Collections;
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

    /// <summary>The fixed top-level library views, entity kind, and optional forced browse filters.</summary>
    private static readonly LibraryViewDefinition[] LibraryViews =
    [
        new(MoviesViewId, "Movies", JellyfinProtocol.CollectionTypes.Movies, "movie", null),
        new(UnwatchedMoviesViewId, "Unwatched Movies", JellyfinProtocol.CollectionTypes.Movies, "movie", false),
        new(VideosViewId, "Videos", JellyfinProtocol.CollectionTypes.HomeVideos, "video", null),
        new(SeriesViewId, "Series", JellyfinProtocol.CollectionTypes.Shows, "video-series", null),
        new(UnwatchedSeriesViewId, "Unwatched Series", JellyfinProtocol.CollectionTypes.Shows, "video-series", false),
        new(CollectionsViewId, "Collections", JellyfinProtocol.CollectionTypes.BoxSets, "collection", null),
        new(MusicViewId, "Music", JellyfinProtocol.CollectionTypes.Music, "music-artist", null)
    ];

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
        var views = LibraryViews
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
        CancellationToken cancellationToken) {
        var views = new List<JellyfinBaseItemDto>(LibraryViews.Length);
        foreach (var view in LibraryViews) {
            var cover = await ResolveViewCoverPathAsync(view, hideNsfw, cancellationToken);
            int? childCount = null;
            int? recursiveItemCount = null;
            // Advertise content counts for the Music library so music clients know it is browsable:
            // child count is the artist count, recursive count is the total track count.
            if (view.Id == MusicViewId) {
                childCount = await CountOfKindAsync("music-artist", hideNsfw, cancellationToken);
                recursiveItemCount = await CountOfKindAsync("audio-track", hideNsfw, cancellationToken);
            }

            views.Add(VirtualFolder(view.Id, view.Name, view.CollectionType, serverId, cover, childCount, recursiveItemCount));
        }

        return new JellyfinQueryResult<JellyfinBaseItemDto>(views, views.Count, 0);
    }

    /// <summary>
    /// Resolves a representative cover path for a library view by scanning the most recently added
    /// items of its kind for the first one that carries artwork. Collections fall back to a member
    /// cover (the box set rarely has its own). Returns null when nothing in the view has a cover.
    /// </summary>
    private async Task<string?> ResolveViewCoverPathAsync(
        LibraryViewDefinition view,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        var response = await _entities.ListAsync(
            view.Kind,
            null,
            null,
            hideNsfw,
            16,
            cancellationToken,
            sort: "added",
            sortDir: "desc",
            played: view.ForcedPlayed);
        foreach (var thumbnail in response.Items) {
            // The Videos view shows standalone videos only, so prefer a top-level poster for its tile.
            if (view.Kind == "video" && thumbnail.ParentEntityId is not null) {
                continue;
            }

            if (thumbnail.CoverUrl is not null) {
                return thumbnail.CoverUrl;
            }

            if (view.Kind == "collection" &&
                await ResolveCollectionCoverPathAsync(thumbnail.Id, hideNsfw, cancellationToken) is { } memberCover) {
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
            ChildCount = LibraryViews.Length,
            RecursiveItemCount = LibraryViews.Length,
            Etag = EtagFor(RootId, "root"),
            UserData = UserDataFor(RootId, isFavorite: false, playback: null)
        };

    /// <summary>Browses Jellyfin-compatible items.</summary>
    public async Task<JellyfinQueryResult<JellyfinBaseItemDto>> GetItemsAsync(
        JellyfinItemQuery query,
        string serverId,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        IReadOnlyList<JellyfinBaseItemDto> items;
        if (query.Ids.Count > 0) {
            items = await ItemsByIdAsync(query.Ids, serverId, hideNsfw, cancellationToken);
        } else if (query.PersonIds.Count > 0) {
            items = await FilmographyAsync(query.PersonIds[0], query, serverId, hideNsfw, cancellationToken);
        } else if (query.Recursive && RequestsMusicTypes(query.IncludeItemTypes) &&
            await RecursiveMusicItemsAsync(query, serverId, hideNsfw, cancellationToken) is { } musicItems) {
            // Music clients fetch flat library lists with Recursive=true (e.g. all albums or all songs),
            // either globally or scoped to the Music view/an artist. The structural-children browse
            // below only yields a parent's immediate children, so these recursive queries are served
            // by flattening the artist/album/track tree to the requested level.
            items = musicItems;
        } else if (query.ParentId is null || query.ParentId == RootId) {
            items = (await GetUserViewsWithArtworkAsync(serverId, hideNsfw, cancellationToken)).Items;
        } else {
            items = await ChildrenOfAsync(query.ParentId.Value, query, serverId, hideNsfw, cancellationToken);
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
            hideNsfw,
            cancellationToken);
        return new JellyfinQueryResult<JellyfinBaseItemDto>(page, total, start);
    }

    /// <summary>Gets one Jellyfin-compatible item by id.</summary>
    public async Task<JellyfinBaseItemDto?> GetItemAsync(
        Guid id,
        string serverId,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        if (id == RootId) {
            return GetRoot(serverId);
        }

        foreach (var view in LibraryViews) {
            if (view.Id == id) {
                var cover = await ResolveViewCoverPathAsync(view, hideNsfw, cancellationToken);
                return VirtualFolder(view.Id, view.Name, view.CollectionType, serverId, cover);
            }
        }

        var entity = await GetDetailedCardAsync(id, hideNsfw, cancellationToken);
        if (entity is null) {
            return null;
        }

        // When an episode is fetched directly (detail page) rather than listed under its parent,
        // resolve its series/season context so the parent artwork (backdrop, logo, series poster)
        // and SeriesId/SeasonId are populated — Infuse renders the episode hero from the parent
        // backdrop, so without this the detail page shows a blank backdrop.
        var context = await ResolveStandaloneContextAsync(entity, hideNsfw, cancellationToken);
        return await MapDetailAsync(entity, serverId, context, hideNsfw, cancellationToken);
    }

    /// <summary>
    /// Maps a detail card to a Jellyfin item, loading the playable video child for a movie so its
    /// technical metadata, streams, and source path populate while the movie folder supplies artwork.
    /// </summary>
    private async Task<JellyfinBaseItemDto> MapDetailAsync(
        IEntityCard detail,
        string serverId,
        ItemContext? context,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        if (!detail.Kind.Equals("movie", StringComparison.OrdinalIgnoreCase)) {
            return MapCard(detail, serverId, context);
        }

        var childVideoId = detail.ChildrenByKind
            .SelectMany(group => group.Entities)
            .FirstOrDefault(child => child.Kind.Equals("video", StringComparison.OrdinalIgnoreCase))?.Id;
        var playableChild = childVideoId is { } id
            ? await GetDetailedCardAsync(id, hideNsfw, cancellationToken)
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
        bool hideNsfw,
        CancellationToken cancellationToken) {
        var resolvesParent =
            entity.Kind.Equals("video", StringComparison.OrdinalIgnoreCase) ||
            entity.Kind.Equals("audio-track", StringComparison.OrdinalIgnoreCase) ||
            entity.Kind.Equals("audio-library", StringComparison.OrdinalIgnoreCase);
        if (!resolvesParent || entity.ParentEntityId is not { } parentId) {
            return null;
        }

        var parent = await _entities.GetAsync(parentId, hideNsfw, cancellationToken);
        return parent is null ? null : await ParentContextForAsync(parent, hideNsfw, cancellationToken);
    }

    /// <summary>Returns recently added playable videos.</summary>
    public async Task<JellyfinQueryResult<JellyfinBaseItemDto>> GetLatestAsync(
        Guid? parentId,
        int limit,
        string serverId,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        if (parentId is { } id && ViewById(id) is { } view) {
            var viewResponse = await _entities.ListAsync(
                view.Kind,
                null,
                null,
                hideNsfw,
                Math.Clamp(limit, 1, 100),
                cancellationToken,
                sort: "added",
                sortDir: "desc",
                played: view.ForcedPlayed);
            var viewItems = viewResponse.Items;
            if (view.Id == VideosViewId) {
                viewItems = viewItems.Where(item => item.ParentEntityId is null).ToArray();
            } else if (view.Id == CollectionsViewId) {
                viewItems = await FillCollectionCoversAsync(viewItems, hideNsfw, cancellationToken);
            }

            var mapped = viewItems.Select(item => MapThumbnail(item, serverId)).ToArray();
            return new JellyfinQueryResult<JellyfinBaseItemDto>(mapped, viewResponse.TotalCount, 0);
        }

        var response = await _entities.ListAsync(
            "video",
            null,
            null,
            hideNsfw,
            Math.Clamp(limit, 1, 100),
            cancellationToken,
            sort: "added",
            sortDir: "desc");
        var items = response.Items.Select(item => MapThumbnail(item, serverId)).ToArray();
        return new JellyfinQueryResult<JellyfinBaseItemDto>(items, response.TotalCount, 0);
    }

    /// <summary>Returns in-progress videos for Jellyfin resume shelves.</summary>
    public async Task<JellyfinQueryResult<JellyfinBaseItemDto>> GetResumeAsync(
        int startIndex,
        int limit,
        string serverId,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        var response = await _entities.ListAsync(
            "video",
            null,
            null,
            hideNsfw,
            Math.Clamp(startIndex + limit, 1, 500),
            cancellationToken,
            sort: "last-played",
            sortDir: "desc",
            status: "in-progress");
        var items = response.Items.Select(item => MapThumbnail(item, serverId)).ToArray();
        var total = items.Length;
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
        CancellationToken cancellationToken) {
        var response = await _entities.ListAsync(
            "video",
            null,
            null,
            hideNsfw,
            Math.Clamp(startIndex + limit, 1, 500),
            cancellationToken,
            sort: "last-played",
            sortDir: "desc",
            status: "in-progress");
        var episodes = response.Items
            .Select(item => MapThumbnail(item, serverId))
            .Where(item => item.Type.Equals(JellyfinProtocol.ItemTypes.Episode, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var total = episodes.Length;
        var start = Math.Clamp(startIndex, 0, total);
        return new JellyfinQueryResult<JellyfinBaseItemDto>(episodes.Skip(start).Take(limit).ToArray(), total, start);
    }

    /// <summary>Lists image metadata for one item.</summary>
    public async Task<IReadOnlyList<JellyfinImageInfo>> GetImageInfosAsync(
        Guid id,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        // Library views are synthetic ids with no entity row; advertise their representative poster.
        if (LibraryViews.Any(view => view.Id == id)) {
            return ViewById(id) is { } view &&
                   await ResolveViewCoverPathAsync(view, hideNsfw, cancellationToken) is { } viewCover
                ? [new JellyfinImageInfo("Primary", 0, EtagFor(id, viewCover))]
                : [];
        }

        var entity = await _entities.GetAsync(id, hideNsfw, cancellationToken);
        if (entity is null) {
            return [];
        }

        var indexesByType = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var infos = ImageAssets(entity.Capabilities)
            .Select(asset => {
                var type = JellyfinImageType(asset.Kind);
                indexesByType.TryGetValue(type, out var index);
                indexesByType[type] = index + 1;
                return new JellyfinImageInfo(type, index, EtagFor(id, asset.Path));
            })
            .ToList();

        // A collection rarely carries its own poster file; advertise a representative member cover
        // as its Primary image so clients know an image is available to request.
        if (!indexesByType.ContainsKey("Primary") &&
            entity.Kind.Equals("collection", StringComparison.OrdinalIgnoreCase) &&
            await ResolveCollectionCoverPathAsync(id, hideNsfw, cancellationToken) is { } coverPath) {
            infos.Insert(0, new JellyfinImageInfo("Primary", 0, EtagFor(id, coverPath)));
        }

        return infos;
    }

    /// <summary>Resolves one item image asset by Jellyfin image type.</summary>
    public async Task<JellyfinImageAsset?> GetImageAssetAsync(
        Guid id,
        string imageType,
        int? imageIndex,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        // Library views are synthetic ids with no entity row; serve their representative poster.
        if (LibraryViews.Any(view => view.Id == id)) {
            return imageType.Equals("Primary", StringComparison.OrdinalIgnoreCase) &&
                   ViewById(id) is { } view &&
                   await ResolveViewCoverPathAsync(view, hideNsfw, cancellationToken) is { } viewCover
                ? new JellyfinImageAsset(viewCover, MimeTypeForPath(viewCover), "Primary", EtagFor(id, viewCover))
                : null;
        }

        var entity = await _entities.GetAsync(id, hideNsfw, cancellationToken);
        if (entity is null) {
            return null;
        }

        var assets = ImageAssets(entity.Capabilities)
            .Where(asset => JellyfinImageType(asset.Kind).Equals(imageType, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var asset = assets.ElementAtOrDefault(Math.Max(imageIndex ?? 0, 0));
        if (asset is null) {
            // Serve a collection's representative member cover as its Primary image when it has no
            // poster of its own — matching the tag advertised by the browse/list projection.
            if (entity.Kind.Equals("collection", StringComparison.OrdinalIgnoreCase) &&
                imageType.Equals("Primary", StringComparison.OrdinalIgnoreCase) &&
                await ResolveCollectionCoverPathAsync(id, hideNsfw, cancellationToken) is { } coverPath) {
                return new JellyfinImageAsset(
                    coverPath,
                    MimeTypeForPath(coverPath),
                    "Primary",
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

    private async Task<string?> ResolveCollectionCoverPathAsync(
        Guid id,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        var covers = await _collections.ResolveCoverPathsAsync([id], hideNsfw, cancellationToken);
        return covers.TryGetValue(id, out var coverPath) ? coverPath : null;
    }

    private async Task<IReadOnlyList<JellyfinBaseItemDto>> ChildrenOfAsync(
        Guid parentId,
        JellyfinItemQuery query,
        string serverId,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        if (ViewById(parentId) is { } view) {
            var thumbnails = await FetchAllThumbnailsAsync(
                view.Kind,
                query,
                hideNsfw,
                cancellationToken,
                view.ForcedPlayed);
            if (view.Id == VideosViewId) {
                thumbnails = thumbnails.Where(item => item.ParentEntityId is null).ToArray();
            } else if (view.Id == CollectionsViewId) {
                thumbnails = await FillCollectionCoversAsync(thumbnails, hideNsfw, cancellationToken);
            }

            return thumbnails.Select(item => MapThumbnail(item, serverId)).ToArray();
        }

        var parent = await _entities.GetAsync(parentId, hideNsfw, cancellationToken);
        if (parent is null) {
            return [];
        }

        // Infuse navigates into a cast member by ParentId; surface that performer's titles rather
        // than the empty structural-children list a person entity would otherwise yield.
        if (parent.Kind.Equals("person", StringComparison.OrdinalIgnoreCase)) {
            return await FilmographyAsync(parentId, query, serverId, hideNsfw, cancellationToken);
        }

        if (parent.Kind.Equals("collection", StringComparison.OrdinalIgnoreCase)) {
            var items = await _collections.ListItemsAsync(parentId, hideNsfw, cancellationToken);
            return items.Items
                .Select(item => MapCollectionItem(item, serverId, parentId))
                .Where(item => item is not null)
                .Select(item => item!)
                .ToArray();
        }

        var context = await ParentContextForAsync(parent, hideNsfw, cancellationToken);
        var childThumbnails = parent.ChildrenByKind
            .SelectMany(group => group.Entities)
            .Where(child => query.Recursive || child.ParentEntityId == parentId)
            .ToArray();

        if (query.Recursive && parent.Kind.Equals("video-series", StringComparison.OrdinalIgnoreCase)) {
            var descendants = new List<JellyfinBaseItemDto>();
            foreach (var child in childThumbnails) {
                if (!child.Kind.Equals("video-season", StringComparison.OrdinalIgnoreCase)) {
                    descendants.Add(MapThumbnail(child, serverId, parentId, context));
                    continue;
                }

                var season = await _entities.GetAsync(child.Id, hideNsfw, cancellationToken);
                if (season is null) {
                    continue;
                }

                var seasonContext = await ParentContextForAsync(season, hideNsfw, cancellationToken);
                descendants.AddRange(season.ChildrenByKind
                    .SelectMany(group => group.Entities)
                    .Select(grandchild => MapThumbnail(grandchild, serverId, child.Id, seasonContext)));
            }

            return descendants;
        }

        return childThumbnails.Select(child => MapThumbnail(child, serverId, parentId, context)).ToArray();
    }

    /// <summary>
    /// Hydrates only folder-container rows (series and seasons) so their structural child counts
    /// populate, leaving playable leaf rows on the cheap thumbnail projection. Series/season lists
    /// are small, so the targeted hydration here is not the cost the broad list hydration was.
    /// </summary>
    private async Task<IReadOnlyList<JellyfinBaseItemDto>> HydrateFolderContainersAsync(
        IReadOnlyList<JellyfinBaseItemDto> items,
        string serverId,
        bool hideNsfw,
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

            var detail = await GetDetailedCardAsync(item.Id, hideNsfw, cancellationToken);
            hydrated.Add(detail is null
                ? item
                : await MapDetailAsync(detail, serverId, ItemContext.From(item), hideNsfw, cancellationToken));
        }

        return hydrated;
    }

    // Music artists/albums are served straight from the thumbnail projection (which carries
    // DateCreated, album-artist context, and the non-null fields strict clients require) — real
    // Jellyfin does not include child counts on album list items, so hydration is unnecessary and
    // would route them through the detail mapper, which lacks the added-date.
    private static bool ShouldHydrateCatalogItem(JellyfinBaseItemDto item) =>
        item.Type is JellyfinProtocol.ItemTypes.Series or JellyfinProtocol.ItemTypes.Season;

    private async Task<IEntityCard?> GetDetailedCardAsync(
        Guid id,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        var card = await _entities.GetAsync(id, hideNsfw, cancellationToken);
        if (card is null) {
            return null;
        }

        return await _entities.GetDetailAsync(id, card.Kind, hideNsfw, cancellationToken) ?? card;
    }

    private async Task<IReadOnlyList<JellyfinBaseItemDto>> ItemsByIdAsync(
        IReadOnlyList<Guid> ids,
        string serverId,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        var items = new List<JellyfinBaseItemDto>(ids.Count);
        foreach (var id in ids) {
            var item = await GetItemAsync(id, serverId, hideNsfw, cancellationToken);
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
        bool hideNsfw,
        CancellationToken cancellationToken) {
        var types = new HashSet<string>(query.IncludeItemTypes, StringComparer.OrdinalIgnoreCase);
        var wantArtists = types.Contains(JellyfinProtocol.ItemTypes.MusicArtist);
        var wantAlbums = types.Contains(JellyfinProtocol.ItemTypes.MusicAlbum);
        var wantSongs = types.Contains(JellyfinProtocol.ItemTypes.Audio);

        // Resolve scope: global for null/root/library-view parents, else the music entity's subtree.
        Guid? scopeArtist = null;
        Guid? scopeAlbum = null;
        if (query.ParentId is { } parentId && parentId != RootId && LibraryViews.All(view => view.Id != parentId)) {
            var parent = await _entities.GetAsync(parentId, hideNsfw, cancellationToken);
            if (parent is null) {
                return [];
            }

            if (parent.Kind.Equals("music-artist", StringComparison.OrdinalIgnoreCase)) {
                scopeArtist = parentId;
            } else if (parent.Kind.Equals("audio-library", StringComparison.OrdinalIgnoreCase)) {
                scopeAlbum = parentId;
            } else {
                return null; // non-music subtree — let the normal browse path handle it
            }
        }

        // Lookups (unfiltered by search) so albums/tracks can resolve their album-artist for context.
        var artistById = (await FetchAllOfKindAsync("music-artist", hideNsfw, cancellationToken))
            .ToDictionary(artist => artist.Id);
        var albumById = (await FetchAllOfKindAsync("audio-library", hideNsfw, cancellationToken))
            .ToDictionary(album => album.Id);

        var items = new List<JellyfinBaseItemDto>();

        if (wantArtists && scopeArtist is null && scopeAlbum is null) {
            foreach (var artist in await FetchAllThumbnailsAsync("music-artist", query, hideNsfw, cancellationToken)) {
                items.Add(MapThumbnail(artist, serverId));
            }
        }

        if (wantAlbums && scopeAlbum is null) {
            foreach (var album in await FetchAllThumbnailsAsync("audio-library", query, hideNsfw, cancellationToken)) {
                if (scopeArtist is { } artistScope && album.ParentEntityId != artistScope) {
                    continue;
                }

                items.Add(MapThumbnail(album, serverId, album.ParentEntityId, AlbumArtistContext(album, artistById)));
            }
        }

        if (wantSongs) {
            foreach (var song in await FetchAllThumbnailsAsync("audio-track", query, hideNsfw, cancellationToken)) {
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
    private async Task<int> CountOfKindAsync(string kind, bool hideNsfw, CancellationToken cancellationToken) {
        var response = await _entities.ListAsync(kind, null, null, hideNsfw, 1, cancellationToken);
        return response.TotalCount;
    }

    /// <summary>Lists every entity of a kind (paged internally), unfiltered by the request's search term.</summary>
    private async Task<IReadOnlyList<EntityThumbnail>> FetchAllOfKindAsync(
        string kind,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        var results = new List<EntityThumbnail>();
        string? cursor = null;
        do {
            var response = await _entities.ListAsync(kind, null, cursor, hideNsfw, 1000, cancellationToken);
            results.AddRange(response.Items);
            cursor = response.NextCursor;
        } while (cursor is not null && results.Count < MaxBrowseItems);

        return results;
    }

    private async Task<IReadOnlyList<EntityThumbnail>> FetchAllThumbnailsAsync(
        string kind,
        JellyfinItemQuery query,
        bool hideNsfw,
        CancellationToken cancellationToken,
        bool? forcedPlayed = null) {
        var results = new List<EntityThumbnail>();
        string? cursor = null;
        do {
            var response = await _entities.ListAsync(
                kind,
                query.SearchTerm,
                cursor,
                hideNsfw,
                1000,
                cancellationToken,
                sort: ToPrismediaSort(query.SortBy),
                sortDir: ToPrismediaSortDir(query.SortOrder),
                favorite: query.IsFavorite,
                played: forcedPlayed);
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
        bool hideNsfw,
        CancellationToken cancellationToken) {
        var results = new List<EntityThumbnail>();
        string? cursor = null;
        do {
            var response = await _entities.ListAsync(
                null,
                query.SearchTerm,
                cursor,
                hideNsfw,
                1000,
                cancellationToken,
                referencedBy: personId,
                sort: ToPrismediaSort(query.SortBy),
                sortDir: ToPrismediaSortDir(query.SortOrder));
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
        bool hideNsfw,
        CancellationToken cancellationToken) {
        var missing = thumbnails.Where(item => item.CoverUrl is null).Select(item => item.Id).ToArray();
        if (missing.Length == 0) {
            return thumbnails;
        }

        var covers = await _collections.ResolveCoverPathsAsync(missing, hideNsfw, cancellationToken);
        if (covers.Count == 0) {
            return thumbnails;
        }

        return thumbnails
            .Select(item => item.CoverUrl is null && covers.TryGetValue(item.Id, out var coverUrl)
                ? item with { CoverUrl = coverUrl }
                : item)
            .ToArray();
    }

    private static JellyfinBaseItemDto? MapCollectionItem(CollectionItemDetail item, string serverId, Guid collectionId) =>
        item.Entity.Kind is "video" or "movie" or "video-series" or "video-season"
            ? MapThumbnail(item.Entity, serverId, collectionId)
            : null;

    private static LibraryViewDefinition? ViewById(Guid id) =>
        LibraryViews.FirstOrDefault(view => view.Id == id);

    private sealed record LibraryViewDefinition(
        Guid Id,
        string Name,
        string CollectionType,
        string Kind,
        bool? ForcedPlayed);

}
