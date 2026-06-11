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
/// <param name="HideNsfw">When true, adults-only results (NC-17/X style certifications) are filtered out.</param>
public sealed record RequestSearchRequest(
    string Query,
    IReadOnlyList<RequestMediaKind> Kinds,
    IReadOnlyList<RequestProviderKind> Sources,
    bool HideNsfw);

/// <summary>Aggregated request search response.</summary>
public sealed record RequestSearchResponse(
    IReadOnlyList<RequestSearchResult> Results,
    IReadOnlyList<RequestProviderHealth> ProviderErrors);

/// <summary>Provider health warning captured while aggregating search results.</summary>
public sealed record RequestProviderHealth(Guid ServiceId, RequestProviderKind Kind, string DisplayName, string Message);

/// <summary>Normalized external search result.</summary>
/// <param name="Subtitle">Short secondary line for review context: the artist for albums, disambiguation for artists, studio for movies, network for series.</param>
/// <param name="TrackCount">Track count for albums; null for other kinds.</param>
/// <param name="Tracked">True when the item already exists in the upstream service's library.</param>
/// <param name="UpstreamId">The upstream library id when <paramref name="Tracked"/> is true.</param>
/// <param name="Monitored">Upstream monitored flag when tracked; null otherwise.</param>
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
    bool Tracked,
    string? UpstreamId,
    bool? Monitored,
    bool Requestable);

/// <summary>Normalized external detail record for a requestable item.</summary>
/// <param name="Subtitle">Short secondary line for review context: the artist for albums, disambiguation for artists, studio for movies, network for series.</param>
/// <param name="TrackCount">Track count for albums; null for other kinds.</param>
/// <param name="Tracked">True when the item already exists in the upstream service's library.</param>
/// <param name="UpstreamId">The upstream library id when <paramref name="Tracked"/> is true.</param>
/// <param name="Monitored">Upstream monitored flag when tracked; null otherwise.</param>
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
    IReadOnlyList<RequestTrack> Tracks,
    bool Tracked,
    string? UpstreamId,
    bool? Monitored,
    RequestServiceOptionsResponse ServiceOptions);

/// <summary>Selectable or informational child option, such as a season or album.</summary>
/// <param name="Number">Ordering number where the provider has one (season number).</param>
/// <param name="Year">Release year for albums; null elsewhere.</param>
/// <param name="ItemCount">Episode count for seasons; null elsewhere.</param>
/// <param name="Monitored">Upstream monitored flag for this child when the parent is tracked; null otherwise.</param>
public sealed record RequestChildOption(
    string Id,
    string Title,
    RequestMediaKind Kind,
    bool Requestable,
    int? Number,
    int? Year,
    int? ItemCount,
    string? Overview,
    string? PosterUrl,
    bool? Monitored);

/// <summary>One track on an album detail, for review before requesting.</summary>
public sealed record RequestTrack(int Number, string Title, int? DurationSeconds);

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

/// <summary>A previously submitted request with its last known upstream status.</summary>
/// <param name="ServiceId">Configured service instance id; null when the service has since been deleted.</param>
/// <param name="ServiceName">Display name of the service at submit time.</param>
/// <param name="UpstreamId">Library id assigned by the upstream service, when known.</param>
/// <param name="SelectedChildCount">Number of seasons/albums explicitly selected on submit.</param>
/// <param name="StatusUpdatedAt">When the status was last refreshed from the upstream service.</param>
public sealed record RequestHistoryEntry(
    Guid Id,
    Guid? ServiceId,
    string ServiceName,
    RequestProviderKind Source,
    RequestMediaKind Kind,
    string ExternalId,
    string Title,
    string? Subtitle,
    int? Year,
    string? PosterUrl,
    string? UpstreamId,
    bool Monitored,
    int SelectedChildCount,
    RequestHistoryStatus Status,
    string? StatusMessage,
    DateTimeOffset RequestedAt,
    DateTimeOffset StatusUpdatedAt);

/// <summary>Request history list with provider warnings captured during the live status refresh.</summary>
public sealed record RequestHistoryResponse(
    IReadOnlyList<RequestHistoryEntry> Entries,
    IReadOnlyList<RequestProviderHealth> ProviderErrors);
