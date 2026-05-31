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

    /// <summary>Returns Jellyfin user views for Prismedia's v1 video-first surface.</summary>
    public JellyfinQueryResult<JellyfinBaseItemDto> GetUserViews(string serverId) {
        var views = new[]
        {
            VirtualFolder(MoviesViewId, "Movies", JellyfinProtocol.CollectionTypes.Movies, serverId),
            VirtualFolder(VideosViewId, "Videos", JellyfinProtocol.CollectionTypes.HomeVideos, serverId),
            VirtualFolder(SeriesViewId, "Series", JellyfinProtocol.CollectionTypes.Shows, serverId),
            VirtualFolder(CollectionsViewId, "Collections", JellyfinProtocol.CollectionTypes.BoxSets, serverId)
        };
        return new JellyfinQueryResult<JellyfinBaseItemDto>(views, views.Length, 0);
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
            ChildCount = 4,
            RecursiveItemCount = 4,
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
        } else if (query.ParentId is null || query.ParentId == RootId) {
            items = GetUserViews(serverId).Items;
        } else {
            items = await ChildrenOfAsync(query.ParentId.Value, query, serverId, hideNsfw, cancellationToken);
        }

        items = ApplyItemTypeFilter(items, query.IncludeItemTypes);
        items = ApplyPlayedFilter(items, query.IsPlayed);
        var total = items.Count;
        var start = Math.Clamp(query.StartIndex, 0, total);
        var limit = Math.Clamp(query.Limit ?? total, 0, total);
        var page = await HydrateCatalogItemsAsync(
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

        if (id == MoviesViewId) {
            return VirtualFolder(MoviesViewId, "Movies", JellyfinProtocol.CollectionTypes.Movies, serverId);
        }

        if (id == VideosViewId) {
            return VirtualFolder(VideosViewId, "Videos", JellyfinProtocol.CollectionTypes.HomeVideos, serverId);
        }

        if (id == SeriesViewId) {
            return VirtualFolder(SeriesViewId, "Series", JellyfinProtocol.CollectionTypes.Shows, serverId);
        }

        if (id == CollectionsViewId) {
            return VirtualFolder(CollectionsViewId, "Collections", JellyfinProtocol.CollectionTypes.BoxSets, serverId);
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
    /// Builds parent (series/season) context for an item fetched on its own, by walking up to its
    /// structural parent. Returns null for items that carry no parent artwork (movies, top-level).
    /// </summary>
    private async Task<ItemContext?> ResolveStandaloneContextAsync(
        IEntityCard entity,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        if (!entity.Kind.Equals("video", StringComparison.OrdinalIgnoreCase) ||
            entity.ParentEntityId is not { } parentId) {
            return null;
        }

        var parent = await _entities.GetAsync(parentId, hideNsfw, cancellationToken);
        return parent is null ? null : await ParentContextForAsync(parent, hideNsfw, cancellationToken);
    }

    /// <summary>Returns recently added playable videos.</summary>
    public async Task<JellyfinQueryResult<JellyfinBaseItemDto>> GetLatestAsync(
        int limit,
        string serverId,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        var response = await _entities.ListAsync(
            "video",
            null,
            null,
            hideNsfw,
            Math.Clamp(limit, 1, 100),
            cancellationToken,
            sort: "added",
            sortDir: "desc");
        var items = await HydrateCatalogItemsAsync(
            response.Items.Select(item => MapThumbnail(item, serverId)).ToArray(),
            serverId,
            hideNsfw,
            cancellationToken);
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
            sort: "added",
            sortDir: "desc",
            status: "in-progress");
        var items = await HydrateCatalogItemsAsync(
            response.Items.Select(item => MapThumbnail(item, serverId)).ToArray(),
            serverId,
            hideNsfw,
            cancellationToken);
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
        CancellationToken cancellationToken) {
        var response = await _entities.ListAsync(
            "video",
            null,
            null,
            hideNsfw,
            Math.Clamp(startIndex + limit, 1, 500),
            cancellationToken,
            sort: "added",
            sortDir: "desc",
            status: "in-progress");
        var mapped = response.Items.Select(item => MapThumbnail(item, serverId)).ToArray();
        var episodes = await HydrateCatalogItemsAsync(
            mapped.Where(item => item.Type.Equals(JellyfinProtocol.ItemTypes.Episode, StringComparison.OrdinalIgnoreCase)).ToArray(),
            serverId,
            hideNsfw,
            cancellationToken);
        var total = episodes.Count;
        var start = Math.Clamp(startIndex, 0, total);
        return new JellyfinQueryResult<JellyfinBaseItemDto>(episodes.Skip(start).Take(limit).ToArray(), total, start);
    }

    /// <summary>Lists image metadata for one item.</summary>
    public async Task<IReadOnlyList<JellyfinImageInfo>> GetImageInfosAsync(
        Guid id,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        var entity = await _entities.GetAsync(id, hideNsfw, cancellationToken);
        if (entity is null) {
            return [];
        }

        var indexesByType = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        return ImageAssets(entity.Capabilities)
            .Select(asset => {
                var type = JellyfinImageType(asset.Kind);
                indexesByType.TryGetValue(type, out var index);
                indexesByType[type] = index + 1;
                return new JellyfinImageInfo(type, index, EtagFor(id, asset.Path));
            })
            .ToArray();
    }

    /// <summary>Resolves one item image asset by Jellyfin image type.</summary>
    public async Task<JellyfinImageAsset?> GetImageAssetAsync(
        Guid id,
        string imageType,
        int? imageIndex,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        var entity = await _entities.GetAsync(id, hideNsfw, cancellationToken);
        if (entity is null) {
            return null;
        }

        var assets = ImageAssets(entity.Capabilities)
            .Where(asset => JellyfinImageType(asset.Kind).Equals(imageType, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var asset = assets.ElementAtOrDefault(Math.Max(imageIndex ?? 0, 0));
        if (asset is null) {
            return null;
        }

        return new JellyfinImageAsset(
            asset.Path,
            asset.MimeType ?? MimeTypeForPath(asset.Path),
            JellyfinImageType(asset.Kind),
            EtagFor(id, asset.Path));
    }

    private async Task<IReadOnlyList<JellyfinBaseItemDto>> ChildrenOfAsync(
        Guid parentId,
        JellyfinItemQuery query,
        string serverId,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        if (parentId == MoviesViewId) {
            var thumbnails = await FetchAllThumbnailsAsync("movie", query, hideNsfw, cancellationToken);
            return thumbnails.Select(item => MapThumbnail(item, serverId)).ToArray();
        }

        if (parentId == VideosViewId) {
            var thumbnails = await FetchAllThumbnailsAsync("video", query, hideNsfw, cancellationToken);
            return thumbnails
                .Where(item => item.ParentEntityId is null)
                .Select(item => MapThumbnail(item, serverId))
                .ToArray();
        }

        if (parentId == SeriesViewId) {
            var thumbnails = await FetchAllThumbnailsAsync("video-series", query, hideNsfw, cancellationToken);
            return thumbnails.Select(item => MapThumbnail(item, serverId)).ToArray();
        }

        if (parentId == CollectionsViewId) {
            var thumbnails = await FetchAllThumbnailsAsync("collection", query, hideNsfw, cancellationToken);
            return thumbnails.Select(item => MapThumbnail(item, serverId)).ToArray();
        }

        var parent = await _entities.GetAsync(parentId, hideNsfw, cancellationToken);
        if (parent is null) {
            return [];
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

    private async Task<IReadOnlyList<JellyfinBaseItemDto>> HydrateCatalogItemsAsync(
        IReadOnlyList<JellyfinBaseItemDto> items,
        string serverId,
        bool hideNsfw,
        CancellationToken cancellationToken) {
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

    private static bool ShouldHydrateCatalogItem(JellyfinBaseItemDto item) =>
        item.MediaType?.Equals(JellyfinProtocol.MediaTypes.Video, StringComparison.OrdinalIgnoreCase) == true ||
        item.Type is JellyfinProtocol.ItemTypes.Series or JellyfinProtocol.ItemTypes.Season or JellyfinProtocol.ItemTypes.BoxSet;

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

    private async Task<IReadOnlyList<EntityThumbnail>> FetchAllThumbnailsAsync(
        string kind,
        JellyfinItemQuery query,
        bool hideNsfw,
        CancellationToken cancellationToken) {
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
                favorite: query.IsFavorite);
            results.AddRange(response.Items);
            cursor = response.NextCursor;
        } while (cursor is not null && results.Count < MaxBrowseItems);

        return results;
    }

    private static JellyfinBaseItemDto? MapCollectionItem(CollectionItemDetail item, string serverId, Guid collectionId) =>
        item.Entity.Kind is "video" or "movie" or "video-series" or "video-season"
            ? MapThumbnail(item.Entity, serverId, collectionId)
            : null;

}
