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
    bool AutoPick);

/// <summary>Persistence port for book acquisition profiles (matching rules + import target).</summary>
public interface IBookAcquisitionProfileStore {
    /// <summary>Returns the decision rules from the default profile, or <see cref="BookAcquisitionRules.Default"/> when none exists.</summary>
    Task<BookAcquisitionRules> GetDefaultRulesAsync(CancellationToken cancellationToken);

    /// <summary>Returns the import target from the default profile, or null when none exists.</summary>
    Task<BookImportProfile?> GetDefaultImportProfileAsync(CancellationToken cancellationToken);

    /// <summary>True when the default profile is set to auto-queue the top accepted release without manual review.</summary>
    Task<bool> GetDefaultAutoPickAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<BookAcquisitionProfileView>> ListAsync(CancellationToken cancellationToken);
    Task<BookAcquisitionProfileView?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<BookAcquisitionProfileView> SaveAsync(BookAcquisitionProfileSaveCommand command, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);
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
}

/// <summary>Persistence port for acquisition records and their scored release candidates.</summary>
public interface IAcquisitionStore {
    Task<AcquisitionSummary> CreateAsync(AcquisitionMetadata metadata, CancellationToken cancellationToken);
    Task<IReadOnlyList<AcquisitionSummary>> ListAsync(CancellationToken cancellationToken);
    Task<AcquisitionDetail?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Returns the search input (title/author) for an acquisition, or null when it no longer exists.</summary>
    Task<AcquisitionSearchInput?> GetSearchInputAsync(Guid id, CancellationToken cancellationToken);

    Task SetStatusAsync(Guid id, AcquisitionStatus status, string? message, CancellationToken cancellationToken);

    /// <summary>Replaces an acquisition's candidate set with a freshly scored search result.</summary>
    Task ReplaceCandidatesAsync(Guid id, IReadOnlyList<ScoredRelease> candidates, CancellationToken cancellationToken);

    /// <summary>Loads the server-side download details for a candidate belonging to an acquisition, or null when absent.</summary>
    Task<AcquisitionQueueCandidate?> GetQueueCandidateAsync(Guid acquisitionId, Guid candidateId, CancellationToken cancellationToken);

    /// <summary>Records a started transfer linking an acquisition to its download-client item.</summary>
    Task CreateTransferAsync(Guid acquisitionId, Guid? downloadClientConfigId, string clientItemId, string? category, CancellationToken cancellationToken);

    /// <summary>Lists transfers whose acquisitions are still queued or downloading, for the monitor to advance.</summary>
    Task<IReadOnlyList<ActiveTransfer>> ListActiveTransfersAsync(CancellationToken cancellationToken);

    /// <summary>True when any acquisition still has an in-flight transfer; gates scheduling the monitor job.</summary>
    Task<bool> HasActiveTransfersAsync(CancellationToken cancellationToken);

    /// <summary>Updates a transfer's progress, raw state, and on-disk content path.</summary>
    Task UpdateTransferAsync(Guid transferId, double progress, string? state, string? contentPath, CancellationToken cancellationToken);

    /// <summary>Returns the most recent transfer's client item id for an acquisition, or null when none exists.</summary>
    Task<string?> GetTransferClientItemIdAsync(Guid acquisitionId, CancellationToken cancellationToken);

    /// <summary>Loads the full import context (metadata + profile + completed download path) for an acquisition.</summary>
    Task<AcquisitionImportContext?> GetImportContextAsync(Guid acquisitionId, CancellationToken cancellationToken);

    /// <summary>Loads the transfer wiring (status, final path, client item) for an acquisition, or null when absent.</summary>
    Task<AcquisitionTransferInfo?> GetTransferInfoAsync(Guid acquisitionId, CancellationToken cancellationToken);

    /// <summary>Records the final on-disk location of the imported payload.</summary>
    Task SetFinalSourcePathAsync(Guid acquisitionId, string finalSourcePath, CancellationToken cancellationToken);

    /// <summary>Writes the path-keyed identity hint the book scan consumes to stamp the new entity.</summary>
    Task WriteImportHintAsync(Guid acquisitionId, string sourcePath, AcquisitionImportContext context, CancellationToken cancellationToken);
}
