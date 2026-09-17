using Prismedia.Domain.Entities;

namespace Prismedia.Contracts.Integrations;

/// <summary>Exact metadata identities, independent of an external application's local item IDs.</summary>
public sealed record ManagedLookupInput(EntityKind EntityKind, IReadOnlyDictionary<string, string> ExternalIds);
/// <summary>A work confirmed by the manager's metadata source; it is not evidence of owned files.</summary>
public sealed record ManagedCandidate(EntityKind EntityKind, string Title, int? Year, IReadOnlyDictionary<string, string> ExternalIds);
/// <summary>Read-only lookup can reconcile an uncertain creation without repeating its mutation.</summary>
public sealed record ManagedLookupResult(ManagedCandidate Candidate, ManagedItemSnapshot? Existing);
/// <summary>Initial creation is unmonitored and does not search. Existing holdings retain all settings.</summary>
public sealed record EnsureManagedInput(Guid OperationId, ManagedLookupInput Work, string ProfileId, string RootId, string ExpectedRootPath);
/// <summary>Only definite outcomes may be returned; lost responses must leave the host's creation intent uncertain.</summary>
public sealed record EnsureManagedResult(ManagedMutationOutcome Outcome, ManagedItemSnapshot? Holding = null, bool Created = false, string? Problem = null);
