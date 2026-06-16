using System.Text.Json.Serialization;

namespace Prismedia.Contracts.Jellyfin;

/// <summary>Jellyfin-compatible paged result envelope.</summary>
public sealed record JellyfinQueryResult<T>(
    [property: JsonPropertyName("Items")] IReadOnlyList<T> Items,
    [property: JsonPropertyName("TotalRecordCount")] int TotalRecordCount,
    [property: JsonPropertyName("StartIndex")] int StartIndex);

/// <summary>Jellyfin-compatible base item DTO with the subset Prismedia v1 exposes.</summary>
public sealed record JellyfinBaseItemDto {
    [JsonPropertyName("Name")]
    public required string Name { get; init; }

    [JsonPropertyName("ServerId")]
    public required string ServerId { get; init; }

    [JsonPropertyName("Id")]
    [JsonConverter(typeof(JellyfinGuidConverter))]
    public required Guid Id { get; init; }

    [JsonPropertyName("Etag")]
    public string? Etag { get; init; }

    [JsonPropertyName("OriginalTitle")]
    public string? OriginalTitle { get; init; }

    [JsonPropertyName("DateCreated")]
    [JsonConverter(typeof(JellyfinDateConverter))]
    public DateTimeOffset? DateCreated { get; init; }

    [JsonPropertyName("StartDate")]
    [JsonConverter(typeof(JellyfinDateConverter))]
    public DateTimeOffset? StartDate { get; init; }

    [JsonPropertyName("EndDate")]
    [JsonConverter(typeof(JellyfinDateConverter))]
    public DateTimeOffset? EndDate { get; init; }

    [JsonPropertyName("SortName")]
    public string? SortName { get; init; }

    [JsonPropertyName("PremiereDate")]
    [JsonConverter(typeof(JellyfinDateConverter))]
    public DateTimeOffset? PremiereDate { get; init; }

    [JsonPropertyName("ProductionYear")]
    public int? ProductionYear { get; init; }

    [JsonPropertyName("Overview")]
    public string? Overview { get; init; }

    [JsonPropertyName("OfficialRating")]
    public string? OfficialRating { get; init; }

    [JsonPropertyName("CustomRating")]
    public string? CustomRating { get; init; }

    [JsonPropertyName("CommunityRating")]
    public float? CommunityRating { get; init; }

    [JsonPropertyName("CriticRating")]
    public float? CriticRating { get; init; }

    [JsonPropertyName("Genres")]
    public IReadOnlyList<string> Genres { get; init; } = [];

    [JsonPropertyName("GenreItems")]
    public IReadOnlyList<JellyfinNameGuidPairDto> GenreItems { get; init; } = [];

    [JsonPropertyName("Tags")]
    public IReadOnlyList<string> Tags { get; init; } = [];

    [JsonPropertyName("People")]
    public IReadOnlyList<JellyfinBaseItemPersonDto> People { get; init; } = [];

    [JsonPropertyName("Studios")]
    public IReadOnlyList<JellyfinNameGuidPairDto> Studios { get; init; } = [];

    [JsonPropertyName("ProviderIds")]
    public IReadOnlyDictionary<string, string>? ProviderIds { get; init; }

    [JsonPropertyName("ExternalUrls")]
    public IReadOnlyList<JellyfinExternalUrlDto> ExternalUrls { get; init; } = [];

    [JsonPropertyName("RemoteTrailers")]
    public IReadOnlyList<JellyfinMediaUrlDto> RemoteTrailers { get; init; } = [];

    [JsonPropertyName("Type")]
    public required string Type { get; init; }

    [JsonPropertyName("MediaType")]
    public string? MediaType { get; init; }

    [JsonPropertyName("Path")]
    public string? Path { get; init; }

    [JsonPropertyName("LocationType")]
    public string? LocationType { get; init; }

    [JsonPropertyName("PlayAccess")]
    public string? PlayAccess { get; init; }

    [JsonPropertyName("Container")]
    public string? Container { get; init; }

    [JsonPropertyName("MediaSourceCount")]
    public int? MediaSourceCount { get; init; }

    [JsonPropertyName("SupportsResume")]
    public bool? SupportsResume { get; init; }

    [JsonPropertyName("SupportsSync")]
    public bool? SupportsSync { get; init; }

    [JsonPropertyName("CanDownload")]
    public bool? CanDownload { get; init; }

    [JsonPropertyName("CanDelete")]
    public bool? CanDelete { get; init; }

    [JsonPropertyName("EnableMediaSourceDisplay")]
    public bool? EnableMediaSourceDisplay { get; init; }

    [JsonPropertyName("ChannelId")]
    public string? ChannelId { get; init; }

    [JsonPropertyName("DisplayPreferencesId")]
    public string? DisplayPreferencesId { get; init; }

    [JsonPropertyName("VideoType")]
    public string? VideoType { get; init; }

    [JsonPropertyName("Taglines")]
    public IReadOnlyList<string> Taglines { get; init; } = [];

    [JsonPropertyName("ProductionLocations")]
    public IReadOnlyList<string> ProductionLocations { get; init; } = [];

    [JsonPropertyName("LocalTrailerCount")]
    public int? LocalTrailerCount { get; init; }

    [JsonPropertyName("SpecialFeatureCount")]
    public int? SpecialFeatureCount { get; init; }

    [JsonPropertyName("HasSubtitles")]
    public bool? HasSubtitles { get; init; }

    [JsonPropertyName("Width")]
    public int? Width { get; init; }

    [JsonPropertyName("Height")]
    public int? Height { get; init; }

    [JsonPropertyName("AspectRatio")]
    public string? AspectRatio { get; init; }

    [JsonPropertyName("IsHD")]
    public bool? IsHD { get; init; }

    [JsonPropertyName("CollectionType")]
    public string? CollectionType { get; init; }

    [JsonPropertyName("ParentId")]
    [JsonConverter(typeof(JellyfinNullableGuidConverter))]
    public Guid? ParentId { get; init; }

    [JsonPropertyName("IsFolder")]
    public bool IsFolder { get; init; }

    [JsonPropertyName("ChildCount")]
    public int? ChildCount { get; init; }

    [JsonPropertyName("RecursiveItemCount")]
    public int? RecursiveItemCount { get; init; }

    [JsonPropertyName("RunTimeTicks")]
    public long? RunTimeTicks { get; init; }

    [JsonPropertyName("IndexNumber")]
    public int? IndexNumber { get; init; }

    [JsonPropertyName("IndexNumberEnd")]
    public int? IndexNumberEnd { get; init; }

    [JsonPropertyName("ParentIndexNumber")]
    public int? ParentIndexNumber { get; init; }

    [JsonPropertyName("SeriesId")]
    [JsonConverter(typeof(JellyfinNullableGuidConverter))]
    public Guid? SeriesId { get; init; }

    [JsonPropertyName("SeriesName")]
    public string? SeriesName { get; init; }

    [JsonPropertyName("SeasonId")]
    [JsonConverter(typeof(JellyfinNullableGuidConverter))]
    public Guid? SeasonId { get; init; }

    [JsonPropertyName("SeasonName")]
    public string? SeasonName { get; init; }

    [JsonPropertyName("SeriesPrimaryImageTag")]
    public string? SeriesPrimaryImageTag { get; init; }

    [JsonPropertyName("Album")]
    public string? Album { get; init; }

    [JsonPropertyName("AlbumId")]
    [JsonConverter(typeof(JellyfinNullableGuidConverter))]
    public Guid? AlbumId { get; init; }

    [JsonPropertyName("AlbumPrimaryImageTag")]
    public string? AlbumPrimaryImageTag { get; init; }

    [JsonPropertyName("AlbumArtist")]
    public string? AlbumArtist { get; init; }

    [JsonPropertyName("AlbumArtists")]
    public IReadOnlyList<JellyfinNameGuidPairDto> AlbumArtists { get; init; } = [];

    [JsonPropertyName("Artists")]
    public IReadOnlyList<string> Artists { get; init; } = [];

    [JsonPropertyName("ArtistItems")]
    public IReadOnlyList<JellyfinNameGuidPairDto> ArtistItems { get; init; } = [];

    [JsonPropertyName("ParentLogoItemId")]
    [JsonConverter(typeof(JellyfinNullableGuidConverter))]
    public Guid? ParentLogoItemId { get; init; }

    [JsonPropertyName("ParentLogoImageTag")]
    public string? ParentLogoImageTag { get; init; }

    [JsonPropertyName("ParentBackdropItemId")]
    [JsonConverter(typeof(JellyfinNullableGuidConverter))]
    public Guid? ParentBackdropItemId { get; init; }

    [JsonPropertyName("ParentBackdropImageTags")]
    public IReadOnlyList<string> ParentBackdropImageTags { get; init; } = [];

    [JsonPropertyName("ParentThumbItemId")]
    [JsonConverter(typeof(JellyfinNullableGuidConverter))]
    public Guid? ParentThumbItemId { get; init; }

    [JsonPropertyName("ParentThumbImageTag")]
    public string? ParentThumbImageTag { get; init; }

    [JsonPropertyName("ImageTags")]
    public IReadOnlyDictionary<string, string> ImageTags { get; init; } = new Dictionary<string, string>();

    [JsonPropertyName("BackdropImageTags")]
    public IReadOnlyList<string> BackdropImageTags { get; init; } = [];

    /// <summary>
    /// Per-image-type blurhash map (image type → image tag → blurhash) matching Jellyfin's shape.
    /// Real Jellyfin always emits this key; inner maps are empty until blurhashes are computed.
    /// </summary>
    [JsonPropertyName("ImageBlurHashes")]
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? ImageBlurHashes { get; init; }

    [JsonPropertyName("PrimaryImageAspectRatio")]
    public double? PrimaryImageAspectRatio { get; init; }

    [JsonPropertyName("UserData")]
    public JellyfinUserItemDataDto? UserData { get; init; }

    [JsonPropertyName("MediaSources")]
    public IReadOnlyList<JellyfinCatalogMediaSourceDto> MediaSources { get; init; } = [];

    [JsonPropertyName("MediaStreams")]
    public IReadOnlyList<JellyfinCatalogMediaStreamDto> MediaStreams { get; init; } = [];

    [JsonPropertyName("Chapters")]
    public IReadOnlyList<JellyfinChapterInfoDto> Chapters { get; init; } = [];
}

/// <summary>Jellyfin-compatible name/id pair used by genre, studio, and related item arrays.</summary>
public sealed record JellyfinNameGuidPairDto(
    [property: JsonPropertyName("Name")] string Name,
    [property: JsonPropertyName("Id")][property: JsonConverter(typeof(JellyfinGuidConverter))] Guid Id);

/// <summary>Jellyfin-compatible external URL reference.</summary>
public sealed record JellyfinExternalUrlDto(
    [property: JsonPropertyName("Name")] string Name,
    [property: JsonPropertyName("Url")] string Url);

/// <summary>Jellyfin-compatible media URL reference, used for remote trailers.</summary>
public sealed record JellyfinMediaUrlDto(
    [property: JsonPropertyName("Name")] string Name,
    [property: JsonPropertyName("Url")] string Url);

/// <summary>Jellyfin-compatible credited person entry.</summary>
public sealed record JellyfinBaseItemPersonDto(
    [property: JsonPropertyName("Name")] string Name,
    [property: JsonPropertyName("Id")][property: JsonConverter(typeof(JellyfinGuidConverter))] Guid Id,
    [property: JsonPropertyName("Role")] string? Role,
    [property: JsonPropertyName("Type")] string Type,
    [property: JsonPropertyName("PrimaryImageTag")] string? PrimaryImageTag,
    [property: JsonPropertyName("ImageBlurHashes")] IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? ImageBlurHashes = null);

/// <summary>Jellyfin-compatible chapter marker.</summary>
public sealed record JellyfinChapterInfoDto {
    [JsonPropertyName("StartPositionTicks")]
    public long StartPositionTicks { get; init; }

    [JsonPropertyName("Name")]
    public required string Name { get; init; }

    [JsonPropertyName("ImagePath")]
    public string? ImagePath { get; init; }

    [JsonPropertyName("ImageDateModified")]
    [JsonConverter(typeof(JellyfinDateConverter))]
    public DateTimeOffset? ImageDateModified { get; init; }

    [JsonPropertyName("ImageTag")]
    public string? ImageTag { get; init; }
}

/// <summary>Minimal Jellyfin-compatible user data DTO backed by Prismedia global state.</summary>
public sealed record JellyfinUserItemDataDto(
    [property: JsonPropertyName("PlaybackPositionTicks")] long PlaybackPositionTicks,
    [property: JsonPropertyName("PlayCount")] int PlayCount,
    [property: JsonPropertyName("IsFavorite")] bool IsFavorite,
    [property: JsonPropertyName("Played")] bool Played,
    [property: JsonPropertyName("Key")] string Key,
    [property: JsonPropertyName("ItemId")] string ItemId,
    [property: JsonPropertyName("PlayedPercentage")] double? PlayedPercentage = null,
    [property: JsonPropertyName("LastPlayedDate")][property: JsonConverter(typeof(JellyfinDateConverter))] DateTimeOffset? LastPlayedDate = null);

/// <summary>
/// Jellyfin-compatible media segment (intro/outro/recap/preview/commercial skip marker).
/// Prismedia does not yet produce segments; the type exists so the empty paged result is typed.
/// </summary>
public sealed record JellyfinMediaSegmentDto(
    [property: JsonPropertyName("Id")] string Id,
    [property: JsonPropertyName("ItemId")] string ItemId,
    [property: JsonPropertyName("Type")] string Type,
    [property: JsonPropertyName("StartTicks")] long StartTicks,
    [property: JsonPropertyName("EndTicks")] long EndTicks);
