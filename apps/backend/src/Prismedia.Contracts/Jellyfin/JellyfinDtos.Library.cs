using System.Text.Json.Serialization;

namespace Prismedia.Contracts.Jellyfin;

/// <summary>Jellyfin-compatible image metadata.</summary>
public sealed record JellyfinImageInfo(
    [property: JsonPropertyName("ImageType")] string ImageType,
    [property: JsonPropertyName("ImageIndex")] int? ImageIndex,
    [property: JsonPropertyName("ImageTag")] string ImageTag);

/// <summary>Jellyfin-compatible user view grouping option.</summary>
public sealed record JellyfinSpecialViewOptionDto(
    [property: JsonPropertyName("Name")] string? Name,
    [property: JsonPropertyName("Id")] string? Id);

/// <summary>Jellyfin-compatible legacy item filter response.</summary>
public sealed record JellyfinQueryFiltersLegacyDto(
    [property: JsonPropertyName("Genres")] IReadOnlyList<string> Genres,
    [property: JsonPropertyName("Tags")] IReadOnlyList<string> Tags,
    [property: JsonPropertyName("OfficialRatings")] IReadOnlyList<string> OfficialRatings,
    [property: JsonPropertyName("Years")] IReadOnlyList<int> Years);

/// <summary>Jellyfin-compatible modern item filter response.</summary>
public sealed record JellyfinQueryFiltersDto(
    [property: JsonPropertyName("Genres")] IReadOnlyList<JellyfinNameGuidPairDto> Genres,
    [property: JsonPropertyName("Tags")] IReadOnlyList<string> Tags,
    [property: JsonPropertyName("AudioLanguages")] IReadOnlyList<JellyfinNameValuePairDto> AudioLanguages,
    [property: JsonPropertyName("SubtitleLanguages")] IReadOnlyList<JellyfinNameValuePairDto> SubtitleLanguages);

/// <summary>Jellyfin-compatible name/value pair used by filter metadata.</summary>
public sealed record JellyfinNameValuePairDto(
    [property: JsonPropertyName("Name")] string Name,
    [property: JsonPropertyName("Value")] string Value);

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
