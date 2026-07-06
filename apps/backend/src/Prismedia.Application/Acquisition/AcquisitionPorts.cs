using Prismedia.Contracts.Acquisition;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Acquisition;

/// <summary>Validation failure raised by acquisition configuration use cases.</summary>
public sealed class AcquisitionConfigurationException(string code, string message) : Exception(message) {
    /// <summary>Machine-readable problem code (see <see cref="Contracts.System.ApiProblemCodes"/>).</summary>
    public string Code { get; } = code;
}

/// <summary>Searches an indexer aggregator for releases. Prowlarr is the v1 implementation; the shape is Torznab-compatible.</summary>
public interface IIndexerSearchClient {
    /// <summary>The indexer family this client serves.</summary>
    IndexerKind Kind { get; }

    /// <summary>Runs a release search against the connection, returning normalized releases.</summary>
    Task<IReadOnlyList<IndexerRelease>> SearchAsync(IndexerConnection connection, IndexerQuery query, CancellationToken cancellationToken);

    /// <summary>Probes the connection for reachability and authentication.</summary>
    Task<IndexerConnectionTest> TestAsync(IndexerConnection connection, CancellationToken cancellationToken);
}

/// <summary>Resolves the search client for an indexer family.</summary>
public interface IIndexerSearchClientFactory {
    IIndexerSearchClient Get(IndexerKind kind);
}

/// <summary>Command for creating or updating an indexer configuration.</summary>
public sealed record IndexerConfigSaveCommand(
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

/// <summary>Persistence port for configured indexers.</summary>
public interface IIndexerConfigStore {
    Task<IReadOnlyList<IndexerConfigSummary>> ListAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<IndexerConfigDetail>> ListDetailsAsync(CancellationToken cancellationToken);
    Task<IndexerConfigDetail?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<IndexerConfigSummary> SaveAsync(IndexerConfigSaveCommand command, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);
}

/// <summary>The import target a profile contributes: which library root, how to place files, and the path template.</summary>
public sealed record BookImportProfile(Guid TargetLibraryRootId, string PathTemplate, ImportMode ImportMode);

/// <summary>Command for creating or updating an acquisition profile (kind-scoped; see the contract record).</summary>
public sealed record BookAcquisitionProfileSaveCommand(
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

/// <summary>
/// Persistence port for custom formats (named, scored release classifiers), scoped per profile kind.
/// A profile references formats of its own kind by id and assigns each a per-profile score; the profile
/// store resolves those scores against this table when it builds the decision rules.
/// </summary>
public interface ICustomFormatStore {
    /// <summary>Every custom format, kind then name ordered, for the settings surface.</summary>
    Task<IReadOnlyList<Contracts.Acquisition.CustomFormatView>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Creates or updates a custom format (upsert on id). The store validates the conditions before persisting.</summary>
    Task<Contracts.Acquisition.CustomFormatView> SaveAsync(Contracts.Acquisition.CustomFormatSaveRequest request, CancellationToken cancellationToken);

    /// <summary>Deletes a custom format by id. Returns false when it no longer exists.</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);
}

/// <summary>
/// Persistence port for acquisition profiles (matching rules + import target), scoped per media kind.
/// Every resolution method takes an optional explicit profile id (a request-time choice): when it names
/// an existing profile of the right kind that profile wins, otherwise resolution falls back to the
/// kind's default profile, then to permissive defaults — a stale or wrong-kind choice degrades, never
/// fails.
/// </summary>
public interface IBookAcquisitionProfileStore {
    /// <summary>The decision rules for a search: the chosen profile, else the kind's default, else <see cref="BookAcquisitionRules.Default"/>.</summary>
    Task<BookAcquisitionRules> GetRulesAsync(Guid? profileId, EntityKind kind, CancellationToken cancellationToken);

    /// <summary>The import target: the chosen profile's, else the kind's default profile's, or null when the kind has no profile.</summary>
    Task<BookImportProfile?> GetImportProfileAsync(Guid? profileId, EntityKind kind, CancellationToken cancellationToken);

    /// <summary>True when the resolved profile auto-queues the top accepted release without manual review.</summary>
    Task<bool> GetAutoPickAsync(Guid? profileId, EntityKind kind, CancellationToken cancellationToken);

    /// <summary>True when the resolved profile auto-blocklists a failed download and grabs the next-best candidate.</summary>
    Task<bool> GetAutoRedownloadAsync(Guid? profileId, EntityKind kind, CancellationToken cancellationToken);

    /// <summary>The resolved profile's download-client category override, or null to use the client's own category.</summary>
    Task<string?> GetDownloadCategoryAsync(Guid? profileId, EntityKind kind, CancellationToken cancellationToken);

    Task<IReadOnlyList<BookAcquisitionProfileView>> ListAsync(CancellationToken cancellationToken);
    Task<BookAcquisitionProfileView?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<BookAcquisitionProfileView> SaveAsync(BookAcquisitionProfileSaveCommand command, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);
}

/// <summary>
/// Sends a chosen release candidate to the download client. Extracted so failed-download recovery can
/// re-grab the next-best candidate without depending on the full queue service implementation.
/// </summary>
public interface IAcquisitionQueueService {
    /// <summary>Queues the given candidate of an acquisition for download. Returns the refreshed acquisition, or null when it no longer exists.</summary>
    Task<Contracts.Acquisition.AcquisitionDetail?> QueueAsync(Guid acquisitionId, Guid candidateId, CancellationToken cancellationToken);
}

/// <summary>Plans how a completed download's files map into the target library root.</summary>
public interface IAcquisitionImportPlanner {
    /// <summary>
    /// Inspects the downloaded content at <paramref name="contentPath"/> and produces an import plan of
    /// absolute source → absolute target moves into the library root, or a block when the payload is ambiguous.
    /// </summary>
    Task<ResolvedImportPlan> PlanAsync(string contentPath, string libraryRootPath, BookImportProfile profile, ImportTemplateContext context, CancellationToken cancellationToken);
}

/// <summary>An import plan resolved to absolute paths, ready to execute.</summary>
public sealed record ResolvedImportPlan(bool Blocked, ImportBlockReason? BlockReason, IReadOnlyList<ResolvedImportItem> Items) {
    public static ResolvedImportPlan Block(ImportBlockReason reason) => new(true, reason, []);
}

/// <summary>One resolved move: absolute source file to absolute destination under the library root.</summary>
public sealed record ResolvedImportItem(string SourceAbsolutePath, string TargetAbsolutePath);

/// <summary>Executes the file moves of a resolved import plan, returning the final on-disk paths.</summary>
public interface IImportFileMover {
    /// <summary>
    /// Places one planned file at its target (move for <see cref="ImportMode.Move"/>, copy otherwise),
    /// creating parent directories and giving colliding targets a stable numeric suffix. Returns the final path.
    /// </summary>
    Task<string> PlaceAsync(ResolvedImportItem item, ImportMode mode, CancellationToken cancellationToken);
}

/// <summary>Stamps acquisition-supplied identity onto a freshly scanned book so auto-identify resolves it ID-first.</summary>
public interface IAcquisitionHintApplier {
    /// <summary>
    /// Looks up an unconsumed import hint whose source path matches <paramref name="sourcePath"/> and, if found,
    /// writes its external/plugin ids onto the entity and marks the hint consumed. Returns true when applied.
    /// </summary>
    Task<bool> ApplyAsync(Guid entityId, string sourcePath, CancellationToken cancellationToken);

    /// <summary>
    /// Binds a request-created wanted entity of <paramref name="kind"/> (a book, a movie, an album) to
    /// the path the scan is about to upsert: when an unconsumed import hint matching
    /// <paramref name="sourcePath"/> carries a wanted-entity link, the imported path becomes that
    /// entity's source file and its Wanted state clears — so the scan's path-keyed upsert finds the
    /// wanted entity instead of creating a duplicate. Call BEFORE the kind's upsert for the path.
    /// Returns true when a wanted entity was bound.
    /// </summary>
    Task<bool> BindWantedEntityAsync(EntityKind kind, string sourcePath, CancellationToken cancellationToken);

    /// <summary>
    /// Binds a request-created wanted ancestor grouping of <paramref name="parentKind"/> (an author, an
    /// artist, a series) to the folder the scan is about to upsert: when an unconsumed hint under
    /// <paramref name="folderPath"/> links a wanted entity with a fileless ancestor of that kind, the
    /// folder becomes the ancestor's source path and its Wanted state clears — so the scan reuses the
    /// wanted grouping instead of creating a second one. Call BEFORE the grouping's upsert.
    /// Returns true when a wanted ancestor was bound.
    /// </summary>
    Task<bool> BindWantedParentAsync(EntityKind parentKind, string folderPath, CancellationToken cancellationToken);

    /// <summary>
    /// Binds a wanted positioned child (a phantom season under its series, a phantom episode under its
    /// season) to the path the scan is about to upsert: when the entity whose source path is
    /// <paramref name="parentPath"/> has a fileless wanted child of <paramref name="childKind"/> at
    /// sibling sort order <paramref name="sortOrder"/>, <paramref name="childPath"/> becomes that
    /// child's source path and its Wanted state clears — so the scan's upsert finds the phantom instead
    /// of creating a duplicate. Works with or without an import hint: a monitored on-disk series
    /// gaining new episode files binds its phantoms the same way. Call BEFORE the child's upsert.
    /// Returns true when a phantom was bound.
    /// </summary>
    Task<bool> BindWantedChildBySortOrderAsync(EntityKind childKind, string parentPath, int sortOrder, string childPath, CancellationToken cancellationToken);
}

/// <summary>Persistence port for acquisition records and their scored release candidates.</summary>
public interface IAcquisitionStore {
    Task<AcquisitionSummary> CreateAsync(AcquisitionMetadata metadata, CancellationToken cancellationToken);
    Task<IReadOnlyList<AcquisitionSummary>> ListAsync(CancellationToken cancellationToken);
    Task<AcquisitionDetail?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Hard-deletes an acquisition and its candidates/transfers/hints via cascade. Returns false when it no longer exists.</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Returns the search input (title/author) for an acquisition, or null when it no longer exists.</summary>
    Task<AcquisitionSearchInput?> GetSearchInputAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Returns an acquisition's current status, or null when it no longer exists.</summary>
    Task<AcquisitionStatus?> GetStatusAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// When the given acquisition is an upgrade child (its <c>UpgradeOfAcquisitionId</c> is set), returns the
    /// owned quality of the parent it must beat — used to run the child's search as an upgrade search. The
    /// returned record carries the parent's quality in its kind's vocabulary (a book rank, or a media ladder
    /// code). Returns null for an ordinary acquisition, so callers can tell an upgrade grab apart from a first
    /// grab.
    /// </summary>
    Task<UpgradeOwnedQuality?> GetUpgradeOwnedQualityAsync(Guid acquisitionId, CancellationToken cancellationToken);

    /// <summary>Assembles everything the upgrade-replace job needs from a downloaded upgrade child, or null when it is not a resolvable upgrade child.</summary>
    Task<UpgradeReplaceTarget?> GetUpgradeReplaceTargetAsync(Guid childId, CancellationToken cancellationToken);

    /// <summary>Updates an acquisition's owned book quality (e.g. after a successful upgrade swap) without changing its status.</summary>
    Task UpdateOwnedQualityAsync(Guid acquisitionId, BookQualityRank ownedQuality, CancellationToken cancellationToken);

    /// <summary>
    /// Updates an acquisition's owned media-quality ladder code, revision, and custom-format score (after a
    /// successful movie/episode upgrade swap) without changing its status. <paramref name="ownedMediaRevision"/>
    /// is the PROPER/REPACK revision detected from the release that replaced the owned copy, so the upgrade
    /// loop's same-quality proper comparison sees the advance; <paramref name="ownedFormatScore"/> is that
    /// release's total custom-format score, so the same-quality format-score comparison sees it too.
    /// </summary>
    Task UpdateOwnedMediaQualityAsync(Guid acquisitionId, string ownedMediaQuality, int ownedMediaRevision, int ownedFormatScore, CancellationToken cancellationToken);

    /// <summary>
    /// Fills in held metadata from a provider enrichment, gap-only: a poster only when none is set, a year only
    /// when none is set, and a description only when the held one is empty or shorter. Never overwrites richer
    /// data the request already captured.
    /// </summary>
    Task EnrichMetadataAsync(Guid acquisitionId, string? description, string? posterUrl, int? year, CancellationToken cancellationToken);

    Task SetStatusAsync(Guid id, AcquisitionStatus status, string? message, CancellationToken cancellationToken);

    /// <summary>
    /// Atomically marks an acquisition <see cref="AcquisitionStatus.Imported"/>, records the owned quality it
    /// imported, and flags the quality as captured — all in one commit, so the upgrade due-policy never sees a
    /// half-imported acquisition with a floor owned quality and mistakes it for "owns nothing".
    /// <paramref name="ownedMediaQuality"/> is the code on the kind's video/audio ladder (movies, TV, music);
    /// when non-null it is stored alongside the book tiers and the same captured flag is set.
    /// <paramref name="ownedMediaRevision"/> is the PROPER/REPACK revision detected from the same selected
    /// release title, stored alongside the media quality (defaults to 1; ignored by book kinds).
    /// <paramref name="ownedFormatScore"/> is the total custom-format score of the same selected release
    /// (computed against the profile's formats), stored for every kind so the upgrade loop's same-quality
    /// format-score cutoff can advance (defaults to 0).
    /// </summary>
    Task MarkImportedWithQualityAsync(Guid id, BookQualityRank ownedQuality, string? message, CancellationToken cancellationToken, string? ownedMediaQuality = null, int ownedMediaRevision = 1, int ownedFormatScore = 0);

    /// <summary>Replaces an acquisition's candidate set with a freshly scored search result.</summary>
    Task ReplaceCandidatesAsync(Guid id, IReadOnlyList<ScoredRelease> candidates, CancellationToken cancellationToken);

    /// <summary>Loads the server-side download details for a candidate belonging to an acquisition, or null when absent.</summary>
    Task<AcquisitionQueueCandidate?> GetQueueCandidateAsync(Guid acquisitionId, Guid candidateId, CancellationToken cancellationToken);

    /// <summary>Lists an acquisition's accepted candidates best-first, with the identity fields the failed-handler needs to skip blocklisted ones.</summary>
    Task<IReadOnlyList<AcquisitionCandidateRef>> ListAcceptedCandidatesAsync(Guid acquisitionId, CancellationToken cancellationToken);

    /// <summary>
    /// Marks every candidate of an acquisition whose normalized release identity equals <paramref name="identity"/>
    /// as rejected with <see cref="ReleaseRejectionReason.Blocklisted"/>, so the picker reflects a manual block
    /// immediately — including duplicate rows for the same release returned by other indexers.
    /// </summary>
    Task MarkCandidatesBlocklistedAsync(Guid acquisitionId, string identity, CancellationToken cancellationToken);

    /// <summary>Records the release an acquisition was sent to download, so a later failure can blocklist exactly it.</summary>
    Task SetSelectedReleaseAsync(Guid acquisitionId, SelectedRelease selected, CancellationToken cancellationToken);

    /// <summary>Reads the last release an acquisition was sent to download, or null when none was recorded.</summary>
    Task<SelectedRelease?> GetSelectedReleaseAsync(Guid acquisitionId, CancellationToken cancellationToken);

    /// <summary>Records a started transfer linking an acquisition to its download-client item.</summary>
    Task CreateTransferAsync(Guid acquisitionId, Guid? downloadClientConfigId, string clientItemId, string? category, CancellationToken cancellationToken, TransferSeedGoal? seedGoal = null);

    /// <summary>Transfers under seeding watch (imported by hardlink/copy, waiting for their seed goal).</summary>
    Task<IReadOnlyList<SeedingTransfer>> ListSeedingTransfersAsync(CancellationToken cancellationToken);

    /// <summary>Puts an imported acquisition's transfer under seeding watch (no-op when it carries no seed goal).</summary>
    Task<bool> MarkTransferSeedingAsync(Guid acquisitionId, DateTimeOffset since, CancellationToken cancellationToken);

    /// <summary>Ends a transfer's seeding watch (goal met or the torrent is gone).</summary>
    Task ClearTransferSeedingAsync(Guid transferId, CancellationToken cancellationToken);

    /// <summary>Lists transfers whose acquisitions are still queued or downloading, for the monitor to advance.</summary>
    Task<IReadOnlyList<ActiveTransfer>> ListActiveTransfersAsync(CancellationToken cancellationToken);

    /// <summary>True when any acquisition still has an in-flight transfer; gates scheduling the monitor job.</summary>
    Task<bool> HasActiveTransfersAsync(CancellationToken cancellationToken);

    /// <summary>Updates a transfer's progress, raw state, and on-disk content path.</summary>
    Task UpdateTransferAsync(Guid transferId, double progress, string? state, string? contentPath, CancellationToken cancellationToken);

    /// <summary>
    /// Sets (or clears, with null) the timestamp at which a transfer was first observed stalled. The monitor
    /// uses it as the anchor for the stall grace window before abandoning a stuck download.
    /// </summary>
    Task MarkTransferStalledAsync(Guid transferId, DateTimeOffset? stalledSince, CancellationToken cancellationToken);

    /// <summary>Loads the full import context (metadata + profile + completed download path) for an acquisition.</summary>
    Task<AcquisitionImportContext?> GetImportContextAsync(Guid acquisitionId, CancellationToken cancellationToken);

    /// <summary>Loads the transfer wiring (status, final path, client item) for an acquisition, or null when absent.</summary>
    Task<AcquisitionTransferInfo?> GetTransferInfoAsync(Guid acquisitionId, CancellationToken cancellationToken);

    /// <summary>Records the final on-disk location of the imported payload.</summary>
    Task SetFinalSourcePathAsync(Guid acquisitionId, string finalSourcePath, CancellationToken cancellationToken);

    /// <summary>Writes the path-keyed identity hint the book scan consumes to stamp the new entity.</summary>
    Task WriteImportHintAsync(Guid acquisitionId, string sourcePath, AcquisitionImportContext context, BookQualityRank ownedQuality, CancellationToken cancellationToken);

    /// <summary>True when any acquisition targets this wanted library entity; a request commit uses it to avoid double-requesting.</summary>
    Task<bool> AnyForEntityAsync(Guid entityId, CancellationToken cancellationToken);

    /// <summary>The newest acquisition targeting this library entity with its candidates, or null when it has none.</summary>
    Task<AcquisitionDetail?> GetLatestForEntityAsync(Guid entityId, CancellationToken cancellationToken);

    /// <summary>Every acquisition id targeting this library entity, newest first.</summary>
    Task<IReadOnlyList<Guid>> ListIdsForEntityAsync(Guid entityId, CancellationToken cancellationToken);
}

/// <summary>
/// Persistence port for monitors — standing intents that keep an acquisition's release search alive until
/// the wanted item is acquired. The "due" and reconciliation logic (fulfilling a monitor whose acquisition
/// imported, pausing one whose acquisition was deleted/cancelled) lives here so the sweep handler stays thin.
/// </summary>
public interface IMonitorStore {
    /// <summary>Starts (or re-activates) monitoring for an acquisition. Idempotent on the acquisition — returns the existing monitor if one exists.</summary>
    Task<Contracts.Acquisition.MonitorView> StartAsync(Guid acquisitionId, Domain.Entities.EntityKind kind, string title, string? author, CancellationToken cancellationToken);

    /// <summary>
    /// Starts (or re-activates) a container monitor watching a library entity (an author, an artist) for
    /// new works. Idempotent on the entity — returns the existing monitor if one exists. A non-null
    /// <paramref name="targeting"/> stores the request-time library/profile choices on the monitor
    /// (phantom requests inherit them later); null leaves any stored choices untouched. A non-null
    /// <paramref name="preset"/> records the monitoring preset that governs whether future syncs
    /// auto-monitor newly discovered works; null leaves any stored preset untouched (a sync never clobbers
    /// what an explicit request chose).
    /// </summary>
    Task<Contracts.Acquisition.MonitorView> StartForEntityAsync(Guid entityId, Domain.Entities.EntityKind kind, string title, AcquisitionTargeting? targeting, Domain.Entities.MonitorPreset? preset, CancellationToken cancellationToken);

    /// <summary>Returns the container monitor watching an entity, or null when the entity is not monitored.</summary>
    Task<Contracts.Acquisition.MonitorView?> GetByEntityAsync(Guid entityId, CancellationToken cancellationToken);

    /// <summary>The request-time library/profile choices stored on an entity's container monitor, or null when it has none.</summary>
    Task<AcquisitionTargeting?> GetTargetingByEntityAsync(Guid entityId, CancellationToken cancellationToken);

    /// <summary>
    /// The monitoring preset stored on an entity's container monitor, or null when the entity is not
    /// monitored. The discovery sync consults it to decide whether newly discovered works are
    /// auto-monitored (only <see cref="Domain.Entities.MonitorPreset.All"/> and
    /// <see cref="Domain.Entities.MonitorPreset.Future"/> materialize them).
    /// </summary>
    Task<Domain.Entities.MonitorPreset?> GetPresetByEntityAsync(Guid entityId, CancellationToken cancellationToken);

    /// <summary>Stops monitoring by hard-deleting the monitor row (the acquisition is left untouched). Returns false when it no longer exists.</summary>
    Task<bool> DeleteAsync(Guid monitorId, CancellationToken cancellationToken);

    /// <summary>Sets a monitor's status (pause/resume). Returns false when it no longer exists.</summary>
    Task<bool> SetStatusAsync(Guid monitorId, Domain.Entities.MonitorStatus status, CancellationToken cancellationToken);

    /// <summary>Lists all monitors (with each linked acquisition's status) for the monitored/wanted surface, newest first.</summary>
    Task<IReadOnlyList<Contracts.Acquisition.MonitorView>> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// A page of the Wanted "Missing" list, newest-monitor-first: active per-item monitors (skipping
    /// container discovery follows) whose acquisition is not yet <see cref="Domain.Entities.AcquisitionStatus.Imported"/>
    /// (or whose acquisition is gone). The imported-vs-not filter is applied in SQL and the page is sliced in
    /// SQL (<c>Skip</c>/<c>Take</c>), so the query stays cheap at Sonarr scale. <paramref name="pageSize"/> is
    /// clamped (a floor of 1, a ceiling of 200); <paramref name="page"/> is 1-based. <paramref name="kind"/>
    /// filters to one media kind when set. <see cref="WantedPage.Total"/> is the exact SQL count of matching
    /// monitors. Each item's <c>NextSearchAt</c> reflects the same exponential backoff the due sweep uses.
    /// </summary>
    Task<WantedPage> ListMissingAsync(int page, int pageSize, Domain.Entities.EntityKind? kind, CancellationToken cancellationToken);

    /// <summary>
    /// A page of the Wanted "Cutoff Unmet" list, newest-monitor-first: active monitors whose acquisition IS
    /// imported but whose owned copy is still below its kind's cutoff (the same cutoff evaluation the due
    /// sweep runs, so the two never disagree — books compare source/format tiers, media compares the ladder
    /// position and the custom-format score). The imported+active filter and the page slice run in SQL; the
    /// cutoff comparison then runs in memory over the materialized page, dropping rows that are actually at or
    /// above cutoff (fulfilled-but-not-yet-swept). Because the cutoff refinement is per-page,
    /// <see cref="WantedPage.Total"/> is the SQL count of the imported+active set — an UPPER BOUND on the true
    /// unmet count, not an exact figure. <paramref name="pageSize"/> is clamped (floor 1, ceiling 200);
    /// <paramref name="page"/> is 1-based; <paramref name="kind"/> filters to one media kind when set.
    /// </summary>
    Task<WantedPage> ListCutoffUnmetAsync(int page, int pageSize, Domain.Entities.EntityKind? kind, CancellationToken cancellationToken);

    /// <summary>Returns the monitor linked to an acquisition, or null when it is not monitored.</summary>
    Task<Contracts.Acquisition.MonitorView?> GetByAcquisitionAsync(Guid acquisitionId, CancellationToken cancellationToken);

    /// <summary>Cheap gate for the scheduler: true when any active monitor with a live acquisition exists.</summary>
    Task<bool> HasActiveMonitorsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Reconciles monitors (fulfilling those whose acquisition imported, pausing orphaned/cancelled ones)
    /// and returns the active monitors whose re-search is now due given the default interval.
    /// </summary>
    Task<IReadOnlyList<DueMonitor>> ListDueMonitorsAsync(int defaultIntervalMinutes, CancellationToken cancellationToken);

    /// <summary>Stamps a monitor as just re-searched so it is not picked up again until the interval elapses.</summary>
    Task MarkSearchedAsync(Guid monitorId, CancellationToken cancellationToken);

    /// <summary>
    /// Atomically claims the monitor's one upgrade slot and creates a child acquisition (copied from the
    /// parent, linked via <c>UpgradeOfAcquisitionId</c>) to search for a better release. Returns the child id,
    /// or null when the slot is already taken (an upgrade is in flight) or the parent is gone. Serialized by
    /// the MonitoredSearch singleton job, so the claim cannot race.
    /// </summary>
    Task<Guid?> CreateUpgradeChildAsync(Guid monitorId, CancellationToken cancellationToken);

    /// <summary>
    /// Releases the upgrade slot for the monitor whose in-flight child this was, and counts the outcome: a
    /// successful swap increments the replacement count, a failure increments the barren-search count (both
    /// feed the best-effort caps). Called by the replace job; the due-sweep handles children that never
    /// reached it (their slot is still claimed).
    /// </summary>
    Task ResolveUpgradeChildAsync(Guid childId, bool succeeded, CancellationToken cancellationToken);
}

/// <summary>
/// Persistence port for the acquisition blocklist: release identities refused for future grabs. Consulted
/// by the search runner (to reject blocklisted releases) and written by failed-download auto-recovery and
/// manual blocking.
/// </summary>
public interface IAcquisitionBlocklistStore {
    /// <summary>Returns every blocklisted release identity, for the decision engine's blocklist gate.</summary>
    Task<IReadOnlySet<string>> GetIdentitiesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Adds a release identity to the blocklist. Idempotent and first-reason-wins: if the identity is
    /// already present its existing reason, message, and timestamp are kept and the request is a no-op
    /// (so an automatic <see cref="BlocklistReason.Failed"/> entry is not overwritten by a later add).
    /// </summary>
    Task AddAsync(BlocklistAddRequest request, CancellationToken cancellationToken);

    /// <summary>Lists blocklist entries newest-first for the management surface.</summary>
    Task<IReadOnlyList<Contracts.Acquisition.AcquisitionBlocklistEntry>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Removes a blocklist entry by id. Returns false when it no longer exists.</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);
}
