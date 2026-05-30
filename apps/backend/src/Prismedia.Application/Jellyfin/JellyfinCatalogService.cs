using System.Security.Cryptography;
using System.Text;
using Prismedia.Application.Collections;
using Prismedia.Application.Entities;
using Prismedia.Contracts.Collections;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Jellyfin;

namespace Prismedia.Application.Jellyfin;

/// <summary>Maps Prismedia's video-first library model to clean-room Jellyfin-compatible DTOs.</summary>
public sealed class JellyfinCatalogService {
    public static readonly Guid RootId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    public static readonly Guid VideosViewId = Guid.Parse("10000000-0000-0000-0000-000000000002");
    public static readonly Guid SeriesViewId = Guid.Parse("10000000-0000-0000-0000-000000000003");
    public static readonly Guid CollectionsViewId = Guid.Parse("10000000-0000-0000-0000-000000000004");

    private const int MaxBrowseItems = 5000;

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
            VirtualFolder(VideosViewId, "Videos", "movies", serverId),
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
            ChildCount = 3,
            RecursiveItemCount = 3,
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
        var page = items.Skip(start).Take(limit).ToArray();
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

        if (id == VideosViewId) {
            return VirtualFolder(VideosViewId, "Videos", "movies", serverId);
        }

        if (id == SeriesViewId) {
            return VirtualFolder(SeriesViewId, "Series", "tvshows", serverId);
        }

        if (id == CollectionsViewId) {
            return VirtualFolder(CollectionsViewId, "Collections", "boxsets", serverId);
        }

        var entity = await _entities.GetAsync(id, hideNsfw, cancellationToken);
        return entity is null ? null : MapCard(entity, serverId);
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
            sort: "added",
            sortDir: "desc",
            status: "in-progress");
        var items = response.Items.Select(item => MapThumbnail(item, serverId)).ToArray();
        var total = items.Length;
        var start = Math.Clamp(startIndex, 0, total);
        return new JellyfinQueryResult<JellyfinBaseItemDto>(items.Skip(start).Take(limit).ToArray(), total, start);
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

        return ImageAssets(entity.Capabilities)
            .Select((asset, index) => new JellyfinImageInfo(JellyfinImageType(asset.Kind), index, EtagFor(id, asset.Path)))
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
        if (parentId == VideosViewId) {
            var thumbnails = await FetchAllThumbnailsAsync("video", query, hideNsfw, cancellationToken);
            return thumbnails.Select(item => MapThumbnail(item, serverId)).ToArray();
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

        var childThumbnails = parent.ChildrenByKind
            .SelectMany(group => group.Entities)
            .Where(child => query.Recursive || child.ParentEntityId == parentId)
            .ToArray();
        return childThumbnails.Select(child => MapThumbnail(child, serverId, parentId)).ToArray();
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
        item.Entity.Kind is "video" or "video-series" or "video-season"
            ? MapThumbnail(item.Entity, serverId, collectionId)
            : null;

    private static JellyfinBaseItemDto MapThumbnail(EntityThumbnail item, string serverId, Guid? parentOverride = null) {
        var imageTags = ImageTags(item.Id, item.CoverUrl, null);
        return new JellyfinBaseItemDto {
            Id = item.Id,
            Name = item.Title,
            ServerId = serverId,
            Etag = EtagFor(item.Id, item.CoverUrl ?? item.Title),
            SortName = item.Title,
            Type = JellyfinType(item.Kind, item.ParentEntityId),
            MediaType = IsPlayableVideo(item.Kind) ? "Video" : null,
            CollectionType = CollectionType(item.Kind),
            ParentId = parentOverride ?? item.ParentEntityId,
            IsFolder = IsFolder(item.Kind),
            ImageTags = imageTags.Primary is null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["Primary"] = imageTags.Primary },
            BackdropImageTags = imageTags.Backdrop is null ? [] : [imageTags.Backdrop],
            PrimaryImageAspectRatio = 0.6667,
            IndexNumber = item.SortOrder,
            UserData = UserDataFor(item.Id, item.IsFavorite, null),
            MediaSources = IsPlayableVideo(item.Kind) ? [] : null,
            MediaStreams = IsPlayableVideo(item.Kind) ? [] : null
        };
    }

    private static JellyfinBaseItemDto MapCard(IEntityCard item, string serverId) {
        var technical = item.Capabilities.OfType<TechnicalCapability>().FirstOrDefault();
        var playback = item.Capabilities.OfType<PlaybackCapability>().FirstOrDefault();
        var flags = item.Capabilities.OfType<FlagsCapability>().FirstOrDefault();
        var description = item.Capabilities.OfType<DescriptionCapability>().FirstOrDefault();
        var position = item.Capabilities.OfType<PositionCapability>().FirstOrDefault();
        var image = PrimaryAndBackdrop(item.Capabilities);
        var childCount = item.ChildrenByKind.Sum(group => group.Entities.Count);

        return new JellyfinBaseItemDto {
            Id = item.Id,
            Name = item.Title,
            ServerId = serverId,
            Etag = EtagFor(item.Id, image.PrimaryPath ?? item.Title),
            SortName = item.Title,
            Overview = description?.Value,
            Type = JellyfinType(item.Kind, item.ParentEntityId),
            MediaType = IsPlayableVideo(item.Kind) ? "Video" : null,
            CollectionType = CollectionType(item.Kind),
            ParentId = item.ParentEntityId,
            IsFolder = IsFolder(item.Kind),
            ChildCount = childCount == 0 ? null : childCount,
            RecursiveItemCount = childCount == 0 ? null : childCount,
            RunTimeTicks = technical?.Duration is { } duration ? duration.Ticks : null,
            IndexNumber = PositionValue(position, "episode") ?? PositionValue(position, "sort") ?? item.SortOrder,
            ParentIndexNumber = PositionValue(position, "season"),
            ImageTags = image.PrimaryTag is null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["Primary"] = image.PrimaryTag },
            BackdropImageTags = image.BackdropTag is null ? [] : [image.BackdropTag],
            PrimaryImageAspectRatio = image.PrimaryTag is null ? null : 0.6667,
            UserData = UserDataFor(item.Id, flags?.IsFavorite == true, playback),
            MediaSources = IsPlayableVideo(item.Kind) ? [] : null,
            MediaStreams = IsPlayableVideo(item.Kind) ? [] : null
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

    private static (string? PrimaryTag, string? PrimaryPath, string? BackdropTag, string? BackdropPath) PrimaryAndBackdrop(
        IReadOnlyList<EntityCapability> capabilities) {
        var images = ImageAssets(capabilities);
        var primary = images.FirstOrDefault(image =>
            image.Kind.Equals("thumbnail", StringComparison.OrdinalIgnoreCase) ||
            image.Kind.Equals("poster", StringComparison.OrdinalIgnoreCase) ||
            image.Kind.Equals("cover", StringComparison.OrdinalIgnoreCase)) ?? images.FirstOrDefault();
        var backdrop = images.FirstOrDefault(image => image.Kind.Equals("backdrop", StringComparison.OrdinalIgnoreCase));
        return (
            primary is null ? null : EtagFor(Guid.Empty, primary.Path),
            primary?.Path,
            backdrop is null ? null : EtagFor(Guid.Empty, backdrop.Path),
            backdrop?.Path);
    }

    private static (string? Primary, string? Backdrop) ImageTags(Guid id, string? primary, string? backdrop) =>
        (primary is null ? null : EtagFor(id, primary), backdrop is null ? null : EtagFor(id, backdrop));

    private static string JellyfinImageType(string prismediaKind) =>
        prismediaKind.Trim().ToLowerInvariant() switch {
            "backdrop" => "Backdrop",
            "logo" => "Logo",
            _ => "Primary"
        };

    private static JellyfinUserItemDataDto UserDataFor(Guid id, bool isFavorite, PlaybackCapability? playback) {
        var resumeTicks = playback is null ? 0 : TimeSpan.FromSeconds(playback.ResumeSeconds).Ticks;
        var playCount = playback?.PlayCount ?? 0;
        return new JellyfinUserItemDataDto(
            resumeTicks,
            playCount,
            isFavorite,
            playback?.CompletedAt is not null || playCount > 0 && resumeTicks == 0,
            id.ToString("N"));
    }

    private static bool IsPlayableVideo(string kind) => kind.Equals("video", StringComparison.OrdinalIgnoreCase);

    private static bool IsFolder(string kind) =>
        kind is "video-series" or "video-season" or "collection";

    private static string JellyfinType(string kind, Guid? parentId) =>
        kind.Trim().ToLowerInvariant() switch {
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
