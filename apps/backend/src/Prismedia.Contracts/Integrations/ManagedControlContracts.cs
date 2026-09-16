using Prismedia.Domain.Entities;

namespace Prismedia.Contracts.Integrations;

/// <summary>One exact remote target in a reviewed scope; titles do not establish its identity.</summary>
public sealed record ManagedControlTarget(string RemoteId, EntityKind EntityKind,
    int? SeasonNumber = null, int? EpisodeNumber = null, int? AbsoluteNumber = null);
/// <summary>A pinned work and finite content targets; a series ID alone does not authorize every episode.</summary>
public sealed record ManagedControlScope(ManagedItemInput Item, IReadOnlyList<ManagedControlTarget> Targets);
/// <summary>The manager's current monitoring flag for one selected target.</summary>
public sealed record ManagedTargetMonitoring(ManagedControlTarget Target, bool Monitored);
/// <summary>Operations the adapter can faithfully apply to this exact scope without changing unrelated targets.</summary>
public sealed record ManagedControlCapabilities(bool CanSearch, bool CanChangeMonitoring, bool CanChangeProfile,
    string? MonitoringUnavailableReason = null);
/// <summary>Remote command identity and its original queue timestamp, which fence reuse of transient numeric IDs.</summary>
public sealed record ManagedCommandReference(string Id, DateTimeOffset QueuedAt);
/// <summary>Observed command execution, never evidence that content has become locally available.</summary>
public sealed record ManagedCommandSnapshot(ManagedCommandReference Reference, ManagedCommandStatus Status, string? Problem = null);
/// <summary>Fresh configuration and optional command evidence for one exact manager scope.</summary>
public sealed record ManagedControlState(ManagedLibraryItem Item, string Path,
    IReadOnlyList<ManagedTargetMonitoring> Targets, ManagedControlCapabilities Capabilities, ManagedCommandSnapshot? Command = null);
/// <summary>Observes current configuration and, when supplied, one previously acknowledged command ID.</summary>
public sealed record ReconcileManagedInput(ManagedControlScope Scope, ManagedCommandReference? Command = null);
/// <summary>Explicit field changes. Omission preserves a setting; an omitted profile never chooses a default.</summary>
public sealed record ManagedConfigurationChange(string? ProfileId = null, bool? Monitored = null);
/// <summary>
/// Applies reviewed fields only when identity, path, and relevant expected values still agree.
/// OperationId belongs to Prismedia's durable intent; it does not imply upstream idempotency support.
/// </summary>
public sealed record ConfigureManagedInput(Guid OperationId, ManagedControlScope Scope, string ExpectedPath,
    string ExpectedProfileId, IReadOnlyDictionary<string, bool> ExpectedMonitoring, ManagedConfigurationChange Changes);
/// <summary>Requests one search using the reviewed profile, path and finite scope; no implicit monitoring changes.</summary>
public sealed record RequestManagedInput(Guid OperationId, ManagedControlScope Scope, string ExpectedPath, string ExpectedProfileId);
/// <summary>Verified mutation outcome. Uncertain execution must fail the invocation instead of claiming rejection.</summary>
public sealed record ManagedMutationResult(ManagedMutationOutcome Outcome, ManagedCommandSnapshot? Command = null, string? Problem = null);
