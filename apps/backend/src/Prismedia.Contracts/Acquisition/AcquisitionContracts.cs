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

/// <summary>
/// Creates an acquisition and kicks off a background release search. Book metadata captured here is
/// later stamped onto the imported entity so identify resolves it ID-first.
/// </summary>
/// <param name="Title">Book title; primary search text.</param>
/// <param name="Author">Optional author, appended to the query and stored for the import hint.</param>
/// <param name="Series">Optional series name stored for the import hint.</param>
/// <param name="Year">Optional publication year stored for the import hint.</param>
/// <param name="PosterUrl">Optional cover URL stored for the import hint.</param>
/// <param name="PluginId">Optional plugin manifest id that supplied the metadata.</param>
/// <param name="PluginItemId">Optional plugin item id (external-id value) for ID-first identify.</param>
/// <param name="RequestHistoryId">Optional originating request-history entry to link.</param>
public sealed record AcquisitionCreateRequest(
    string Title,
    string? Author,
    string? Series,
    int? Year,
    string? PosterUrl,
    string? PluginId,
    string? PluginItemId,
    Guid? RequestHistoryId);

/// <summary>A scored release candidate surfaced for review. Download links stay server-side; the id selects one to queue.</summary>
public sealed record ReleaseCandidateView(
    Guid Id,
    string IndexerName,
    string Title,
    long SizeBytes,
    int? Seeders,
    int? Peers,
    DownloadProtocol Protocol,
    bool Accepted,
    double Score,
    IReadOnlyList<ReleaseRejectionReason> Rejections,
    string? InfoUrl,
    DateTimeOffset? PublishedAt);

/// <summary>An acquisition in list form: its state machine position and the metadata that identifies it.</summary>
public sealed record AcquisitionSummary(
    Guid Id,
    AcquisitionStatus Status,
    string? StatusMessage,
    string Title,
    string? Author,
    string? Series,
    int? Year,
    string? PosterUrl,
    double? Progress,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>An acquisition with its scored candidates for the review screen.</summary>
public sealed record AcquisitionDetail(
    AcquisitionSummary Summary,
    IReadOnlyList<ReleaseCandidateView> Candidates);
