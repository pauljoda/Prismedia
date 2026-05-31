using System.Text.Json.Serialization;

namespace Prismedia.Contracts.Jellyfin;

/// <summary>Jellyfin-compatible paged result envelope.</summary>
public sealed record JellyfinQueryResult<T>(
    [property: JsonPropertyName("Items")] IReadOnlyList<T> Items,
    [property: JsonPropertyName("TotalRecordCount")] int TotalRecordCount,
    [property: JsonPropertyName("StartIndex")] int StartIndex);

/// <summary>Minimal Jellyfin-compatible public system information.</summary>
public sealed record JellyfinPublicSystemInfo(
    [property: JsonPropertyName("LocalAddress")] string LocalAddress,
    [property: JsonPropertyName("ServerName")] string ServerName,
    [property: JsonPropertyName("Version")] string Version,
    [property: JsonPropertyName("ProductName")] string ProductName,
    [property: JsonPropertyName("Id")] string Id,
    [property: JsonPropertyName("StartupWizardCompleted")] bool StartupWizardCompleted);

/// <summary>Minimal Jellyfin-compatible private system information.</summary>
public sealed record JellyfinSystemInfo(
    [property: JsonPropertyName("LocalAddress")] string LocalAddress,
    [property: JsonPropertyName("ServerName")] string ServerName,
    [property: JsonPropertyName("Version")] string Version,
    [property: JsonPropertyName("ProductName")] string ProductName,
    [property: JsonPropertyName("Id")] string Id,
    [property: JsonPropertyName("StartupWizardCompleted")] bool StartupWizardCompleted,
    [property: JsonPropertyName("OperatingSystem")] string OperatingSystem,
    [property: JsonPropertyName("PackageName")] string PackageName,
    [property: JsonPropertyName("ServerNameRaw")] string? ServerNameRaw = null);

/// <summary>Jellyfin-compatible authenticate-by-name request.</summary>
public sealed record JellyfinAuthenticateByNameRequest {
    [JsonPropertyName("Username")]
    public string? Username { get; init; }

    [JsonPropertyName("Pw")]
    public string? Pw { get; init; }

    [JsonPropertyName("Password")]
    public string? Password { get; init; }

    /// <summary>Password value supplied by either Jellyfin's <c>Pw</c> field or older client <c>Password</c> field.</summary>
    [JsonIgnore]
    public string? EffectivePassword => Pw ?? Password;
}

/// <summary>Jellyfin-compatible authentication result.</summary>
public sealed record JellyfinAuthenticationResult(
    [property: JsonPropertyName("User")] JellyfinUserDto User,
    [property: JsonPropertyName("SessionInfo")] JellyfinSessionInfoDto SessionInfo,
    [property: JsonPropertyName("AccessToken")] string AccessToken,
    [property: JsonPropertyName("ServerId")] string ServerId);

/// <summary>Jellyfin-compatible user DTO for fake Prismedia profiles.</summary>
public sealed record JellyfinUserDto(
    [property: JsonPropertyName("Name")] string Name,
    [property: JsonPropertyName("ServerId")] string ServerId,
    [property: JsonPropertyName("ServerName")] string ServerName,
    [property: JsonPropertyName("Id")] Guid Id,
    [property: JsonPropertyName("HasPassword")] bool HasPassword,
    [property: JsonPropertyName("HasConfiguredPassword")] bool HasConfiguredPassword,
    [property: JsonPropertyName("HasConfiguredEasyPassword")] bool HasConfiguredEasyPassword,
    [property: JsonPropertyName("EnableAutoLogin")] bool EnableAutoLogin,
    [property: JsonPropertyName("LastLoginDate")] DateTimeOffset? LastLoginDate,
    [property: JsonPropertyName("LastActivityDate")] DateTimeOffset? LastActivityDate,
    [property: JsonPropertyName("Policy")] JellyfinUserPolicyDto Policy,
    [property: JsonPropertyName("Configuration")] JellyfinUserConfigurationDto Configuration);

/// <summary>Minimal Jellyfin-compatible user policy.</summary>
public sealed record JellyfinUserPolicyDto(
    [property: JsonPropertyName("IsAdministrator")] bool IsAdministrator,
    [property: JsonPropertyName("IsHidden")] bool IsHidden,
    [property: JsonPropertyName("IsDisabled")] bool IsDisabled,
    [property: JsonPropertyName("EnableRemoteControlOfOtherUsers")] bool EnableRemoteControlOfOtherUsers,
    [property: JsonPropertyName("EnableSharedDeviceControl")] bool EnableSharedDeviceControl,
    [property: JsonPropertyName("EnableContentDeletion")] bool EnableContentDeletion,
    [property: JsonPropertyName("EnableContentDownloading")] bool EnableContentDownloading,
    [property: JsonPropertyName("EnableSyncTranscoding")] bool EnableSyncTranscoding,
    [property: JsonPropertyName("EnableMediaPlayback")] bool EnableMediaPlayback);

/// <summary>Minimal Jellyfin-compatible user configuration.</summary>
public sealed record JellyfinUserConfigurationDto(
    [property: JsonPropertyName("AudioLanguagePreference")] string? AudioLanguagePreference,
    [property: JsonPropertyName("PlayDefaultAudioTrack")] bool PlayDefaultAudioTrack,
    [property: JsonPropertyName("SubtitleLanguagePreference")] string? SubtitleLanguagePreference,
    [property: JsonPropertyName("DisplayMissingEpisodes")] bool DisplayMissingEpisodes,
    [property: JsonPropertyName("GroupedFolders")] IReadOnlyList<string> GroupedFolders,
    [property: JsonPropertyName("SubtitleMode")] string SubtitleMode);

/// <summary>Minimal Jellyfin-compatible session info.</summary>
public sealed record JellyfinSessionInfoDto(
    [property: JsonPropertyName("Id")] string Id,
    [property: JsonPropertyName("UserId")] Guid UserId,
    [property: JsonPropertyName("UserName")] string UserName,
    [property: JsonPropertyName("Client")] string? Client,
    [property: JsonPropertyName("DeviceName")] string? DeviceName,
    [property: JsonPropertyName("DeviceId")] string? DeviceId,
    [property: JsonPropertyName("ApplicationVersion")] string? ApplicationVersion,
    [property: JsonPropertyName("IsActive")] bool IsActive);

/// <summary>Jellyfin-compatible branding configuration.</summary>
public sealed record JellyfinBrandingConfiguration(
    [property: JsonPropertyName("LoginDisclaimer")] string LoginDisclaimer,
    [property: JsonPropertyName("CustomCss")] string CustomCss,
    [property: JsonPropertyName("SplashscreenEnabled")] bool SplashscreenEnabled);

/// <summary>Jellyfin-compatible base item DTO with the subset Prismedia v1 exposes.</summary>
public sealed record JellyfinBaseItemDto {
    [JsonPropertyName("Name")]
    public required string Name { get; init; }

    [JsonPropertyName("ServerId")]
    public required string ServerId { get; init; }

    [JsonPropertyName("Id")]
    public required Guid Id { get; init; }

    [JsonPropertyName("Etag")]
    public string? Etag { get; init; }

    [JsonPropertyName("OriginalTitle")]
    public string? OriginalTitle { get; init; }

    [JsonPropertyName("DateCreated")]
    public DateTimeOffset? DateCreated { get; init; }

    [JsonPropertyName("StartDate")]
    public DateTimeOffset? StartDate { get; init; }

    [JsonPropertyName("EndDate")]
    public DateTimeOffset? EndDate { get; init; }

    [JsonPropertyName("SortName")]
    public string? SortName { get; init; }

    [JsonPropertyName("PremiereDate")]
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
    public IReadOnlyList<string>? Genres { get; init; }

    [JsonPropertyName("GenreItems")]
    public IReadOnlyList<JellyfinNameGuidPairDto>? GenreItems { get; init; }

    [JsonPropertyName("Tags")]
    public IReadOnlyList<string>? Tags { get; init; }

    [JsonPropertyName("People")]
    public IReadOnlyList<JellyfinBaseItemPersonDto>? People { get; init; }

    [JsonPropertyName("Studios")]
    public IReadOnlyList<JellyfinNameGuidPairDto>? Studios { get; init; }

    [JsonPropertyName("ProviderIds")]
    public IReadOnlyDictionary<string, string>? ProviderIds { get; init; }

    [JsonPropertyName("ExternalUrls")]
    public IReadOnlyList<JellyfinExternalUrlDto>? ExternalUrls { get; init; }

    [JsonPropertyName("RemoteTrailers")]
    public IReadOnlyList<JellyfinMediaUrlDto>? RemoteTrailers { get; init; }

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
    public IReadOnlyList<string>? Taglines { get; init; }

    [JsonPropertyName("ProductionLocations")]
    public IReadOnlyList<string>? ProductionLocations { get; init; }

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
    public Guid? SeriesId { get; init; }

    [JsonPropertyName("SeriesName")]
    public string? SeriesName { get; init; }

    [JsonPropertyName("SeasonId")]
    public Guid? SeasonId { get; init; }

    [JsonPropertyName("SeasonName")]
    public string? SeasonName { get; init; }

    [JsonPropertyName("SeriesPrimaryImageTag")]
    public string? SeriesPrimaryImageTag { get; init; }

    [JsonPropertyName("ParentLogoItemId")]
    public Guid? ParentLogoItemId { get; init; }

    [JsonPropertyName("ParentLogoImageTag")]
    public string? ParentLogoImageTag { get; init; }

    [JsonPropertyName("ParentBackdropItemId")]
    public Guid? ParentBackdropItemId { get; init; }

    [JsonPropertyName("ParentBackdropImageTags")]
    public IReadOnlyList<string>? ParentBackdropImageTags { get; init; }

    [JsonPropertyName("ParentThumbItemId")]
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
    public IReadOnlyList<JellyfinCatalogMediaSourceDto>? MediaSources { get; init; }

    [JsonPropertyName("MediaStreams")]
    public IReadOnlyList<JellyfinCatalogMediaStreamDto>? MediaStreams { get; init; }

    [JsonPropertyName("Chapters")]
    public IReadOnlyList<JellyfinChapterInfoDto>? Chapters { get; init; }
}

/// <summary>Jellyfin-compatible name/id pair used by genre, studio, and related item arrays.</summary>
public sealed record JellyfinNameGuidPairDto(
    [property: JsonPropertyName("Name")] string Name,
    [property: JsonPropertyName("Id")] Guid Id);

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
    [property: JsonPropertyName("Id")] Guid Id,
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
    public DateTimeOffset? ImageDateModified { get; init; }

    [JsonPropertyName("ImageTag")]
    public string? ImageTag { get; init; }
}

/// <summary>Minimal Jellyfin-compatible media source embedded in catalog item DTOs.</summary>
public sealed record JellyfinCatalogMediaSourceDto {
    [JsonPropertyName("Id")]
    public required string Id { get; init; }

    [JsonPropertyName("Path")]
    public required string Path { get; init; }

    [JsonPropertyName("Protocol")]
    public string Protocol { get; init; } = "File";

    [JsonPropertyName("Type")]
    public string Type { get; init; } = "Default";

    [JsonPropertyName("Container")]
    public string? Container { get; init; }

    [JsonPropertyName("Size")]
    public long? Size { get; init; }

    [JsonPropertyName("Name")]
    public string? Name { get; init; }

    [JsonPropertyName("ETag")]
    public string? ETag { get; init; }

    [JsonPropertyName("RunTimeTicks")]
    public long? RunTimeTicks { get; init; }

    [JsonPropertyName("Bitrate")]
    public int? Bitrate { get; init; }

    [JsonPropertyName("VideoType")]
    public string VideoType { get; init; } = "VideoFile";

    [JsonPropertyName("IsRemote")]
    public bool IsRemote { get; init; }

    [JsonPropertyName("ReadAtNativeFramerate")]
    public bool ReadAtNativeFramerate { get; init; }

    [JsonPropertyName("IgnoreDts")]
    public bool IgnoreDts { get; init; }

    [JsonPropertyName("IgnoreIndex")]
    public bool IgnoreIndex { get; init; }

    [JsonPropertyName("GenPtsInput")]
    public bool GenPtsInput { get; init; }

    [JsonPropertyName("SupportsTranscoding")]
    public bool SupportsTranscoding { get; init; } = true;

    [JsonPropertyName("SupportsDirectStream")]
    public bool SupportsDirectStream { get; init; } = true;

    [JsonPropertyName("SupportsDirectPlay")]
    public bool SupportsDirectPlay { get; init; } = true;

    [JsonPropertyName("IsInfiniteStream")]
    public bool IsInfiniteStream { get; init; }

    [JsonPropertyName("UseMostCompatibleTranscodingProfile")]
    public bool UseMostCompatibleTranscodingProfile { get; init; }

    [JsonPropertyName("RequiresOpening")]
    public bool RequiresOpening { get; init; }

    [JsonPropertyName("RequiresClosing")]
    public bool RequiresClosing { get; init; }

    [JsonPropertyName("RequiresLooping")]
    public bool RequiresLooping { get; init; }

    [JsonPropertyName("SupportsProbing")]
    public bool SupportsProbing { get; init; } = true;

    [JsonPropertyName("DefaultAudioStreamIndex")]
    public int? DefaultAudioStreamIndex { get; init; }

    [JsonPropertyName("MediaStreams")]
    public IReadOnlyList<JellyfinCatalogMediaStreamDto> MediaStreams { get; init; } = [];

    [JsonPropertyName("MediaAttachments")]
    public IReadOnlyList<object> MediaAttachments { get; init; } = [];

    [JsonPropertyName("Formats")]
    public IReadOnlyList<object> Formats { get; init; } = [];

    [JsonPropertyName("RequiredHttpHeaders")]
    public IReadOnlyDictionary<string, string> RequiredHttpHeaders { get; init; } = new Dictionary<string, string>();

    [JsonPropertyName("HasSegments")]
    public bool HasSegments { get; init; }
}

/// <summary>Minimal Jellyfin-compatible media stream embedded in catalog item DTOs.</summary>
public sealed record JellyfinCatalogMediaStreamDto {
    [JsonPropertyName("Index")]
    public int Index { get; init; }

    [JsonPropertyName("Type")]
    public required string Type { get; init; }

    [JsonPropertyName("Codec")]
    public string? Codec { get; init; }

    [JsonPropertyName("Language")]
    public string? Language { get; init; }

    [JsonPropertyName("DisplayTitle")]
    public string? DisplayTitle { get; init; }

    [JsonPropertyName("Width")]
    public int? Width { get; init; }

    [JsonPropertyName("Height")]
    public int? Height { get; init; }

    [JsonPropertyName("AverageFrameRate")]
    public double? AverageFrameRate { get; init; }

    [JsonPropertyName("RealFrameRate")]
    public double? RealFrameRate { get; init; }

    [JsonPropertyName("BitRate")]
    public int? BitRate { get; init; }

    [JsonPropertyName("Channels")]
    public int? Channels { get; init; }

    [JsonPropertyName("ChannelLayout")]
    public string? ChannelLayout { get; init; }

    [JsonPropertyName("SampleRate")]
    public int? SampleRate { get; init; }

    [JsonPropertyName("AspectRatio")]
    public string? AspectRatio { get; init; }

    [JsonPropertyName("VideoRange")]
    public string? VideoRange { get; init; }

    [JsonPropertyName("VideoRangeType")]
    public string? VideoRangeType { get; init; }

    [JsonPropertyName("IsDefault")]
    public bool IsDefault { get; init; } = true;

    [JsonPropertyName("IsForced")]
    public bool IsForced { get; init; }

    [JsonPropertyName("IsExternal")]
    public bool IsExternal { get; init; }
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
    [property: JsonPropertyName("LastPlayedDate")] DateTimeOffset? LastPlayedDate = null);

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

/// <summary>Jellyfin-compatible image metadata.</summary>
public sealed record JellyfinImageInfo(
    [property: JsonPropertyName("ImageType")] string ImageType,
    [property: JsonPropertyName("ImageIndex")] int? ImageIndex,
    [property: JsonPropertyName("ImageTag")] string ImageTag);

/// <summary>Jellyfin-compatible user view grouping option.</summary>
public sealed record JellyfinSpecialViewOptionDto(
    [property: JsonPropertyName("Name")] string? Name,
    [property: JsonPropertyName("Id")] string? Id);

/// <summary>Jellyfin-compatible virtual folder information.</summary>
public sealed record JellyfinVirtualFolderInfoDto(
    [property: JsonPropertyName("Name")] string Name,
    [property: JsonPropertyName("Locations")] IReadOnlyList<string> Locations,
    [property: JsonPropertyName("CollectionType")] string CollectionType,
    [property: JsonPropertyName("LibraryOptions")] JellyfinLibraryOptionsDto LibraryOptions,
    [property: JsonPropertyName("ItemId")] string ItemId,
    [property: JsonPropertyName("PrimaryImageItemId")] string PrimaryImageItemId,
    [property: JsonPropertyName("RefreshProgress")] double? RefreshProgress,
    [property: JsonPropertyName("RefreshStatus")] string? RefreshStatus);

/// <summary>Minimal Jellyfin-compatible library options for virtual folders.</summary>
public sealed record JellyfinLibraryOptionsDto {
    [JsonPropertyName("Enabled")]
    public bool Enabled { get; init; } = true;

    [JsonPropertyName("EnablePhotos")]
    public bool EnablePhotos { get; init; }

    [JsonPropertyName("EnableRealtimeMonitor")]
    public bool EnableRealtimeMonitor { get; init; }

    [JsonPropertyName("PathInfos")]
    public IReadOnlyList<object> PathInfos { get; init; } = [];

    [JsonPropertyName("TypeOptions")]
    public IReadOnlyList<object> TypeOptions { get; init; } = [];
}

/// <summary>Minimal display preferences DTO accepted by Jellyfin clients.</summary>
public sealed record JellyfinDisplayPreferencesDto {
    [JsonPropertyName("Id")]
    public string? Id { get; init; }

    [JsonPropertyName("Client")]
    public string? Client { get; init; }

    [JsonPropertyName("SortBy")]
    public string? SortBy { get; init; }

    [JsonPropertyName("SortOrder")]
    public string? SortOrder { get; init; }

    [JsonPropertyName("RememberIndexing")]
    public bool RememberIndexing { get; init; } = true;

    [JsonPropertyName("RememberSorting")]
    public bool RememberSorting { get; init; } = true;

    [JsonPropertyName("ShowBackdrop")]
    public bool ShowBackdrop { get; init; } = true;

    [JsonPropertyName("ShowSidebar")]
    public bool ShowSidebar { get; init; } = true;

    [JsonPropertyName("CustomPrefs")]
    public IReadOnlyDictionary<string, string> CustomPrefs { get; init; } = new Dictionary<string, string>();
}
