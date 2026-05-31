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
public sealed class JellyfinCatalogService {
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
            VirtualFolder(MoviesViewId, "Movies", "movies", serverId),
            VirtualFolder(VideosViewId, "Videos", "homevideos", serverId),
            VirtualFolder(SeriesViewId, "Series", "tvshows", serverId),
            VirtualFolder(CollectionsViewId, "Collections", "boxsets", serverId)
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
            return VirtualFolder(MoviesViewId, "Movies", "movies", serverId);
        }

        if (id == VideosViewId) {
            return VirtualFolder(VideosViewId, "Videos", "homevideos", serverId);
        }

        if (id == SeriesViewId) {
            return VirtualFolder(SeriesViewId, "Series", "tvshows", serverId);
        }

        if (id == CollectionsViewId) {
            return VirtualFolder(CollectionsViewId, "Collections", "boxsets", serverId);
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
            mapped.Where(item => item.Type.Equals("Episode", StringComparison.OrdinalIgnoreCase)).ToArray(),
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
        item.MediaType?.Equals("Video", StringComparison.OrdinalIgnoreCase) == true ||
        item.Type is "Series" or "Season" or "BoxSet";

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

    private static JellyfinBaseItemDto MapThumbnail(
        EntityThumbnail item,
        string serverId,
        Guid? parentOverride = null,
        ItemContext? context = null) {
        var imageTags = ImageTags(item.Id, item.CoverUrl, null);
        var isPlayable = IsPlayable(item.Kind);
        long? runtimeTicks = isPlayable ? RuntimeTicksFrom(item) ?? 0 : null;
        var container = isPlayable ? ContainerFrom(item) : null;
        var streams = isPlayable ? CatalogStreams(item, container) : null;
        return new JellyfinBaseItemDto {
            Id = item.Id,
            Name = item.Title,
            ServerId = serverId,
            Etag = EtagFor(item.Id, item.CoverUrl ?? item.Title),
            SortName = item.Title,
            Type = JellyfinType(item.Kind, item.ParentEntityId),
            MediaType = isPlayable ? "Video" : null,
            Path = isPlayable ? VirtualItemPath(item.Id) : null,
            LocationType = isPlayable ? "FileSystem" : null,
            PlayAccess = isPlayable ? "Full" : null,
            Container = container,
            MediaSourceCount = isPlayable ? 1 : null,
            SupportsResume = isPlayable ? true : null,
            SupportsSync = isPlayable ? true : null,
            CollectionType = CollectionType(item.Kind),
            ParentId = parentOverride ?? item.ParentEntityId,
            IsFolder = IsFolder(item.Kind),
            ImageTags = imageTags.Primary is null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["Primary"] = imageTags.Primary },
            BackdropImageTags = imageTags.Backdrop is null ? [] : [imageTags.Backdrop],
            PrimaryImageAspectRatio = 0.6667,
            IndexNumber = item.SortOrder,
            ParentIndexNumber = context?.ParentIndexNumber,
            SeriesId = context?.SeriesId,
            SeriesName = context?.SeriesName,
            SeasonId = context?.SeasonId,
            SeasonName = context?.SeasonName,
            SeriesPrimaryImageTag = context?.SeriesPrimaryImageTag,
            ParentLogoItemId = context?.ParentLogoItemId,
            ParentLogoImageTag = context?.ParentLogoImageTag,
            ParentBackdropItemId = context?.ParentBackdropItemId,
            ParentBackdropImageTags = context?.ParentBackdropImageTags,
            ParentThumbItemId = context?.ParentThumbItemId,
            ParentThumbImageTag = context?.ParentThumbImageTag,
            VideoType = isPlayable ? "VideoFile" : null,
            CanDelete = isPlayable ? true : null,
            EnableMediaSourceDisplay = isPlayable ? true : null,
            DisplayPreferencesId = item.Id.ToString("N"),
            LocalTrailerCount = isPlayable ? 0 : null,
            SpecialFeatureCount = isPlayable ? 0 : null,
            UserData = UserDataFor(item.Id, item.IsFavorite, null),
            MediaSources = isPlayable ? [CatalogMediaSource(item.Id, item.Title, VirtualItemPath(item.Id), container, null, runtimeTicks, streams ?? [])] : null,
            MediaStreams = streams
        };
    }

    private static JellyfinBaseItemDto MapCard(
        IEntityCard item,
        string serverId,
        ItemContext? context = null,
        IEntityCard? playableChild = null) {
        // A movie's playable file, technical metadata, subtitles, and chapters live on its single
        // video child, while artwork/description/dates/people live on the movie folder. When a child
        // is supplied, source the media-bearing capabilities from it and everything else from the folder.
        var media = playableChild ?? item;
        var technical = media.Capabilities.OfType<TechnicalCapability>().FirstOrDefault();
        var playback = item.Capabilities.OfType<PlaybackCapability>().FirstOrDefault();
        var flags = item.Capabilities.OfType<FlagsCapability>().FirstOrDefault();
        var description = item.Capabilities.OfType<DescriptionCapability>().FirstOrDefault();
        var position = item.Capabilities.OfType<PositionCapability>().FirstOrDefault();
        var rating = item.Capabilities.OfType<RatingCapability>().FirstOrDefault();
        var dates = item.Capabilities.OfType<DatesCapability>().FirstOrDefault();
        var lifetime = item.Capabilities.OfType<LifetimeCapability>().FirstOrDefault();
        var classification = item.Capabilities.OfType<ClassificationCapability>().FirstOrDefault();
        var links = item.Capabilities.OfType<LinksCapability>().FirstOrDefault();
        var subtitles = media.Capabilities.OfType<SubtitlesCapability>().FirstOrDefault();
        var markers = media.Capabilities.OfType<MarkersCapability>().FirstOrDefault();
        var image = ImageMetadata(item.Id, item.Capabilities);
        var source = SourceFile(media);
        var isPlayable = IsPlayable(item.Kind);
        long? runtimeTicks = isPlayable ? technical?.Duration?.Ticks ?? 0 : null;
        var container = isPlayable ? technical?.Container ?? ContainerFromPath(source?.Path) : null;
        var streams = isPlayable ? CatalogStreams(technical, container, subtitles) : null;
        var childCount = item.ChildrenByKind.Sum(group => group.Entities.Count);
        var tags = TagItems(item);
        var studios = StudioItems(item);
        var premiereDate = PremiereDateFrom(dates);
        var startDate = ToDateTimeOffset(lifetime?.Start);
        var endDate = ToDateTimeOffset(lifetime?.End);
        var communityRating = rating?.Value is { } ratingValue ? Math.Clamp(ratingValue, 0, 5) * 2f : (float?)null;

        return new JellyfinBaseItemDto {
            Id = item.Id,
            Name = item.Title,
            ServerId = serverId,
            Etag = EtagFor(item.Id, image.PrimaryPath ?? item.Title),
            OriginalTitle = item.Title,
            SortName = item.Title,
            StartDate = startDate,
            EndDate = endDate,
            PremiereDate = premiereDate,
            ProductionYear = ProductionYearFrom(premiereDate, dates, lifetime),
            Overview = description?.Value,
            OfficialRating = EmptyAsNull(classification?.Value),
            CustomRating = EmptyAsNull(classification?.Value),
            CommunityRating = communityRating,
            Genres = tags.Count == 0 ? null : tags.Select(tag => tag.Name).ToArray(),
            GenreItems = tags.Count == 0 ? null : tags,
            Tags = tags.Count == 0 ? null : tags.Select(tag => tag.Name).ToArray(),
            People = People(item),
            Studios = studios.Count == 0 ? null : studios,
            ProviderIds = ProviderIds(links),
            ExternalUrls = ExternalUrls(links),
            RemoteTrailers = RemoteTrailers(links),
            Type = JellyfinType(item.Kind, item.ParentEntityId),
            MediaType = isPlayable ? "Video" : null,
            Path = isPlayable ? source?.Path ?? VirtualItemPath(item.Id) : null,
            LocationType = isPlayable ? "FileSystem" : null,
            PlayAccess = isPlayable ? "Full" : null,
            Container = container,
            MediaSourceCount = isPlayable ? 1 : null,
            SupportsResume = isPlayable ? true : null,
            SupportsSync = isPlayable ? true : null,
            CanDownload = isPlayable ? true : null,
            HasSubtitles = subtitles?.Items.Count > 0 ? true : null,
            Width = technical?.Width,
            Height = technical?.Height,
            AspectRatio = AspectRatio(technical?.Width, technical?.Height),
            IsHD = IsHd(technical),
            CollectionType = CollectionType(item.Kind),
            ParentId = context?.ParentId ?? item.ParentEntityId,
            IsFolder = IsFolder(item.Kind),
            ChildCount = childCount == 0 ? null : childCount,
            RecursiveItemCount = childCount == 0 ? null : childCount,
            RunTimeTicks = runtimeTicks,
            IndexNumber = PositionValue(position, "episode") ?? PositionValue(position, "sort") ?? item.SortOrder,
            ParentIndexNumber = PositionValue(position, "season") ?? context?.ParentIndexNumber,
            SeriesId = context?.SeriesId,
            SeriesName = context?.SeriesName,
            SeasonId = context?.SeasonId,
            SeasonName = context?.SeasonName,
            SeriesPrimaryImageTag = context?.SeriesPrimaryImageTag,
            ParentLogoItemId = context?.ParentLogoItemId,
            ParentLogoImageTag = context?.ParentLogoImageTag,
            ParentBackdropItemId = context?.ParentBackdropItemId,
            ParentBackdropImageTags = context?.ParentBackdropImageTags,
            ParentThumbItemId = context?.ParentThumbItemId,
            ParentThumbImageTag = context?.ParentThumbImageTag,
            VideoType = isPlayable ? "VideoFile" : null,
            CanDelete = isPlayable ? true : null,
            EnableMediaSourceDisplay = isPlayable ? true : null,
            DisplayPreferencesId = item.Id.ToString("N"),
            LocalTrailerCount = isPlayable ? 0 : null,
            SpecialFeatureCount = isPlayable ? 0 : null,
            ImageTags = image.Tags,
            BackdropImageTags = image.BackdropImageTags,
            PrimaryImageAspectRatio = image.PrimaryImageAspectRatio,
            UserData = UserDataFor(item.Id, flags?.IsFavorite == true, playback, runtimeTicks),
            MediaSources = isPlayable ? [CatalogMediaSource(item.Id, item.Title, source?.Path ?? VirtualItemPath(item.Id), container, source?.Path, runtimeTicks, streams ?? [], technical)] : null,
            MediaStreams = streams,
            Chapters = Chapters(markers)
        };
    }

    private static JellyfinBaseItemDto VirtualFolder(Guid id, string name, string collectionType, string serverId) =>
        new() {
            Id = id,
            Name = name,
            ServerId = serverId,
            Type = "CollectionFolder",
            CollectionType = collectionType,
            IsFolder = true,
            Etag = EtagFor(id, collectionType),
            UserData = UserDataFor(id, isFavorite: false, playback: null)
        };

    private async Task<ItemContext?> ParentContextForAsync(
        IEntityCard parent,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        if (parent.Kind.Equals("video-series", StringComparison.OrdinalIgnoreCase)) {
            // Episodes directly under a series (no season): parent-image fields all come from the series.
            var seriesImages = ImageMetadata(parent.Id, parent.Capabilities);
            return new ItemContext(
                parent.Id,
                parent.Title,
                null,
                null,
                null,
                SeriesPrimaryImageTag: ImageTag(seriesImages, "Primary"),
                ParentLogoItemId: ImageTag(seriesImages, "Logo") is null ? null : parent.Id,
                ParentLogoImageTag: ImageTag(seriesImages, "Logo"),
                ParentBackdropItemId: seriesImages.BackdropImageTags.Count == 0 ? null : parent.Id,
                ParentBackdropImageTags: seriesImages.BackdropImageTags.Count == 0 ? null : seriesImages.BackdropImageTags,
                ParentThumbItemId: ImageTag(seriesImages, "Thumb") is null ? null : parent.Id,
                ParentThumbImageTag: ImageTag(seriesImages, "Thumb"));
        }

        if (!parent.Kind.Equals("video-season", StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        IEntityCard? series = null;
        if (parent.ParentEntityId is { } seriesId) {
            series = await _entities.GetAsync(seriesId, hideNsfw, cancellationToken);
        }

        // Series supplies the backdrop/logo/primary artwork; the season supplies the thumb
        // (falling back to the series thumb) — matching how Jellyfin populates episode parent images.
        var seriesImageMeta = series is null
            ? null
            : ImageMetadata(series.Id, series.Capabilities);
        var seasonImageMeta = ImageMetadata(parent.Id, parent.Capabilities);
        var thumbTag = ImageTag(seasonImageMeta, "Thumb") ?? (seriesImageMeta is null ? null : ImageTag(seriesImageMeta, "Thumb"));
        var thumbItemId = ImageTag(seasonImageMeta, "Thumb") is not null
            ? parent.Id
            : thumbTag is null ? (Guid?)null : series?.Id;
        var backdropTags = seriesImageMeta?.BackdropImageTags ?? [];

        var parentIndexNumber = PositionValue(
            parent.Capabilities.OfType<PositionCapability>().FirstOrDefault(),
            "season") ?? parent.SortOrder;
        return new ItemContext(
            parent.ParentEntityId,
            series?.Title,
            parent.Id,
            parent.Title,
            parentIndexNumber,
            SeriesPrimaryImageTag: seriesImageMeta is null ? null : ImageTag(seriesImageMeta, "Primary"),
            ParentLogoItemId: seriesImageMeta is not null && ImageTag(seriesImageMeta, "Logo") is not null ? series?.Id : null,
            ParentLogoImageTag: seriesImageMeta is null ? null : ImageTag(seriesImageMeta, "Logo"),
            ParentBackdropItemId: backdropTags.Count == 0 ? null : series?.Id,
            ParentBackdropImageTags: backdropTags.Count == 0 ? null : backdropTags,
            ParentThumbItemId: thumbItemId,
            ParentThumbImageTag: thumbTag);
    }

    private static string? ImageTag(JellyfinImageMetadata images, string type) =>
        images.Tags.TryGetValue(type, out var tag) ? tag : null;

    private static JellyfinCatalogMediaSourceDto CatalogMediaSource(
        Guid id,
        string name,
        string path,
        string? container,
        string? filePath,
        long? runtimeTicks,
        IReadOnlyList<JellyfinCatalogMediaStreamDto> streams,
        TechnicalCapability? technical = null) {
        long? size = null;
        if (!string.IsNullOrWhiteSpace(filePath)) {
            var file = new FileInfo(filePath);
            size = file.Exists ? file.Length : null;
        }

        var audioIndex = streams
            .Where(stream => stream.Type.Equals("Audio", StringComparison.OrdinalIgnoreCase))
            .Select(stream => (int?)stream.Index)
            .FirstOrDefault();

        return new JellyfinCatalogMediaSourceDto {
            Id = id.ToString("N"),
            Path = path,
            Container = container,
            Size = size,
            Name = Path.GetFileName(path),
            ETag = EtagFor(id, filePath ?? path),
            RunTimeTicks = runtimeTicks,
            Bitrate = technical?.BitRate,
            DefaultAudioStreamIndex = audioIndex,
            MediaStreams = streams
        };
    }

    private static IReadOnlyList<JellyfinCatalogMediaStreamDto> CatalogStreams(
        TechnicalCapability? technical,
        string? container,
        SubtitlesCapability? subtitles) {
        var streams = new List<JellyfinCatalogMediaStreamDto> {
            new JellyfinCatalogMediaStreamDto {
                Index = 0,
                Type = "Video",
                Codec = technical?.Codec,
                DisplayTitle = StreamDisplayTitle(technical, container),
                Width = technical?.Width,
                Height = technical?.Height,
                AverageFrameRate = technical?.FrameRate,
                RealFrameRate = technical?.FrameRate,
                AspectRatio = AspectRatio(technical?.Width, technical?.Height),
                BitRate = technical?.BitRate
            }
        };

        // Audio stream — emitted when the probe captured any audio detail, so clients can resolve a
        // default audio track. Codec is unknown at this layer (the technical capability only carries
        // the video codec); HDR/Dolby-Vision and per-track audio codec metadata are a deferred pass.
        var nextIndex = 1;
        if (technical?.Channels is not null || technical?.SampleRate is not null) {
            streams.Add(new JellyfinCatalogMediaStreamDto {
                Index = nextIndex++,
                Type = "Audio",
                Channels = technical?.Channels,
                ChannelLayout = ChannelLayout(technical?.Channels),
                SampleRate = technical?.SampleRate,
                IsDefault = true
            });
        }

        if (subtitles?.Items.Count > 0) {
            streams.AddRange(subtitles.Items.Select(subtitle => new JellyfinCatalogMediaStreamDto {
                Index = nextIndex++,
                Type = "Subtitle",
                Codec = subtitle.Format,
                Language = EmptyAsNull(subtitle.Language),
                DisplayTitle = EmptyAsNull(subtitle.Label) ?? subtitle.Language,
                IsDefault = subtitle.IsDefault,
                IsForced = false,
                IsExternal = true
            }));
        }

        return streams;
    }

    private static string? ChannelLayout(int? channels) =>
        channels switch {
            1 => "mono",
            2 => "stereo",
            6 => "5.1",
            8 => "7.1",
            _ => null
        };

    private static IReadOnlyList<JellyfinCatalogMediaStreamDto> CatalogStreams(
        EntityThumbnail item,
        string? container) =>
        [
            new JellyfinCatalogMediaStreamDto {
                Index = 0,
                Type = "Video",
                Codec = item.Meta.FirstOrDefault(meta => meta.Icon.Equals("video", StringComparison.OrdinalIgnoreCase) &&
                    !meta.Label.Contains("p", StringComparison.OrdinalIgnoreCase) &&
                    !meta.Label.Equals(container, StringComparison.OrdinalIgnoreCase))?.Label,
                DisplayTitle = item.Meta.FirstOrDefault(meta => meta.Icon.Equals("video", StringComparison.OrdinalIgnoreCase))?.Label
            }
        ];

    private static string? StreamDisplayTitle(TechnicalCapability? technical, string? container) {
        var parts = new[]
        {
            technical?.Height is { } height ? $"{height}p" : null,
            technical?.Codec,
            container
        };
        var title = string.Join(" - ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
        return title.Length == 0 ? null : title;
    }

    private static EntityFile? SourceFile(IEntityCard item) =>
        item.Capabilities
            .OfType<FilesCapability>()
            .SelectMany(files => files.Items)
            .FirstOrDefault(file => file.Role.Equals("source", StringComparison.OrdinalIgnoreCase));

    private static string VirtualItemPath(Guid id) => $"/{id:N}";

    private static long? RuntimeTicksFrom(EntityThumbnail item) {
        var label = item.Meta.FirstOrDefault(meta => meta.Icon.Equals("duration", StringComparison.OrdinalIgnoreCase))?.Label;
        if (string.IsNullOrWhiteSpace(label)) {
            return null;
        }

        var parts = label.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length is < 2 or > 3 || parts.Any(part => !int.TryParse(part, out _))) {
            return null;
        }

        var values = parts.Select(int.Parse).ToArray();
        var duration = values.Length == 2
            ? new TimeSpan(0, values[0], values[1])
            : new TimeSpan(values[0], values[1], values[2]);
        return duration.Ticks;
    }

    private static string? ContainerFrom(EntityThumbnail item) =>
        item.Meta
            .Where(meta => meta.Icon.Equals("video", StringComparison.OrdinalIgnoreCase))
            .Select(meta => meta.Label)
            .LastOrDefault(label => !label.Contains("p", StringComparison.OrdinalIgnoreCase));

    private static string? ContainerFromPath(string? path) {
        if (string.IsNullOrWhiteSpace(path)) {
            return null;
        }

        var extension = Path.GetExtension(path);
        return string.IsNullOrWhiteSpace(extension) ? null : extension.TrimStart('.').ToLowerInvariant();
    }

    private sealed record ItemContext(
        Guid? SeriesId,
        string? SeriesName,
        Guid? SeasonId,
        string? SeasonName,
        int? ParentIndexNumber,
        Guid? ParentId = null,
        string? SeriesPrimaryImageTag = null,
        Guid? ParentLogoItemId = null,
        string? ParentLogoImageTag = null,
        Guid? ParentBackdropItemId = null,
        IReadOnlyList<string>? ParentBackdropImageTags = null,
        Guid? ParentThumbItemId = null,
        string? ParentThumbImageTag = null) {
        public static ItemContext? From(JellyfinBaseItemDto item) =>
            item.SeriesId is null &&
            item.SeriesName is null &&
            item.SeasonId is null &&
            item.SeasonName is null &&
            item.ParentIndexNumber is null &&
            item.ParentId is null
                ? null
                : new ItemContext(
                    item.SeriesId,
                    item.SeriesName,
                    item.SeasonId,
                    item.SeasonName,
                    item.ParentIndexNumber,
                    item.ParentId,
                    item.SeriesPrimaryImageTag,
                    item.ParentLogoItemId,
                    item.ParentLogoImageTag,
                    item.ParentBackdropItemId,
                    item.ParentBackdropImageTags,
                    item.ParentThumbItemId,
                    item.ParentThumbImageTag);
    }

    private static IReadOnlyList<JellyfinBaseItemDto> ApplyItemTypeFilter(
        IReadOnlyList<JellyfinBaseItemDto> items,
        IReadOnlyList<string> includeItemTypes) {
        if (includeItemTypes.Count == 0) {
            return items;
        }

        var allowed = includeItemTypes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return items.Where(item => allowed.Contains(item.Type)).ToArray();
    }

    private static IReadOnlyList<JellyfinBaseItemDto> ApplyPlayedFilter(IReadOnlyList<JellyfinBaseItemDto> items, bool? isPlayed) =>
        isPlayed is null ? items : items.Where(item => item.UserData?.Played == isPlayed).ToArray();

    private static IReadOnlyList<EntityImageAsset> ImageAssets(IReadOnlyList<EntityCapability> capabilities) =>
        capabilities.OfType<ImagesCapability>().FirstOrDefault()?.Items ?? [];

    private static JellyfinImageMetadata ImageMetadata(Guid id, IReadOnlyList<EntityCapability> capabilities) {
        var images = ImageAssets(capabilities);
        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var backdrops = new List<string>();
        foreach (var asset in images) {
            var type = JellyfinImageType(asset.Kind);
            var tag = EtagFor(id, asset.Path);
            if (type.Equals("Backdrop", StringComparison.OrdinalIgnoreCase)) {
                backdrops.Add(tag);
                continue;
            }

            tags.TryAdd(type, tag);
        }

        var imageCapability = capabilities.OfType<ImagesCapability>().FirstOrDefault();
        var primary = PrimaryImageAsset(images, imageCapability);
        if (primary is not null) {
            tags["Primary"] = EtagFor(id, primary.Path);
        }

        return new JellyfinImageMetadata(
            tags,
            backdrops,
            primary?.Path,
            primary is null ? null : 0.6667);
    }

    private static EntityImageAsset? PrimaryImageAsset(
        IReadOnlyList<EntityImageAsset> images,
        ImagesCapability? imageCapability) {
        var primary = images.FirstOrDefault(image =>
            image.Kind.Equals("poster", StringComparison.OrdinalIgnoreCase) ||
            image.Kind.Equals("cover", StringComparison.OrdinalIgnoreCase) ||
            image.Kind.Equals("thumbnail", StringComparison.OrdinalIgnoreCase) ||
            image.Kind.Equals("primary", StringComparison.OrdinalIgnoreCase)) ?? images.FirstOrDefault(image =>
            !JellyfinImageType(image.Kind).Equals("Backdrop", StringComparison.OrdinalIgnoreCase));
        if (primary is not null) {
            return primary;
        }

        if (!string.IsNullOrWhiteSpace(imageCapability?.CoverUrl)) {
            return new EntityImageAsset("cover", imageCapability.CoverUrl, null);
        }

        return string.IsNullOrWhiteSpace(imageCapability?.ThumbnailUrl)
            ? null
            : new EntityImageAsset("thumbnail", imageCapability.ThumbnailUrl, null);
    }

    private static (string? Primary, string? Backdrop) ImageTags(Guid id, string? primary, string? backdrop) =>
        (primary is null ? null : EtagFor(id, primary), backdrop is null ? null : EtagFor(id, backdrop));

    private static string JellyfinImageType(string prismediaKind) =>
        prismediaKind.Trim().ToLowerInvariant() switch {
            "art" => "Art",
            "backdrop" or "background" or "fanart" => "Backdrop",
            "banner" => "Banner",
            "box" => "Box",
            "disc" or "disc-art" => "Disc",
            "logo" or "clearlogo" => "Logo",
            "screenshot" => "Screenshot",
            "thumb" => "Thumb",
            _ => "Primary"
        };

    private static IReadOnlyList<JellyfinNameGuidPairDto> TagItems(IEntityCard item) =>
        RelationshipPairs(item, group =>
            group.Kind.Equals("tag", StringComparison.OrdinalIgnoreCase) ||
            group.Kind.Equals("genre", StringComparison.OrdinalIgnoreCase) ||
            group.Code?.Equals("tags", StringComparison.OrdinalIgnoreCase) == true ||
            group.Code?.Equals("genres", StringComparison.OrdinalIgnoreCase) == true);

    private static IReadOnlyList<JellyfinNameGuidPairDto> StudioItems(IEntityCard item) =>
        RelationshipPairs(item, group =>
            group.Kind.Equals("studio", StringComparison.OrdinalIgnoreCase) ||
            group.Code?.Equals("studio", StringComparison.OrdinalIgnoreCase) == true ||
            group.Code?.Equals("studios", StringComparison.OrdinalIgnoreCase) == true);

    private static IReadOnlyList<JellyfinNameGuidPairDto> RelationshipPairs(
        IEntityCard item,
        Func<EntityGroup, bool> predicate) {
        var pairs = new List<JellyfinNameGuidPairDto>();
        var seen = new HashSet<Guid>();
        foreach (var group in item.Relationships.Where(predicate)) {
            foreach (var entity in group.Entities) {
                if (seen.Add(entity.Id)) {
                    pairs.Add(new JellyfinNameGuidPairDto(entity.Title, entity.Id));
                }
            }
        }

        return pairs;
    }

    private static IReadOnlyList<JellyfinBaseItemPersonDto>? People(IEntityCard item) {
        var groups = item.Relationships
            .Where(group => group.Kind.Equals("person", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (groups.Length == 0) {
            return null;
        }

        var creditMetadata = CreditMetadata(item)
            .GroupBy(credit => credit.PersonId)
            .ToDictionary(group => group.Key, group => group.First());
        var people = new List<JellyfinBaseItemPersonDto>();
        foreach (var group in groups) {
            foreach (var person in group.Entities) {
                creditMetadata.TryGetValue(person.Id, out var credit);
                var type = PersonType(credit?.Role, group);
                people.Add(new JellyfinBaseItemPersonDto(
                    person.Title,
                    person.Id,
                    PersonRole(credit, type, group),
                    type,
                    person.CoverUrl is null ? null : EtagFor(person.Id, person.CoverUrl)));
            }
        }

        return people.Count == 0 ? null : people;
    }

    private static IReadOnlyList<EntityCreditMetadata> CreditMetadata(IEntityCard item) =>
        item switch {
            VideoDetail detail => detail.CreditMetadata,
            VideoSeriesDetail detail => detail.CreditMetadata,
            VideoSeasonDetail detail => detail.CreditMetadata,
            GalleryDetail detail => detail.CreditMetadata,
            _ => []
        };

    private static string PersonType(string? role, EntityGroup group) {
        var code = EmptyAsNull(role) ?? EmptyAsNull(group.Code) ?? group.Label;
        return code.Trim().ToLowerInvariant() switch {
            "actor" or "cast" or "performer" or "performers" => "Actor",
            "composer" => "Composer",
            "creator" => "Creator",
            "director" => "Director",
            "producer" => "Producer",
            "writer" => "Writer",
            "artist" => "Artist",
            "narrator" => "Narrator",
            _ => "Person"
        };
    }

    private static string? PersonRole(EntityCreditMetadata? credit, string type, EntityGroup group) {
        if (!string.IsNullOrWhiteSpace(credit?.Character)) {
            return credit.Character;
        }

        if (!string.IsNullOrWhiteSpace(credit?.Role) &&
            !credit.Role.Equals("actor", StringComparison.OrdinalIgnoreCase)) {
            return TitleLabel(credit.Role);
        }

        if (!string.IsNullOrWhiteSpace(group.Code) &&
            !group.Code.Equals("cast", StringComparison.OrdinalIgnoreCase) &&
            !group.Code.Equals("credits", StringComparison.OrdinalIgnoreCase)) {
            return TitleLabel(group.Code);
        }

        return type.Equals("Actor", StringComparison.OrdinalIgnoreCase) ? null : type;
    }

    private static DateTimeOffset? PremiereDateFrom(DatesCapability? dates) =>
        ToDateTimeOffset(DateByPriority(dates?.Items, PremiereDatePriority));

    private static EntityDate? DateByPriority(IReadOnlyList<EntityDate>? dates, IReadOnlyList<string> priority) {
        if (dates is null || dates.Count == 0) {
            return null;
        }

        foreach (var code in priority) {
            var match = dates.FirstOrDefault(date => date.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
            if (match is not null) {
                return match;
            }
        }

        return dates.FirstOrDefault(date => date.SortableValue is not null) ?? dates[0];
    }

    private static DateTimeOffset? ToDateTimeOffset(EntityDate? date) {
        if (date is null) {
            return null;
        }

        if (date.SortableValue is { } sortableValue) {
            return new DateTimeOffset(sortableValue.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        }

        if (DateOnly.TryParse(date.Value, out var dateOnly)) {
            return new DateTimeOffset(dateOnly.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        }

        if (DateTimeOffset.TryParse(date.Value, out var dateTimeOffset)) {
            return dateTimeOffset.ToUniversalTime();
        }

        return int.TryParse(date.Value, out var year) && year is >= 1 and <= 9999
            ? new DateTimeOffset(year, 1, 1, 0, 0, 0, TimeSpan.Zero)
            : null;
    }

    private static int? ProductionYearFrom(
        DateTimeOffset? premiereDate,
        DatesCapability? dates,
        LifetimeCapability? lifetime) {
        if (premiereDate is { } date) {
            return date.Year;
        }

        var candidate = DateByPriority(dates?.Items, PremiereDatePriority) ?? lifetime?.Start ?? lifetime?.End;
        if (candidate?.SortableValue is { } sortableValue) {
            return sortableValue.Year;
        }

        var value = candidate?.Value?.Trim();
        if (string.IsNullOrWhiteSpace(value)) {
            return null;
        }

        if (value.Length >= 4 && int.TryParse(value[..4], out var year) && year is >= 1 and <= 9999) {
            return year;
        }

        return DateTimeOffset.TryParse(value, out var parsed) ? parsed.Year : null;
    }

    private static IReadOnlyDictionary<string, string>? ProviderIds(LinksCapability? links) {
        if (links?.ExternalIds.Count is not > 0) {
            return null;
        }

        var providerIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in links.ExternalIds) {
            if (!string.IsNullOrWhiteSpace(id.Provider) &&
                !string.IsNullOrWhiteSpace(id.Value) &&
                !providerIds.ContainsKey(id.Provider)) {
                providerIds[id.Provider] = id.Value;
            }
        }

        return providerIds.Count == 0 ? null : providerIds;
    }

    private static IReadOnlyList<JellyfinExternalUrlDto>? ExternalUrls(LinksCapability? links) {
        if (links is null) {
            return null;
        }

        var urls = new List<JellyfinExternalUrlDto>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var url in links.Urls) {
            AddExternalUrl(urls, seen, EmptyAsNull(url.Label) ?? LabelForUrl(url.Value), url.Value);
        }

        foreach (var id in links.ExternalIds.Where(id => !string.IsNullOrWhiteSpace(id.Url))) {
            AddExternalUrl(urls, seen, ProviderLabel(id.Provider), id.Url!);
        }

        return urls.Count == 0 ? null : urls;
    }

    private static IReadOnlyList<JellyfinMediaUrlDto>? RemoteTrailers(LinksCapability? links) {
        if (links is null) {
            return null;
        }

        var trailers = links.Urls
            .Where(url => IsTrailerUrl(url))
            .Select(url => new JellyfinMediaUrlDto(EmptyAsNull(url.Label) ?? "Trailer", url.Value))
            .ToArray();
        return trailers.Length == 0 ? null : trailers;
    }

    private static void AddExternalUrl(
        List<JellyfinExternalUrlDto> urls,
        HashSet<string> seen,
        string name,
        string? value) {
        if (string.IsNullOrWhiteSpace(value) || !seen.Add(value)) {
            return;
        }

        urls.Add(new JellyfinExternalUrlDto(name, value));
    }

    private static bool IsTrailerUrl(EntityUrl url) =>
        url.Label?.Contains("trailer", StringComparison.OrdinalIgnoreCase) == true ||
        url.Value.Contains("trailer", StringComparison.OrdinalIgnoreCase) ||
        url.Value.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) ||
        url.Value.Contains("youtu.be", StringComparison.OrdinalIgnoreCase);

    private static string LabelForUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host)
            ? uri.Host.Replace("www.", string.Empty, StringComparison.OrdinalIgnoreCase)
            : "Link";

    private static string ProviderLabel(string provider) =>
        provider.Trim().ToLowerInvariant() switch {
            "anidb" => "AniDB",
            "imdb" => "IMDb",
            "tmdb" => "TMDb",
            "tvdb" => "TVDb",
            _ => TitleLabel(provider)
        };

    private static IReadOnlyList<JellyfinChapterInfoDto>? Chapters(MarkersCapability? markers) {
        if (markers?.Items.Count is not > 0) {
            return null;
        }

        return markers.Items
            .Select(marker => new JellyfinChapterInfoDto {
                Name = marker.Title,
                StartPositionTicks = TicksFromSeconds(marker.Seconds)
            })
            .ToArray();
    }

    private static long TicksFromSeconds(double seconds) =>
        TimeSpan.FromSeconds(double.IsFinite(seconds) ? Math.Max(0, seconds) : 0).Ticks;

    private static string? AspectRatio(int? width, int? height) {
        if (width is not > 0 || height is not > 0) {
            return null;
        }

        var divisor = GreatestCommonDivisor(width.Value, height.Value);
        return $"{width.Value / divisor}:{height.Value / divisor}";
    }

    private static int GreatestCommonDivisor(int left, int right) {
        left = Math.Abs(left);
        right = Math.Abs(right);
        while (right != 0) {
            var next = left % right;
            left = right;
            right = next;
        }

        return left == 0 ? 1 : left;
    }

    private static bool? IsHd(TechnicalCapability? technical) =>
        technical is null ? null : technical.Height is >= 720 || technical.Width is >= 1280;

    private static string TitleLabel(string value) {
        var words = value.Replace('_', '-')
            .Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0) {
            return value;
        }

        return string.Join(" ", words.Select(word =>
            word.Length == 0
                ? word
                : string.Create(word.Length, word, static (chars, state) => {
                    chars[0] = char.ToUpperInvariant(state[0]);
                    if (state.Length > 1) {
                        state.AsSpan(1).CopyTo(chars[1..]);
                    }
                })));
    }

    private static string? EmptyAsNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record JellyfinImageMetadata(
        IReadOnlyDictionary<string, string> Tags,
        IReadOnlyList<string> BackdropImageTags,
        string? PrimaryPath,
        double? PrimaryImageAspectRatio);

    private static JellyfinUserItemDataDto UserDataFor(
        Guid id,
        bool isFavorite,
        PlaybackCapability? playback,
        long? runtimeTicks = null) {
        var resumeTicks = playback is null ? 0 : TimeSpan.FromSeconds(playback.ResumeSeconds).Ticks;
        var playCount = playback?.PlayCount ?? 0;
        var played = playback?.CompletedAt is not null || playCount > 0 && resumeTicks == 0;
        var playedPercentage = played
            ? 100d
            : resumeTicks > 0 && runtimeTicks is > 0
                ? Math.Clamp(resumeTicks / (double)runtimeTicks.Value * 100d, 0, 100)
                : (double?)null;
        return new JellyfinUserItemDataDto(
            resumeTicks,
            playCount,
            isFavorite,
            played,
            id.ToString("N"),
            id.ToString("N"),
            playedPercentage,
            playback?.LastPlayedAt);
    }

    private static bool IsPlayableVideo(string kind) => kind.Equals("video", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether a kind is a playable leaf in the Jellyfin projection. Movies and videos both map to
    /// playable items (a movie streams through its single video child, resolved in the source service).
    /// </summary>
    private static bool IsPlayable(string kind) =>
        kind.Equals("video", StringComparison.OrdinalIgnoreCase) ||
        kind.Equals("movie", StringComparison.OrdinalIgnoreCase);

    private static bool IsFolder(string kind) =>
        kind is "video-series" or "video-season" or "collection";

    private static string JellyfinType(string kind, Guid? parentId) =>
        kind.Trim().ToLowerInvariant() switch {
            "movie" => "Movie",
            "video" => parentId is null ? "Movie" : "Episode",
            "video-series" => "Series",
            "video-season" => "Season",
            "collection" => "BoxSet",
            _ => "Folder"
        };

    private static string? CollectionType(string kind) =>
        kind.Trim().ToLowerInvariant() switch {
            "video-series" => "tvshows",
            "collection" => "boxsets",
            _ => null
        };

    private static int? PositionValue(PositionCapability? position, string code) =>
        position?.Items.FirstOrDefault(item => item.Code.Equals(code, StringComparison.OrdinalIgnoreCase))?.Value;

    private static string? ToPrismediaSort(string? sortBy) =>
        sortBy?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()?.ToLowerInvariant() switch {
            "datecreated" or "premieredate" or "dateplayed" => "added",
            "sortname" or "name" => "title",
            _ => null
        };

    private static string? ToPrismediaSortDir(string? sortOrder) =>
        sortOrder?.Equals("Descending", StringComparison.OrdinalIgnoreCase) == true ||
        sortOrder?.Equals("desc", StringComparison.OrdinalIgnoreCase) == true
            ? "desc"
            : "asc";

    private static string EtagFor(Guid id, string value) {
        var bytes = SHA1.HashData(Encoding.UTF8.GetBytes($"{id:N}:{value}"));
        return Convert.ToHexString(bytes).ToLowerInvariant()[..12];
    }

    private static string MimeTypeForPath(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".avif" => "image/avif",
            _ => "application/octet-stream"
        };
}
