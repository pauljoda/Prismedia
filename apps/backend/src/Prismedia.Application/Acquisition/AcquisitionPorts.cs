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
    IReadOnlyList<int> Categories);

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

/// <summary>Command for creating or updating a book acquisition profile.</summary>
public sealed record BookAcquisitionProfileSaveCommand(
    Guid? Id,
    string DisplayName,
    bool IsDefault,
    Guid TargetLibraryRootId,
    string PathTemplate,
    ImportMode ImportMode,
    IReadOnlyList<BookFormat> AllowedFormats,
    string? Language,
    int MinSeeders,
    long? MinSizeBytes,
    long? MaxSizeBytes,
    IReadOnlyList<string> RequiredTerms,
    IReadOnlyList<string> IgnoredTerms,
    IReadOnlyList<string> PreferredTerms,
    bool AutoPick,
    bool AutoRedownload,
    bool UpgradeUntilCutoff,
    BookSourceTier CutoffSourceTier,
    BookFormatTier CutoffFormatTier);

/// <summary>Persistence port for book acquisition profiles (matching rules + import target).</summary>
public interface IBookAcquisitionProfileStore {
    /// <summary>Returns the decision rules from the default profile, or <see cref="BookAcquisitionRules.Default"/> when none exists.</summary>
    Task<BookAcquisitionRules> GetDefaultRulesAsync(CancellationToken cancellationToken);

    /// <summary>Returns the import target from the default profile, or null when none exists.</summary>
    Task<BookImportProfile?> GetDefaultImportProfileAsync(CancellationToken cancellationToken);

    /// <summary>True when the default profile is set to auto-queue the top accepted release without manual review.</summary>
    Task<bool> GetDefaultAutoPickAsync(CancellationToken cancellationToken);

    /// <summary>True when the default profile auto-blocklists a failed download and grabs the next-best candidate.</summary>
    Task<bool> GetDefaultAutoRedownloadAsync(CancellationToken cancellationToken);

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
    /// Binds a request-created wanted book to the path the scan is about to upsert: when an unconsumed
    /// import hint matching <paramref name="sourcePath"/> carries a wanted-entity link, the imported path
    /// becomes that entity's source file and its Wanted state clears — so the scan's path-keyed upsert
    /// finds the wanted entity instead of creating a duplicate. Call BEFORE the book upsert for the path.
    /// Returns true when a wanted entity was bound.
    /// </summary>
    Task<bool> BindWantedBookAsync(string sourcePath, CancellationToken cancellationToken);

    /// <summary>
    /// Binds a request-created wanted author to the author folder the scan is about to upsert: when an
    /// unconsumed hint under <paramref name="authorFolderPath"/> links a wanted book whose parent is a
    /// fileless author entity, the folder becomes that author's source path and its Wanted state clears —
    /// so the scan reuses the wanted author instead of creating a second one. Call BEFORE the author
    /// upsert. Returns true when a wanted author was bound.
    /// </summary>
    Task<bool> BindWantedAuthorAsync(string authorFolderPath, CancellationToken cancellationToken);
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
    /// owned quality of the parent it must beat — used to run the child's search as an upgrade search. Returns
    /// null for an ordinary acquisition, so callers can tell an upgrade grab apart from a first grab.
    /// </summary>
    Task<BookQualityRank?> GetUpgradeOwnedQualityAsync(Guid acquisitionId, CancellationToken cancellationToken);

    /// <summary>Assembles everything the upgrade-replace job needs from a downloaded upgrade child, or null when it is not a resolvable upgrade child.</summary>
    Task<UpgradeReplaceTarget?> GetUpgradeReplaceTargetAsync(Guid childId, CancellationToken cancellationToken);

    /// <summary>Updates an acquisition's owned quality (e.g. after a successful upgrade swap) without changing its status.</summary>
    Task UpdateOwnedQualityAsync(Guid acquisitionId, BookQualityRank ownedQuality, CancellationToken cancellationToken);

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
    /// </summary>
    Task MarkImportedWithQualityAsync(Guid id, BookQualityRank ownedQuality, string? message, CancellationToken cancellationToken);

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
    Task CreateTransferAsync(Guid acquisitionId, Guid? downloadClientConfigId, string clientItemId, string? category, CancellationToken cancellationToken);

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

    /// <summary>Returns the most recent transfer's client item id for an acquisition, or null when none exists.</summary>
    Task<string?> GetTransferClientItemIdAsync(Guid acquisitionId, CancellationToken cancellationToken);

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
    /// new works. Idempotent on the entity — returns the existing monitor if one exists.
    /// </summary>
    Task<Contracts.Acquisition.MonitorView> StartForEntityAsync(Guid entityId, Domain.Entities.EntityKind kind, string title, CancellationToken cancellationToken);

    /// <summary>Returns the container monitor watching an entity, or null when the entity is not monitored.</summary>
    Task<Contracts.Acquisition.MonitorView?> GetByEntityAsync(Guid entityId, CancellationToken cancellationToken);

    /// <summary>Stops monitoring by hard-deleting the monitor row (the acquisition is left untouched). Returns false when it no longer exists.</summary>
    Task<bool> DeleteAsync(Guid monitorId, CancellationToken cancellationToken);

    /// <summary>Sets a monitor's status (pause/resume). Returns false when it no longer exists.</summary>
    Task<bool> SetStatusAsync(Guid monitorId, Domain.Entities.MonitorStatus status, CancellationToken cancellationToken);

    /// <summary>Lists all monitors (with each linked acquisition's status) for the monitored/wanted surface, newest first.</summary>
    Task<IReadOnlyList<Contracts.Acquisition.MonitorView>> ListAsync(CancellationToken cancellationToken);

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
