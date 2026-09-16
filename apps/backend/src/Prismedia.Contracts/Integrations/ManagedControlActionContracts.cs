using Prismedia.Domain.Entities;

namespace Prismedia.Contracts.Integrations;

/// <summary>Reviewed manager state and stable local scope identity; upgrades do not change that scope identity.</summary>
public sealed record ManagedControlPreview(string ScopeFingerprint, ManagedControlState State, ManagerOptions Options);
/// <summary>Explicit changes based on a reviewed snapshot. Omitted fields remain unchanged.</summary>
public sealed record CreateManagedControlRequest(Guid OperationId, string ScopeFingerprint, string ExpectedPath,
    string ExpectedProfileId, IReadOnlyDictionary<string, bool> ExpectedMonitoring, ManagedConfigurationChange Changes, bool Search);
/// <summary>Optimistic concurrency for local cancellation, observation, or acknowledgement of uncertainty.</summary>
public sealed record ManagedControlRevisionRequest(long ExpectedRevision);
/// <summary>Retained control progress. Completion describes settings or search execution, never file availability.</summary>
public sealed record ManagedControlActionResponse(Guid Id, Guid ConnectionId, Guid HoldingId, long Revision,
    ManagedControlPhase Phase, ManagedConfigurationChange Changes, bool SearchRequested, bool ConfigurationConfirmed,
    ManagedCommandSnapshot? Command, bool ReviewRequired, bool CanCancel, bool CanCloseUnverified,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, string? Problem);
