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

/// <summary>
/// Card and thumbnail to JellyfinBaseItemDto mapping for <see cref="JellyfinCatalogService"/>.
/// </summary>
public sealed partial class JellyfinCatalogService {
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
            MediaType = isPlayable ? JellyfinProtocol.MediaTypes.Video : null,
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
            RunTimeTicks = runtimeTicks,
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
            MediaType = isPlayable ? JellyfinProtocol.MediaTypes.Video : null,
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

    private static JellyfinBaseItemDto VirtualFolder(
        Guid id,
        string name,
        string collectionType,
        string serverId,
        string? coverPath = null) {
        // Surface a representative item poster as the library tile artwork so clients like Infuse
        // render a real thumbnail instead of a generic folder icon. The tag is keyed on the resolved
        // cover path so the image endpoint can serve the same asset for the synthetic view id.
        var primaryTag = coverPath is null ? null : EtagFor(id, coverPath);
        return new() {
            Id = id,
            Name = name,
            ServerId = serverId,
            Type = JellyfinProtocol.ItemTypes.CollectionFolder,
            CollectionType = collectionType,
            IsFolder = true,
            Etag = EtagFor(id, collectionType),
            ImageTags = primaryTag is null
                ? new Dictionary<string, string>()
                : new Dictionary<string, string> { ["Primary"] = primaryTag },
            PrimaryImageAspectRatio = primaryTag is null ? null : 0.6667,
            UserData = UserDataFor(id, isFavorite: false, playback: null)
        };
    }

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

}
