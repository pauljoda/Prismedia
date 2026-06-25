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

/// <summary>Persistence port for the book acquisition matching/import profile.</summary>
public interface IBookAcquisitionProfileStore {
    /// <summary>Returns the decision rules from the default profile, or <see cref="BookAcquisitionRules.Default"/> when none exists.</summary>
    Task<BookAcquisitionRules> GetDefaultRulesAsync(CancellationToken cancellationToken);
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
}
