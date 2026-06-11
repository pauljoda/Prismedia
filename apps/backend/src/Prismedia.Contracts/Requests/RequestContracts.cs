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
    bool SearchOnRequest,
    bool HasApiKey,
    string? ApiKey);

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
    bool SearchOnRequest,
    bool IsDefault);

/// <summary>Connection test result for a configured request service.</summary>
public sealed record RequestConnectionTestResponse(bool Connected, string? Message);

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
public sealed record RequestSearchResult(
    Guid ServiceId,
    RequestProviderKind Source,
    RequestMediaKind Kind,
    string ExternalId,
    string Title,
    int? Year,
    string? Overview,
    string? PosterUrl,
    string? BackdropUrl,
    decimal? Rating,
    int? RuntimeMinutes,
    string? Certification,
    IReadOnlyList<string> Tags,
    bool AlreadyAvailable,
    bool Requestable);

/// <summary>Normalized external detail record for a requestable item.</summary>
public sealed record RequestDetailResponse(
    RequestProviderKind Source,
    RequestMediaKind Kind,
    string ExternalId,
    string Title,
    int? Year,
    string? Overview,
    string? PosterUrl,
    string? BackdropUrl,
    decimal? Rating,
    int? RuntimeMinutes,
    string? Certification,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Studios,
    IReadOnlyList<string> Credits,
    IReadOnlyList<RequestChildOption> Children,
    IReadOnlyList<RequestServiceOption> ServiceOptions);

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
