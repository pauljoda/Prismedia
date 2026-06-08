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
/// Jellyfin DTO field-mapping helpers for <see cref="JellyfinCatalogService"/>: filters, image metadata, tags/studios/people/credits, dates, provider ids/urls, chapters, and type/sort mapping.
/// </summary>
public sealed partial class JellyfinCatalogService {
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
            ExternalIdProviders.AniDb => "AniDB",
            ExternalIdProviders.Imdb => "IMDb",
            ExternalIdProviders.Tmdb => "TMDb",
            ExternalIdProviders.Tvdb => "TVDb",
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

    private static JellyfinUserItemDataDto UserDataForThumbnail(EntityThumbnail item) {
        var progress = item.Progress is { } value ? Math.Clamp(value, 0, 1) : (double?)null;
        var playCount = item.PlayCount ?? 0;
        var played = progress >= 1 || playCount > 0 && progress is null;
        return new JellyfinUserItemDataDto(
            PlaybackPositionTicks: 0,
            PlayCount: playCount,
            IsFavorite: item.IsFavorite,
            Played: played,
            Key: item.Id.ToString("N"),
            ItemId: item.Id.ToString("N"),
            PlayedPercentage: played ? 100d : progress is null ? null : progress * 100d,
            LastPlayedDate: null);
    }

    private static bool IsPlayableVideo(string kind) => kind.Equals("video", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether a kind is a playable leaf in the Jellyfin projection. Movies and videos both map to
    /// playable items (a movie streams through its single video child, resolved in the source service).
    /// </summary>
    private static bool IsPlayable(string kind) =>
        kind.Equals("video", StringComparison.OrdinalIgnoreCase) ||
        kind.Equals("movie", StringComparison.OrdinalIgnoreCase) ||
        IsAudio(kind);

    /// <summary>Whether a kind is a playable audio leaf (music track) in the Jellyfin projection.</summary>
    private static bool IsAudio(string kind) =>
        kind.Equals("audio-track", StringComparison.OrdinalIgnoreCase);

    private static bool IsFolder(string kind) =>
        kind is "video-series" or "video-season" or "collection" or "person"
            or "music-artist" or "audio-library";

    private static string JellyfinType(string kind, Guid? parentId) =>
        kind.Trim().ToLowerInvariant() switch {
            "movie" => JellyfinProtocol.ItemTypes.Movie,
            "video" => parentId is null ? JellyfinProtocol.ItemTypes.Video : JellyfinProtocol.ItemTypes.Episode,
            "video-series" => JellyfinProtocol.ItemTypes.Series,
            "video-season" => JellyfinProtocol.ItemTypes.Season,
            "collection" => JellyfinProtocol.ItemTypes.BoxSet,
            "person" => JellyfinProtocol.ItemTypes.Person,
            "music-artist" => JellyfinProtocol.ItemTypes.MusicArtist,
            "audio-library" => JellyfinProtocol.ItemTypes.MusicAlbum,
            "audio-track" => JellyfinProtocol.ItemTypes.Audio,
            _ => JellyfinProtocol.ItemTypes.Folder
        };

    private static string? CollectionType(string kind) =>
        kind.Trim().ToLowerInvariant() switch {
            "video-series" => JellyfinProtocol.CollectionTypes.Shows,
            "collection" => JellyfinProtocol.CollectionTypes.BoxSets,
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
            ".jpg" or ".jpeg" => MediaContentTypes.ImageJpeg,
            ".png" => MediaContentTypes.ImagePng,
            ".webp" => MediaContentTypes.ImageWebp,
            ".gif" => MediaContentTypes.ImageGif,
            ".avif" => MediaContentTypes.ImageAvif,
            _ => MediaContentTypes.OctetStream
        };
}
