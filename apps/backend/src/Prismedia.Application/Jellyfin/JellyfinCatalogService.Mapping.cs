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

/// <summary>
/// Card and thumbnail to JellyfinBaseItemDto mapping for <see cref="JellyfinCatalogService"/>.
/// </summary>
public sealed partial class JellyfinCatalogService {
    private static JellyfinBaseItemDto MapThumbnail(
        EntityThumbnail item,
        string serverId,
        Guid? parentOverride = null,
        ItemContext? context = null) {
        var isPlayable = IsPlayable(item.Kind);
        var isAudio = IsAudio(item.Kind);
        var isAlbum = item.Kind == EntityKind.AudioLibrary;
        var isMusic = IsMusic(item.Kind);
        var imageTags = ImageTags(item.Id, item.CoverUrl, null);
        var primaryImageTag = MusicAwarePrimaryImageTag(item.Id, imageTags.Primary, isMusic, isAudio ? context?.AlbumPrimaryImageTag : null);
        // Tracks carry their album reference; tracks and albums both carry the album-artist reference.
        var artistContext = isAudio || isAlbum ? context : null;
        long? runtimeTicks = isPlayable ? RuntimeTicksFrom(item) ?? 0 : null;
        // Audio tracks describe an audio stream/container; video items keep the video shape.
        var container = isAudio ? AudioCodecFrom(item) : isPlayable ? ContainerFrom(item) : null;
        var streams = isAudio ? CatalogAudioStreams(item) : isPlayable ? CatalogStreams(item, container) : null;
        return new JellyfinBaseItemDto {
            Id = item.Id,
            Name = item.Title,
            ServerId = serverId,
            Etag = EtagFor(item.Id, item.CoverUrl ?? item.Title),
            SortName = item.Title,
            DateCreated = item.CreatedAt,
            Type = JellyfinType(item.Kind, item.ParentEntityId),
            // Strict clients (Manet) decode MediaType into an enum on every item and drop those missing
            // it, so non-playable items carry "Unknown" rather than null, matching real Jellyfin.
            MediaType = isAudio ? JellyfinProtocol.MediaTypes.Audio : isPlayable ? JellyfinProtocol.MediaTypes.Video : JellyfinProtocol.MediaTypes.Unknown,
            Path = isPlayable ? VirtualItemPath(item.Id) : null,
            LocationType = isPlayable || isMusic ? "FileSystem" : null,
            PlayAccess = isPlayable ? "Full" : null,
            Container = container,
            MediaSourceCount = isPlayable ? 1 : null,
            SupportsResume = isPlayable ? true : null,
            SupportsSync = isPlayable ? true : null,
            CollectionType = CollectionType(item.Kind),
            ParentId = parentOverride ?? item.ParentEntityId,
            IsFolder = IsFolder(item.Kind),
            // Real Jellyfin always emits RunTimeTicks and blurhash containers non-null;
            // Manet's typed models treat them as required and drop any item where they are null.
            RunTimeTicks = runtimeTicks ?? 0,
            // Prismedia video tags are broad keywords, not Jellyfin genres. Infuse can surface these
            // arrays as description-like text, so expose tag-derived genre fields only for music where
            // clients use them as expected taxonomy.
            Genres = isMusic && item.Genres is { Count: > 0 } genreNames ? genreNames.ToArray() : [],
            GenreItems = isMusic ? GenreItemsFrom(item.Genres) : [],
            ImageBlurHashes = EmptyBlurHashes,
            ImageTags = primaryImageTag is null ? new Dictionary<string, string>() : new Dictionary<string, string> { [JellyfinProtocol.ImageTypes.Primary] = primaryImageTag },
            BackdropImageTags = imageTags.Backdrop is null ? [] : [imageTags.Backdrop],
            PrimaryImageAspectRatio = isMusic ? 1.0 : 0.6667,
            IndexNumber = isAudio ? TrackNumberFrom(item.SortOrder) : item.SortOrder,
            ParentIndexNumber = context?.ParentIndexNumber,
            SeriesId = context?.SeriesId,
            SeriesName = context?.SeriesName,
            SeasonId = context?.SeasonId,
            SeasonName = context?.SeasonName,
            SeriesPrimaryImageTag = context?.SeriesPrimaryImageTag,
            Album = isAudio ? context?.AlbumName : null,
            AlbumId = isAudio ? context?.AlbumId : null,
            AlbumPrimaryImageTag = isAudio ? context?.AlbumPrimaryImageTag : null,
            AlbumArtist = artistContext?.AlbumArtistName,
            AlbumArtists = AlbumArtistItems(artistContext),
            Artists = artistContext?.AlbumArtistName is { } artist ? [artist] : [],
            ArtistItems = AlbumArtistItems(artistContext),
            ParentLogoItemId = context?.ParentLogoItemId,
            ParentLogoImageTag = context?.ParentLogoImageTag,
            ParentBackdropItemId = context?.ParentBackdropItemId,
            ParentBackdropImageTags = context?.ParentBackdropImageTags ?? [],
            ParentThumbItemId = context?.ParentThumbItemId,
            ParentThumbImageTag = context?.ParentThumbImageTag,
            VideoType = isPlayable && !isAudio ? "VideoFile" : null,
            CanDelete = isPlayable ? true : null,
            EnableMediaSourceDisplay = isPlayable ? true : null,
            DisplayPreferencesId = item.Id.ToString("N"),
            LocalTrailerCount = isPlayable ? 0 : null,
            SpecialFeatureCount = isPlayable ? 0 : null,
            UserData = UserDataForThumbnail(item),
            MediaSources = isPlayable
                ? [CatalogMediaSource(item.Id, item.Title, VirtualItemPath(item.Id), container, null, runtimeTicks, streams ?? [], videoType: isAudio ? null : "VideoFile")]
                : [],
            MediaStreams = streams ?? []
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
        var isAudio = IsAudio(item.Kind);
        var isAlbum = item.Kind == EntityKind.AudioLibrary;
        var isMusic = IsMusic(item.Kind);
        var artistContext = isAudio || isAlbum ? context : null;
        long? runtimeTicks = isPlayable ? technical?.Duration?.Ticks ?? 0 : null;
        var container = isAudio
            ? technical?.Container ?? technical?.Codec ?? ContainerFromPath(source?.Path)
            : isPlayable ? technical?.Container ?? ContainerFromPath(source?.Path) : null;
        var streams = isAudio ? CatalogAudioStreams(technical) : isPlayable ? CatalogStreams(technical, container, subtitles) : null;
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
            Genres = isMusic && tags.Count > 0 ? tags.Select(tag => tag.Name).ToArray() : [],
            GenreItems = isMusic && tags.Count > 0 ? tags : [],
            // Infuse and other Jellyfin clients can surface tag/genre arrays as pseudo-description text.
            // Keep video descriptions description-first by omitting Prismedia tag-derived taxonomy from
            // Jellyfin video details; music keeps its taxonomy above where clients expect it.
            Tags = [],
            People = People(item),
            Studios = studios,
            ProviderIds = ProviderIds(links),
            ExternalUrls = ExternalUrls(links),
            RemoteTrailers = RemoteTrailers(links),
            Type = JellyfinType(item.Kind, item.ParentEntityId),
            MediaType = isAudio ? JellyfinProtocol.MediaTypes.Audio : isPlayable ? JellyfinProtocol.MediaTypes.Video : JellyfinProtocol.MediaTypes.Unknown,
            Path = isPlayable ? source?.Path ?? VirtualItemPath(item.Id) : null,
            LocationType = isPlayable || isMusic ? "FileSystem" : null,
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
            RunTimeTicks = runtimeTicks ?? 0,
            IndexNumber = context?.IndexNumber ?? (isAudio
                ? TrackNumberFrom(item.SortOrder)
                : PositionValue(position, "episode") ?? PositionValue(position, "sort") ?? item.SortOrder),
            ParentIndexNumber = PositionValue(position, "season") ?? context?.ParentIndexNumber,
            SeriesId = context?.SeriesId,
            SeriesName = context?.SeriesName,
            SeasonId = context?.SeasonId,
            SeasonName = context?.SeasonName,
            SeriesPrimaryImageTag = context?.SeriesPrimaryImageTag,
            Album = isAudio ? context?.AlbumName : null,
            AlbumId = isAudio ? context?.AlbumId : null,
            AlbumPrimaryImageTag = isAudio ? context?.AlbumPrimaryImageTag : null,
            AlbumArtist = artistContext?.AlbumArtistName,
            AlbumArtists = AlbumArtistItems(artistContext),
            Artists = artistContext?.AlbumArtistName is { } albumArtist ? [albumArtist] : [],
            ArtistItems = AlbumArtistItems(artistContext),
            ParentLogoItemId = context?.ParentLogoItemId,
            ParentLogoImageTag = context?.ParentLogoImageTag,
            ParentBackdropItemId = context?.ParentBackdropItemId,
            ParentBackdropImageTags = context?.ParentBackdropImageTags ?? [],
            ParentThumbItemId = context?.ParentThumbItemId,
            ParentThumbImageTag = context?.ParentThumbImageTag,
            VideoType = isPlayable && !isAudio ? "VideoFile" : null,
            CanDelete = isPlayable ? true : null,
            EnableMediaSourceDisplay = isPlayable ? true : null,
            DisplayPreferencesId = item.Id.ToString("N"),
            LocalTrailerCount = isPlayable ? 0 : null,
            SpecialFeatureCount = isPlayable ? 0 : null,
            ImageTags = MusicAwareImageTags(item.Id, image.Tags, isMusic, isAudio ? context?.AlbumPrimaryImageTag : null),
            BackdropImageTags = image.BackdropImageTags,
            ImageBlurHashes = EmptyBlurHashes,
            PrimaryImageAspectRatio = isMusic ? image.PrimaryImageAspectRatio ?? 1.0 : image.PrimaryImageAspectRatio,
            UserData = UserDataFor(item.Id, flags?.IsFavorite == true, playback, runtimeTicks),
            MediaSources = isPlayable
                ? [CatalogMediaSource(item.Id, item.Title, source?.Path ?? VirtualItemPath(item.Id), container, source?.Path, runtimeTicks, streams ?? [], technical, videoType: isAudio ? null : "VideoFile")]
                : [],
            MediaStreams = streams ?? [],
            Chapters = Chapters(markers)
        };
    }

    private static JellyfinBaseItemDto VirtualFolder(
        Guid id,
        string name,
        string collectionType,
        string serverId,
        string? coverPath = null,
        int? childCount = null,
        int? recursiveItemCount = null) {
        // Surface a representative item poster as the library tile artwork so clients like Infuse
        // render a real thumbnail instead of a generic folder icon. The tag is keyed on the resolved
        // cover path so the image endpoint can serve the same asset for the synthetic view id.
        var primaryTag = coverPath is null ? null : EtagFor(id, coverPath);
        return new() {
            Id = id,
            Name = name,
            ServerId = serverId,
            SortName = name,
            Type = JellyfinProtocol.ItemTypes.CollectionFolder,
            CollectionType = collectionType,
            IsFolder = true,
            // Match real Jellyfin's CollectionFolder shape. Strict clients (e.g. Manet) decode each
            // library into a typed model and drop any whose MediaType/LocationType/etc. are null, then
            // find no music library to sync. These scalar fields plus the counts make the view valid.
            MediaType = JellyfinProtocol.MediaTypes.Unknown,
            LocationType = "FileSystem",
            PlayAccess = "Full",
            CanDownload = false,
            CanDelete = false,
            ChildCount = childCount,
            RecursiveItemCount = recursiveItemCount,
            Etag = EtagFor(id, collectionType),
            ImageTags = primaryTag is null
                ? new Dictionary<string, string>()
                : new Dictionary<string, string> { [JellyfinProtocol.ImageTypes.Primary] = primaryTag },
            PrimaryImageAspectRatio = primaryTag is null ? null : 0.6667,
            UserData = UserDataFor(id, isFavorite: false, playback: null)
        };
    }

    private static JellyfinBaseItemDto FallbackSeasonFolder(
        IEntityCard series,
        string serverId,
        int episodeCount) {
        var images = ImageMetadata(series.Id, series.Capabilities);
        var id = FallbackSeasonIdFor(series.Id);
        return new() {
            Id = id,
            Name = series.Title,
            ServerId = serverId,
            SortName = series.Title,
            Type = JellyfinProtocol.ItemTypes.Season,
            MediaType = JellyfinProtocol.MediaTypes.Unknown,
            ParentId = series.Id,
            IsFolder = true,
            ChildCount = episodeCount,
            RecursiveItemCount = episodeCount,
            RunTimeTicks = 0,
            IndexNumber = 1,
            SeriesId = series.Id,
            SeriesName = series.Title,
            ImageTags = images.Tags,
            BackdropImageTags = images.BackdropImageTags,
            ImageBlurHashes = EmptyBlurHashes,
            PrimaryImageAspectRatio = images.PrimaryImageAspectRatio,
            Etag = EtagFor(id, series.Title),
            UserData = UserDataFor(id, isFavorite: false, playback: null),
            DisplayPreferencesId = id.ToString("N")
        };
    }

    private static ItemContext FallbackSeasonContextFor(IEntityCard series, int? indexNumber = null) {
        var images = ImageMetadata(series.Id, series.Capabilities);
        var seasonId = FallbackSeasonIdFor(series.Id);
        return new ItemContext(
            series.Id,
            series.Title,
            seasonId,
            series.Title,
            1,
            ParentId: seasonId,
            SeriesPrimaryImageTag: ImageTag(images, JellyfinProtocol.ImageTypes.Primary),
            ParentLogoItemId: ImageTag(images, JellyfinProtocol.ImageTypes.Logo) is null ? null : series.Id,
            ParentLogoImageTag: ImageTag(images, JellyfinProtocol.ImageTypes.Logo),
            ParentBackdropItemId: images.BackdropImageTags.Count == 0 ? null : series.Id,
            ParentBackdropImageTags: images.BackdropImageTags.Count == 0 ? null : images.BackdropImageTags,
            ParentThumbItemId: ImageTag(images, JellyfinProtocol.ImageTypes.Thumb) is null ? null : series.Id,
            ParentThumbImageTag: ImageTag(images, JellyfinProtocol.ImageTypes.Thumb),
            IndexNumber: indexNumber);
    }

    private async Task<ItemContext?> ParentContextForAsync(
        IEntityCard parent,
        JellyfinContentVisibility visibility,
        CancellationToken cancellationToken) {
        // Albums (audio-library) parent their tracks: carry the album reference plus the album artist
        // resolved from the album's own parent (music-artist), so each track DTO is self-describing.
        if (parent.Kind == EntityKind.AudioLibrary) {
            var albumImages = ImageMetadata(parent.Id, parent.Capabilities);
            var albumPrimaryImageTag =
                ImageTag(albumImages, JellyfinProtocol.ImageTypes.Primary) ??
                (await ResolveEntityPrimaryImageAssetAsync(parent, visibility, cancellationToken))?.ImageTag;
            IEntityCard? artist = null;
            if (parent.ParentEntityId is { } artistId) {
                artist = await GetVisibleCardAsync(artistId, visibility, cancellationToken);
            }

            return new ItemContext(
                null, null, null, null, null,
                ParentId: parent.Id,
                AlbumId: parent.Id,
                AlbumName: parent.Title,
                AlbumPrimaryImageTag: albumPrimaryImageTag,
                AlbumArtistId: artist?.Id,
                AlbumArtistName: artist?.Title);
        }

        // Artists (music-artist) parent their albums: each album DTO carries this artist as its album artist.
        if (parent.Kind == EntityKind.MusicArtist) {
            return new ItemContext(
                null, null, null, null, null,
                ParentId: parent.Id,
                AlbumArtistId: parent.Id,
                AlbumArtistName: parent.Title);
        }

        if (parent.Kind == EntityKind.VideoSeries) {
            // Episodes directly under a series (no season): parent-image fields all come from the series.
            var seriesImages = ImageMetadata(parent.Id, parent.Capabilities);
            return new ItemContext(
                parent.Id,
                parent.Title,
                null,
                null,
                null,
                SeriesPrimaryImageTag: ImageTag(seriesImages, JellyfinProtocol.ImageTypes.Primary),
                ParentLogoItemId: ImageTag(seriesImages, JellyfinProtocol.ImageTypes.Logo) is null ? null : parent.Id,
                ParentLogoImageTag: ImageTag(seriesImages, JellyfinProtocol.ImageTypes.Logo),
                ParentBackdropItemId: seriesImages.BackdropImageTags.Count == 0 ? null : parent.Id,
                ParentBackdropImageTags: seriesImages.BackdropImageTags.Count == 0 ? null : seriesImages.BackdropImageTags,
                ParentThumbItemId: ImageTag(seriesImages, JellyfinProtocol.ImageTypes.Thumb) is null ? null : parent.Id,
                ParentThumbImageTag: ImageTag(seriesImages, JellyfinProtocol.ImageTypes.Thumb));
        }

        if (parent.Kind != EntityKind.VideoSeason) {
            return null;
        }

        IEntityCard? series = null;
        if (parent.ParentEntityId is { } seriesId) {
            series = await GetVisibleCardAsync(seriesId, visibility, cancellationToken);
        }

        // Series supplies the backdrop/logo/primary artwork; the season supplies the thumb
        // (falling back to the series thumb) — matching how Jellyfin populates episode parent images.
        var seriesImageMeta = series is null
            ? null
            : ImageMetadata(series.Id, series.Capabilities);
        var seasonImageMeta = ImageMetadata(parent.Id, parent.Capabilities);
        var thumbTag = ImageTag(seasonImageMeta, JellyfinProtocol.ImageTypes.Thumb) ?? (seriesImageMeta is null ? null : ImageTag(seriesImageMeta, JellyfinProtocol.ImageTypes.Thumb));
        var thumbItemId = ImageTag(seasonImageMeta, JellyfinProtocol.ImageTypes.Thumb) is not null
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
            SeriesPrimaryImageTag: seriesImageMeta is null ? null : ImageTag(seriesImageMeta, JellyfinProtocol.ImageTypes.Primary),
            ParentLogoItemId: seriesImageMeta is not null && ImageTag(seriesImageMeta, JellyfinProtocol.ImageTypes.Logo) is not null ? series?.Id : null,
            ParentLogoImageTag: seriesImageMeta is null ? null : ImageTag(seriesImageMeta, JellyfinProtocol.ImageTypes.Logo),
            ParentBackdropItemId: backdropTags.Count == 0 ? null : series?.Id,
            ParentBackdropImageTags: backdropTags.Count == 0 ? null : backdropTags,
            ParentThumbItemId: thumbItemId,
            ParentThumbImageTag: thumbTag);
    }

    private static string? ImageTag(JellyfinImageMetadata images, string type) =>
        images.Tags.TryGetValue(type, out var tag) ? tag : null;

    private static IReadOnlyDictionary<string, string> MusicAwareImageTags(
        Guid id,
        IReadOnlyDictionary<string, string> tags,
        bool isMusic,
        string? albumPrimaryImageTag = null) {
        if (!isMusic || tags.ContainsKey(JellyfinProtocol.ImageTypes.Primary)) {
            return tags;
        }

        var next = new Dictionary<string, string>(tags, StringComparer.OrdinalIgnoreCase) {
            [JellyfinProtocol.ImageTypes.Primary] = MusicAwarePrimaryImageTag(id, null, isMusic, albumPrimaryImageTag)!
        };
        return next;
    }

    private static string? MusicAwarePrimaryImageTag(
        Guid id,
        string? primaryImageTag,
        bool isMusic,
        string? albumPrimaryImageTag = null) =>
        primaryImageTag ?? albumPrimaryImageTag ?? (isMusic ? EtagFor(id, PrismediaLogoImagePath) : null);

    /// <summary>Empty blurhash map matching real Jellyfin's always-present <c>ImageBlurHashes</c> object.</summary>
    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> EmptyBlurHashes =
        new Dictionary<string, IReadOnlyDictionary<string, string>>();

    /// <summary>
    /// Maps tag names to Jellyfin <c>GenreItems</c>. The thumbnail projection only carries names, so
    /// each genre id is derived deterministically from its name — stable across responses for client
    /// caching, matching real Jellyfin's name+id shape for display.
    /// </summary>
    private static IReadOnlyList<JellyfinNameGuidPairDto> GenreItemsFrom(IReadOnlyList<string>? genres) =>
        genres is { Count: > 0 }
            ? genres.Select(name => new JellyfinNameGuidPairDto(name, DeterministicId("genre:" + name.ToLowerInvariant()))).ToArray()
            : [];

    /// <summary>Derives a stable GUID from a string via MD5, for synthesized ids (e.g. genres).</summary>
    private static Guid DeterministicId(string value) =>
        new(MD5.HashData(Encoding.UTF8.GetBytes(value)));

    /// <summary>
    /// Converts a stored album-global sort order (0-based, set from the embedded track tag on
    /// single-disc albums) into a 1-based Jellyfin <c>IndexNumber</c> track number. Null sort order
    /// yields a null track number rather than a misleading "1".
    /// </summary>
    private static int? TrackNumberFrom(int? sortOrder) =>
        sortOrder is { } order ? order + 1 : null;

    /// <summary>
    /// Builds the single-element album-artist name/id pair list used for both <c>ArtistItems</c> and
    /// <c>AlbumArtists</c>, or an empty list when the context carries no resolved album artist.
    /// </summary>
    private static IReadOnlyList<JellyfinNameGuidPairDto> AlbumArtistItems(ItemContext? context) =>
        context?.AlbumArtistId is { } artistId && context.AlbumArtistName is { } artistName
            ? [new JellyfinNameGuidPairDto(artistName, artistId)]
            : [];

}
