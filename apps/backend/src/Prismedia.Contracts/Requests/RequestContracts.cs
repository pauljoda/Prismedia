using Prismedia.Domain.Entities;

namespace Prismedia.Contracts.Requests;

/// <summary>Configured Arr or plugin service instance safe for list displays.</summary>
public sealed record RequestServiceInstanceSummary(
    Guid Id,
    RequestProviderKind Kind,
    string DisplayName,
    string BaseUrl,
    bool IsDefault,
    string? DefaultRootFolderPath,
    int? DefaultQualityProfileId,
    int? DefaultMetadataProfileId,
    RequestMinimumAvailability MinimumAvailability,
    IReadOnlyList<int> DefaultTagIds,
    bool SearchOnRequest,
    bool HasApiKey);

/// <summary>Configured Arr or plugin service instance with secret material for server-side use only.</summary>
public sealed record RequestServiceInstanceDetail(
    Guid Id,
    RequestProviderKind Kind,
    string DisplayName,
    string BaseUrl,
    bool IsDefault,
    string? DefaultRootFolderPath,
    int? DefaultQualityProfileId,
    int? DefaultMetadataProfileId,
    RequestMinimumAvailability MinimumAvailability,
    IReadOnlyList<int> DefaultTagIds,
    bool SearchOnRequest,
    bool HasApiKey,
    string? ApiKey);

/// <summary>Request payload for creating or updating a request service instance.</summary>
public sealed record RequestServiceInstanceSaveRequest(
    Guid? Id,
    RequestProviderKind Kind,
    string DisplayName,
    string BaseUrl,
    string? ApiKey,
    string? DefaultRootFolderPath,
    int? DefaultQualityProfileId,
    int? DefaultMetadataProfileId,
    RequestMinimumAvailability MinimumAvailability,
    IReadOnlyList<int> DefaultTagIds,
    bool SearchOnRequest,
    bool IsDefault);

/// <summary>Connection test payload for a request service configuration that may not be saved yet.</summary>
/// <param name="Id">Existing instance id when editing; lets the server reuse the stored API key if none is supplied.</param>
/// <param name="Kind">Provider family to test.</param>
/// <param name="BaseUrl">Service base URL to test against.</param>
/// <param name="ApiKey">API key to test with; null/empty reuses the stored key for <paramref name="Id"/>.</param>
public sealed record RequestServiceTestRequest(
    Guid? Id,
    RequestProviderKind Kind,
    string BaseUrl,
    string? ApiKey);

/// <summary>Connection test result for a configured request service.</summary>
public sealed record RequestConnectionTestResponse(bool Connected, string? Message);

/// <summary>
/// Connection test result with the selectable options pulled from the service on success.
/// A successful test gates saving: the returned options seed the defaults pickers.
/// </summary>
public sealed record RequestServiceTestResponse(
    bool Connected,
    string? Message,
    RequestServiceOptionsResponse? Options);

/// <summary>Search query for requestable external media.</summary>
public sealed record RequestSearchRequest(
    string Query,
    IReadOnlyList<RequestMediaKind> Kinds,
    IReadOnlyList<RequestProviderKind> Sources);

/// <summary>Aggregated request search response.</summary>
public sealed record RequestSearchResponse(
    IReadOnlyList<RequestSearchResult> Results,
    IReadOnlyList<RequestProviderHealth> ProviderErrors);

/// <summary>Provider health warning captured while aggregating search results.</summary>
public sealed record RequestProviderHealth(Guid ServiceId, RequestProviderKind Kind, string DisplayName, string Message);

/// <summary>Normalized external search result.</summary>
/// <param name="Subtitle">Short secondary line for review context: the artist for albums, disambiguation for artists, studio for movies, network for series.</param>
/// <param name="TrackCount">Track count for albums; null for other kinds.</param>
public sealed record RequestSearchResult(
    Guid ServiceId,
    RequestProviderKind Source,
    RequestMediaKind Kind,
    string ExternalId,
    string Title,
    string? Subtitle,
    int? Year,
    string? Overview,
    string? PosterUrl,
    string? BackdropUrl,
    decimal? Rating,
    int? RuntimeMinutes,
    string? Certification,
    int? TrackCount,
    IReadOnlyList<string> Tags,
    bool AlreadyAvailable,
    bool Requestable);

/// <summary>Normalized external detail record for a requestable item.</summary>
/// <param name="Subtitle">Short secondary line for review context: the artist for albums, disambiguation for artists, studio for movies, network for series.</param>
/// <param name="TrackCount">Track count for albums; null for other kinds.</param>
public sealed record RequestDetailResponse(
    RequestProviderKind Source,
    RequestMediaKind Kind,
    string ExternalId,
    string Title,
    string? Subtitle,
    int? Year,
    string? Overview,
    string? PosterUrl,
    string? BackdropUrl,
    decimal? Rating,
    int? RuntimeMinutes,
    string? Certification,
    int? TrackCount,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Studios,
    IReadOnlyList<string> Credits,
    IReadOnlyList<RequestChildOption> Children,
    RequestServiceOptionsResponse ServiceOptions);

/// <summary>Selectable child option, such as a season or album.</summary>
public sealed record RequestChildOption(
    string Id,
    string Title,
    RequestMediaKind Kind,
    bool Requestable,
    int? Number,
    string? Overview,
    string? PosterUrl);

/// <summary>Root folder/profile option exposed by a request service instance.</summary>
public sealed record RequestServiceOption(string Id, string Name, string? Path);

/// <summary>Grouped selectable options exposed by a request service instance.</summary>
public sealed record RequestServiceOptionsResponse(
    IReadOnlyList<RequestServiceOption> QualityProfiles,
    IReadOnlyList<RequestServiceOption> RootFolders,
    IReadOnlyList<RequestServiceOption> MetadataProfiles,
    IReadOnlyList<RequestServiceOption> Tags);

/// <summary>Request payload for submitting a request to a selected service instance.</summary>
public sealed record RequestSubmitRequest(
    Guid ServiceId,
    RequestProviderKind Source,
    RequestMediaKind Kind,
    string ExternalId,
    string Title,
    int? QualityProfileId,
    string? RootFolderPath,
    int? MetadataProfileId,
    bool Monitored,
    bool SearchNow,
    IReadOnlyList<string> SelectedChildIds);

/// <summary>Response returned after a request is accepted by an upstream service.</summary>
public sealed record RequestSubmitResponse(bool Submitted, string? UpstreamId, string? Message);
