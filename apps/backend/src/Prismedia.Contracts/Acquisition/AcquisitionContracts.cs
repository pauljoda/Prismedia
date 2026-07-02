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
/// <param name="Kind">Media kind to acquire (book, movie, …). Defaults to book for the established book flow.</param>
/// <param name="EntityId">Optional wanted library entity this acquisition fulfils; the import attaches its file to this entity.</param>
public sealed record AcquisitionCreateRequest(
    string Title,
    string? Author,
    string? Series,
    int? Year,
    string? PosterUrl,
    string? PluginId,
    string? PluginItemId,
    string? Description = null,
    EntityKind Kind = EntityKind.Book,
    Guid? EntityId = null,
    Guid? ProfileId = null,
    Guid? TargetLibraryRootId = null);

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
    DateTimeOffset UpdatedAt,
    string? Description = null,
    EntityKind Kind = EntityKind.Book,
    Guid? EntityId = null);

/// <summary>An acquisition with its scored candidates for the review screen.</summary>
public sealed record AcquisitionDetail(
    AcquisitionSummary Summary,
    IReadOnlyList<ReleaseCandidateView> Candidates);

/// <summary>Selects which scored candidate to download for an acquisition.</summary>
public sealed record AcquisitionQueueRequest(Guid CandidateId);

/// <summary>Live transfer telemetry for an in-flight acquisition, including per-piece state for a progress map.</summary>
public sealed record AcquisitionTransferView(
    double Progress,
    string? State,
    long TotalSizeBytes,
    double DownloadSpeedBytesPerSecond,
    long EtaSeconds,
    int Seeds,
    int Peers,
    string? SavePath,
    IReadOnlyList<int> PieceStates);

/// <summary>One file belonging to an acquisition (download client file while transferring, or imported library file).</summary>
public sealed record AcquisitionFileItem(string Name, long SizeBytes, double Progress);

/// <summary>The files of an acquisition; <see cref="Imported"/> distinguishes library files from in-progress download files.</summary>
public sealed record AcquisitionFilesView(bool Imported, IReadOnlyList<AcquisitionFileItem> Files);

/// <summary>Configured download client safe for list displays (no secret material).</summary>
public sealed record DownloadClientSummary(
    Guid Id,
    DownloadClientKind Kind,
    string DisplayName,
    string BaseUrl,
    string? Username,
    string Category,
    bool Enabled,
    bool HasPassword);

/// <summary>Configured download client with secret material for server-side use only.</summary>
public sealed record DownloadClientDetail(
    Guid Id,
    DownloadClientKind Kind,
    string DisplayName,
    string BaseUrl,
    string? Username,
    string Category,
    bool Enabled,
    bool HasPassword,
    string? Password);

/// <summary>Request payload for creating or updating a download client configuration.</summary>
public sealed record DownloadClientSaveRequest(
    Guid? Id,
    DownloadClientKind Kind,
    string DisplayName,
    string BaseUrl,
    string? Username,
    string? Password,
    string Category,
    bool Enabled);

/// <summary>Connection test payload for a download client configuration that may not be saved yet.</summary>
public sealed record DownloadClientTestRequest(
    Guid? Id,
    DownloadClientKind Kind,
    string BaseUrl,
    string? Username,
    string? Password);

/// <summary>Connection test result for a download client configuration.</summary>
public sealed record DownloadClientTestResponse(bool Connected, string? Message);

/// <summary>
/// A custom scoring rule: when <paramref name="Term"/> appears in a release title, <paramref name="Weight"/>
/// is added to the release's ranking score. Positive weights pull a release up, negative push it down;
/// a weight of 100 counts as much as one preferred-term match.
/// </summary>
public sealed record WeightedTerm(string Term, int Weight);

/// <summary>
/// An acquisition profile: matching rules plus where and how completed downloads are imported. Profiles
/// are scoped to one media kind (book, movie, or album) and IsDefault is per kind; the book-specific
/// fields (formats, path template, quality cutoffs) are ignored for other kinds.
/// </summary>
public sealed record BookAcquisitionProfileView(
    Guid Id,
    EntityKind Kind,
    string DisplayName,
    bool IsDefault,
    Guid TargetLibraryRootId,
    string PathTemplate,
    ImportMode ImportMode,
    IReadOnlyList<BookFormat> AllowedFormats,
    IReadOnlyList<string> PreferredLanguages,
    int MinSeeders,
    long? MinSizeBytes,
    long? MaxSizeBytes,
    IReadOnlyList<string> RequiredTerms,
    IReadOnlyList<string> IgnoredTerms,
    IReadOnlyList<string> PreferredTerms,
    IReadOnlyList<WeightedTerm> WeightedTerms,
    bool AutoPick,
    bool AutoRedownload,
    bool UpgradeUntilCutoff,
    BookSourceTier CutoffSourceTier,
    BookFormatTier CutoffFormatTier);

/// <summary>Request payload for creating or updating an acquisition profile.</summary>
public sealed record BookAcquisitionProfileSaveRequest(
    Guid? Id,
    string DisplayName,
    bool IsDefault,
    EntityKind Kind,
    Guid TargetLibraryRootId,
    string PathTemplate,
    ImportMode ImportMode,
    IReadOnlyList<BookFormat> AllowedFormats,
    IReadOnlyList<string> PreferredLanguages,
    int MinSeeders,
    long? MinSizeBytes,
    long? MaxSizeBytes,
    IReadOnlyList<string> RequiredTerms,
    IReadOnlyList<string> IgnoredTerms,
    IReadOnlyList<string> PreferredTerms,
    IReadOnlyList<WeightedTerm> WeightedTerms,
    bool AutoPick,
    bool AutoRedownload,
    bool UpgradeUntilCutoff,
    BookSourceTier CutoffSourceTier,
    BookFormatTier CutoffFormatTier);

/// <summary>A monitored wanted item: the standing intent plus the current state of the acquisition it keeps alive.</summary>
public sealed record MonitorView(
    Guid Id,
    EntityKind Kind,
    Guid? AcquisitionId,
    MonitorStatus Status,
    string Title,
    string? Author,
    AcquisitionStatus? AcquisitionStatus,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    Guid? EntityId = null);

/// <summary>Request to start monitoring (keep re-searching) an existing acquisition until it is acquired.</summary>
public sealed record MonitorCreateRequest(Guid AcquisitionId);

/// <summary>
/// Request to monitor a library container entity (an author, an artist) for new works: the daily sweep
/// re-resolves the container from its provider and requests any works the library doesn't have yet,
/// which appear as clearly-badged Wanted placeholders under the container.
/// </summary>
public sealed record EntityMonitorCreateRequest(Guid EntityId);

/// <summary>A blocklisted release identity, surfaced for the blocklist management surface.</summary>
public sealed record AcquisitionBlocklistEntry(
    Guid Id,
    BlocklistReason Reason,
    string? Title,
    string? IndexerName,
    string? InfoHash,
    Guid? AcquisitionId,
    string? Message,
    DateTimeOffset CreatedAt);
