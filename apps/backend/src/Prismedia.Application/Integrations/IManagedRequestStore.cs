using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Integrations;

namespace Prismedia.Application.Integrations;

/// <summary>One server-derived local wanted target paired with the manager lookup evidence sent for it.</summary>
public sealed record ManagedRequestEntityTarget(Guid EntityId, ManagedLookupTarget Target);
/// <summary>Server-derived wanted identity and immutable mapped boundary.</summary>
public sealed record ManagedRequestTarget(Guid EntityId, string Title, ManagedLookupInput Work,
    ExternalLibraryMount Mount, IReadOnlyList<ManagedRequestEntityTarget>? Targets = null);
/// <summary>Immutable creation and fulfillment intent; credentials remain on the connection.</summary>
public sealed record ManagedRequestPlan(CreateManagedRequestInput Request, EnsureManagedInput Creation, string Title,
    string Fingerprint, string? ReviewedCommitFingerprint = null, long? ExpectedConnectionRevision = null,
    Guid? ExistingHoldingId = null);
/// <summary>Request journal independent of transient jobs and manager queue history.</summary>
public sealed record StoredManagedRequest(ManagedRequestOperation Operation, ManagedRequestPlan Plan,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, string? Problem);
/// <summary>Whether exact local bytes were attached; a waiting reason never claims import success.</summary>
public sealed record ManagedRequestMaterialization(bool Imported, string? WaitingReason = null);

/// <summary>Atomic request ownership, revision fences, and exact wanted-identity materialization.</summary>
public interface IManagedRequestStore {
    /// <summary>Requires fileless wanted work with an exact identity and a mapped enabled video library.</summary>
    Task<ManagedRequestTarget> RequireTargetAsync(
        Guid connectionId,
        Guid entityId,
        Guid libraryRootId,
        CancellationToken token);
    /// <summary>Requires a finite child scope when the managed work is a container.</summary>
    Task<ManagedRequestTarget> RequireTargetAsync(Guid connectionId, Guid entityId, Guid libraryRootId,
        IReadOnlyList<Guid>? targetEntityIds, CancellationToken token) =>
        RequireTargetAsync(connectionId, entityId, libraryRootId, token);
    /// <summary>Loads retained intent without contacting the manager.</summary>
    Task<StoredManagedRequest?> FindAsync(Guid id, CancellationToken token);
    /// <summary>Lists recent requests for one connection.</summary>
    Task<IReadOnlyList<StoredManagedRequest>> ListAsync(Guid connectionId, CancellationToken token);
    /// <summary>Commits accepted intent, exclusive fulfillment ownership, and its first queue run together.</summary>
    Task<StoredManagedRequest> CreateAsync(ManagedRequestOperation operation, ManagedRequestPlan plan, CancellationToken token);
    /// <summary>Saves one revision and revalidates local scope before dispatch; safe cancellation releases its owner atomically.</summary>
    Task SaveAsync(ManagedRequestOperation operation, long expectedRevision, string? problem, bool beforeDispatch, CancellationToken token);
    /// <summary>Accepts a pinned holding and its stable wanted targets in the same transaction as request progress.</summary>
    Task AcceptHoldingAsync(StoredManagedRequest work, ManagedItemSnapshot snapshot, CancellationToken token);
    /// <summary>Accepts a holding only after every finite child target has one resolved remote identity.</summary>
    Task AcceptHoldingAsync(StoredManagedRequest work, ManagedItemSnapshot snapshot,
        IReadOnlyList<ManagedResolvedTarget>? resolvedTargets, CancellationToken token) =>
        AcceptHoldingAsync(work, snapshot, token);
    /// <summary>Rechecks pinned identity, ownership, and mapped path before dispatching initial fulfillment controls.</summary>
    Task ValidateHoldingAsync(StoredManagedRequest work, ManagedItemSnapshot snapshot, CancellationToken token);
    /// <summary>Attaches only verified mapped files to the retained wanted identities, then enables ordinary managed tracking.</summary>
    Task<ManagedRequestMaterialization> MaterializeAsync(StoredManagedRequest work, ManagedItemSnapshot snapshot, CancellationToken token);
    /// <summary>Queues observation without resetting uncertain creation.</summary>
    Task QueueAsync(Guid id, CancellationToken token);
    /// <summary>Recovers due work from durable intent even after queue history has been removed.</summary>
    Task QueueDueAsync(CancellationToken token);
}

/// <summary>The accepted manager intent or its local ownership boundary changed.</summary>
public sealed class ManagedRequestConflictException(string message) : Exception(message);
