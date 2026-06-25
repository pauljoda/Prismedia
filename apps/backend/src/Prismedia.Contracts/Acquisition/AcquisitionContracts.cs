using Prismedia.Domain.Entities;

namespace Prismedia.Contracts.Acquisition;

/// <summary>Configured indexer aggregator safe for list displays (no secret material).</summary>
public sealed record IndexerConfigSummary(
    Guid Id,
    IndexerKind Kind,
    string DisplayName,
    string BaseUrl,
    bool Enabled,
    int Priority,
    IReadOnlyList<int> Categories,
    bool HasApiKey);

/// <summary>Configured indexer aggregator with secret material for server-side use only.</summary>
public sealed record IndexerConfigDetail(
    Guid Id,
    IndexerKind Kind,
    string DisplayName,
    string BaseUrl,
    bool Enabled,
    int Priority,
    IReadOnlyList<int> Categories,
    bool HasApiKey,
    string? ApiKey);

/// <summary>Request payload for creating or updating an indexer configuration.</summary>
public sealed record IndexerConfigSaveRequest(
    Guid? Id,
    IndexerKind Kind,
    string DisplayName,
    string BaseUrl,
    string? ApiKey,
    bool Enabled,
    int Priority,
    IReadOnlyList<int> Categories);

/// <summary>Connection test payload for an indexer configuration that may not be saved yet.</summary>
/// <param name="Id">Existing config id when editing; lets the server reuse the stored API key if none is supplied.</param>
public sealed record IndexerTestRequest(
    Guid? Id,
    IndexerKind Kind,
    string BaseUrl,
    string? ApiKey);

/// <summary>Connection test result for an indexer configuration.</summary>
public sealed record IndexerTestResponse(bool Connected, string? Message);

/// <summary>Ad-hoc release search for verification and the request-driven acquisition flow.</summary>
/// <param name="Title">Primary search text (book title).</param>
/// <param name="Author">Optional author, appended to the query when present.</param>
public sealed record AcquisitionSearchRequest(string Title, string? Author);

/// <summary>A scored release candidate surfaced for review. Rejected candidates carry their reasons rather than being hidden.</summary>
public sealed record ReleaseCandidateView(
    string IndexerName,
    string Title,
    long SizeBytes,
    int? Seeders,
    int? Peers,
    DownloadProtocol Protocol,
    bool Accepted,
    double Score,
    IReadOnlyList<ReleaseRejectionReason> Rejections,
    string? MagnetUrl,
    string? DownloadUrl,
    string? InfoUrl,
    DateTimeOffset? PublishedAt);

/// <summary>An indexer that failed during a search, surfaced so partial results are transparent.</summary>
public sealed record IndexerSearchError(Guid IndexerId, string IndexerName, string Message);

/// <summary>Scored candidates plus any indexer errors for a release search.</summary>
public sealed record AcquisitionSearchResponse(
    IReadOnlyList<ReleaseCandidateView> Candidates,
    IReadOnlyList<IndexerSearchError> Errors);
