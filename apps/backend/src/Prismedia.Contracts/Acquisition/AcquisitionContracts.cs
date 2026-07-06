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
    bool HasApiKey,
    int? QueryLimitPerHour = null,
    DateTimeOffset? DisabledUntil = null,
    string? LastFailureMessage = null,
    double? SeedRatio = null,
    int? SeedTimeMinutes = null);

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
    string? ApiKey,
    int? QueryLimitPerHour = null,
    double? SeedRatio = null,
    int? SeedTimeMinutes = null);

/// <summary>Request payload for creating or updating an indexer configuration.</summary>
public sealed record IndexerConfigSaveRequest(
    Guid? Id,
    IndexerKind Kind,
    string DisplayName,
    string BaseUrl,
    string? ApiKey,
    bool Enabled,
    int Priority,
    IReadOnlyList<int> Categories,
    int? QueryLimitPerHour = null,
    double? SeedRatio = null,
    int? SeedTimeMinutes = null);

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
    Guid? TargetLibraryRootId = null,
    int? SeasonNumber = null,
    int? EpisodeNumber = null);

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

/// <summary>
/// One row of the global Downloads view: an active (not yet imported, not cancelled) acquisition with
/// live download-client telemetry when a transfer is in flight. Telemetry fields are null for rows
/// without a live transfer (still searching, awaiting selection, importing) and when the download
/// client is unreachable; <see cref="Progress"/> then falls back to the last persisted progress.
/// </summary>
/// <param name="EntityId">The wanted library entity the acquisition targets; rows link to its detail page.</param>
/// <param name="TransferState">The client's raw transfer state label, normalized casing left to the UI.</param>
/// <param name="ClientName">Display name of the download client carrying the transfer.</param>
/// <param name="Author">Creator context (author/artist), for the row's subtitle line.</param>
/// <param name="Series">Series name (for TV seasons/episodes), for the row's subtitle line.</param>
/// <param name="Year">Release year, appended to the subtitle when present.</param>
public sealed record DownloadQueueItemView(
    Guid AcquisitionId,
    EntityKind Kind,
    string Title,
    AcquisitionStatus Status,
    string? StatusMessage,
    double? Progress,
    DateTimeOffset UpdatedAt,
    Guid? EntityId = null,
    string? PosterUrl = null,
    string? TransferState = null,
    long? TotalSizeBytes = null,
    double? DownloadSpeedBytesPerSecond = null,
    long? EtaSeconds = null,
    int? Seeds = null,
    int? Peers = null,
    string? ClientName = null,
    string? Author = null,
    string? Series = null,
    int? Year = null);

/// <summary>Configured download client safe for list displays (no secret material).</summary>
public sealed record DownloadClientSummary(
    Guid Id,
    DownloadClientKind Kind,
    string DisplayName,
    string BaseUrl,
    string? Username,
    string Category,
    bool Enabled,
    bool HasPassword,
    bool HasApiKey = false,
    int Priority = 25,
    double? SeedRatio = null,
    int? SeedTimeMinutes = null);

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
    string? Password,
    string? ApiKey = null,
    int Priority = 25,
    double? SeedRatio = null,
    int? SeedTimeMinutes = null);

/// <summary>Request payload for creating or updating a download client configuration.</summary>
/// <param name="ApiKey">API key for clients that authenticate with one (SABnzbd); blank keeps the stored key.</param>
public sealed record DownloadClientSaveRequest(
    Guid? Id,
    DownloadClientKind Kind,
    string DisplayName,
    string BaseUrl,
    string? Username,
    string? Password,
    string Category,
    bool Enabled,
    string? ApiKey = null,
    int Priority = 25,
    double? SeedRatio = null,
    int? SeedTimeMinutes = null);

/// <summary>Connection test payload for a download client configuration that may not be saved yet.</summary>
public sealed record DownloadClientTestRequest(
    Guid? Id,
    DownloadClientKind Kind,
    string BaseUrl,
    string? Username,
    string? Password,
    string? ApiKey = null);

/// <summary>Connection test result for a download client configuration.</summary>
public sealed record DownloadClientTestResponse(bool Connected, string? Message);

/// <summary>A path-prefix rewrite from a download client's filesystem view to Prismedia's.</summary>
public sealed record RemotePathMappingView(
    Guid Id,
    Guid DownloadClientConfigId,
    string RemotePath,
    string LocalPath);

/// <summary>Request payload for creating or updating a remote path mapping.</summary>
public sealed record RemotePathMappingSaveRequest(
    Guid? Id,
    Guid DownloadClientConfigId,
    string RemotePath,
    string LocalPath);

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
    BookFormatTier CutoffFormatTier,
    string? DownloadCategory = null,
    IReadOnlyList<string>? AllowedQualities = null,
    string? CutoffQuality = null,
    IReadOnlyDictionary<string, int>? FormatScores = null,
    int MinFormatScore = 0,
    int? CutoffFormatScore = null);

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
    BookFormatTier CutoffFormatTier,
    string? DownloadCategory = null,
    IReadOnlyList<string>? AllowedQualities = null,
    string? CutoffQuality = null,
    IReadOnlyDictionary<string, int>? FormatScores = null,
    int MinFormatScore = 0,
    int? CutoffFormatScore = null);

/// <summary>One condition of a custom format for the API surface (see the application <c>CustomFormatCondition</c>).</summary>
/// <param name="Type">The release axis this condition tests.</param>
/// <param name="Value">The pattern/name/code the axis is tested against (a regex, a language name, or a quality code).</param>
/// <param name="Negate">When true, the condition matches when its underlying test does NOT.</param>
/// <param name="Required">When true, this condition must match for the format to match.</param>
public sealed record CustomFormatConditionView(CustomFormatConditionType Type, string Value, bool Negate, bool Required);

/// <summary>A named, scored release classifier (custom format), scoped to one profile kind.</summary>
public sealed record CustomFormatView(
    Guid Id,
    EntityKind Kind,
    string Name,
    IReadOnlyList<CustomFormatConditionView> Conditions);

/// <summary>Request payload for creating or updating a custom format (upsert; id null creates).</summary>
public sealed record CustomFormatSaveRequest(
    Guid? Id,
    EntityKind Kind,
    string Name,
    IReadOnlyList<CustomFormatConditionView> Conditions);

/// <summary>A monitored wanted item: the standing intent plus the current state of the acquisition it keeps alive.</summary>
/// <param name="Preset">
/// The monitoring preset on a container monitor (an author/artist/series watch): it governs whether the
/// discovery sync auto-monitors newly discovered works. Meaningful only for container monitors
/// (<see cref="EntityId"/> set); per-item monitors carry the default <see cref="MonitorPreset.All"/>.
/// </param>
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
    Guid? EntityId = null,
    MonitorPreset Preset = MonitorPreset.All);

/// <summary>
/// One row of a Wanted list (Missing or Cutoff Unmet): a monitored item that is not yet in hand, or is in
/// hand but below its kind's cutoff. Carries the monitor/acquisition identity the row's bulk Search-now and
/// Unmonitor actions target, the entity link (when set, the row links to its library detail page), both
/// statuses, the search cadence, and the owned → cutoff quality strings the cutoff-unmet view renders.
/// </summary>
/// <param name="MonitorId">The monitor backing this row; the target of Search-now / Unmonitor.</param>
/// <param name="AcquisitionId">The acquisition the monitor keeps alive, or null when it was removed.</param>
/// <param name="EntityId">The library entity this item resolves to, or null; when set the row links to its detail page.</param>
/// <param name="Kind">The media kind, for the kind badge and the kind filter.</param>
/// <param name="Title">The wanted item's title.</param>
/// <param name="MonitorStatus">The monitor's current status.</param>
/// <param name="AcquisitionStatus">The linked acquisition's status, or null when it is gone.</param>
/// <param name="LastSearchedAt">When the item was last re-searched; null means never.</param>
/// <param name="NextSearchAt">When the item is next due for a re-search (last-searched plus the sweep's exponential backoff); null when never searched.</param>
/// <param name="OwnedQuality">The owned quality string in the kind's vocabulary (a book's "source/format" tiers, or a media ladder code); null on the missing list.</param>
/// <param name="CutoffQuality">The kind's cutoff quality, same vocabulary as <see cref="OwnedQuality"/>; null on the missing list.</param>
/// <param name="BarrenSearches">Consecutive fruitless searches so far, surfaced so a stuck item is visible.</param>
/// <param name="PosterUrl">Cover art for the row's thumbnail (the acquisition's captured poster), or null when none was captured.</param>
public sealed record WantedListItemView(
    Guid MonitorId,
    Guid? AcquisitionId,
    Guid? EntityId,
    EntityKind Kind,
    string Title,
    MonitorStatus MonitorStatus,
    AcquisitionStatus? AcquisitionStatus,
    DateTimeOffset? LastSearchedAt,
    DateTimeOffset? NextSearchAt,
    string? OwnedQuality,
    string? CutoffQuality,
    int BarrenSearches,
    string? PosterUrl = null,
    string? Author = null);

/// <summary>
/// One page of a Wanted list: the page's rows plus the total count of matching rows for the pagination
/// controls. For Missing, <see cref="Total"/> is exact; for Cutoff Unmet it is an upper bound (the count of
/// imported+active monitors, before the per-page cutoff refinement) — see the endpoint summary.
/// </summary>
public sealed record WantedPageView(IReadOnlyList<WantedListItemView> Items, int Total);

/// <summary>
/// Whether a library entity can carry a standing container monitor. Monitoring rides on plugin
/// trackability: the entity must be a monitorable container kind AND hold a provider identity some
/// enabled metadata plugin can re-resolve by id (the lookup-id action) — otherwise the daily discovery
/// sweep could never notice new works. <see cref="TrackableProviders"/> lists the provider ids the
/// watch would ride on, so the UI can name them.
/// </summary>
public sealed record MonitorEligibilityView(bool CanMonitor, IReadOnlyList<string> TrackableProviders);

/// <summary>Request to start monitoring (keep re-searching) an existing acquisition until it is acquired.</summary>
public sealed record MonitorCreateRequest(Guid AcquisitionId);

/// <summary>
/// Request to monitor a library container entity (an author, an artist, a series) for new works: the
/// daily sweep re-resolves the container from its provider and requests any works the library doesn't
/// have yet, which appear as clearly-badged Wanted placeholders under the container.
/// </summary>
/// <param name="Preset">
/// The monitoring preset governing whether future syncs auto-monitor newly discovered works. Null leaves
/// any preset a prior request recorded untouched (and defaults to <see cref="MonitorPreset.All"/> for a
/// fresh container), so a plain monitor toggle never narrows the discovery scope.
/// </param>
public sealed record EntityMonitorCreateRequest(Guid EntityId, MonitorPreset? Preset = null);

/// <summary>
/// One entry in the durable acquisition activity log, surfaced newest-first for the history surface.
/// Outlives its acquisition: <see cref="AcquisitionId"/> is null once the acquisition has been removed,
/// but the denormalized title/kind/entity and release context keep the entry meaningful.
/// </summary>
public sealed record AcquisitionHistoryView(
    Guid Id,
    Guid? AcquisitionId,
    Guid? EntityId,
    EntityKind Kind,
    AcquisitionHistoryEvent Event,
    string Title,
    string? ReleaseTitle,
    string? IndexerName,
    string? DownloadClientName,
    string? QualityCode,
    int? FormatScore,
    string? Message,
    DateTimeOffset CreatedAt);

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
