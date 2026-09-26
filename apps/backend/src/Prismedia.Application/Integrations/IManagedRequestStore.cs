using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;

namespace Prismedia.Application.Integrations;

/// <summary>Atomic request ownership, revision fences, and exact wanted-identity materialization.</summary>
public interface IManagedRequestStore {
    #region Abstract Methods

    /// <summary>
    /// Requires fileless wanted work with an exact identity and a mapped enabled library.
    /// </summary>
    /// <param name="connectionId">Connection that will own fulfillment.</param>
    /// <param name="entityId">Wanted work or container that receives the request.</param>
    /// <param name="libraryRootId">Mapped external library that will receive files.</param>
    /// <param name="targetEntityIds">Finite child scope for a container, or null for the whole work.</param>
    /// <param name="bookRendition">Exact Book rendition, required for a Book and null for other kinds.</param>
    /// <param name="token">Cancellation token.</param>
    Task<ManagedRequestTarget> RequireTargetAsync(
        Guid connectionId,
        Guid entityId,
        Guid libraryRootId,
        IReadOnlyList<Guid>? targetEntityIds,
        BookRendition? bookRendition,
        CancellationToken token);

    /// <summary>Loads retained intent without contacting the manager.</summary>
    Task<StoredManagedRequest?> FindAsync(Guid id, CancellationToken token);

    /// <summary>Lists recent requests for one connection.</summary>
    Task<IReadOnlyList<StoredManagedRequest>> ListAsync(Guid connectionId, CancellationToken token);

    /// <summary>Commits accepted intent, exclusive fulfillment ownership, and its first queue run together.</summary>
    Task<StoredManagedRequest> CreateAsync(ManagedRequestOperation operation, ManagedRequestPlan plan, CancellationToken token);

    /// <summary>Saves one revision and revalidates local scope before dispatch; safe cancellation releases its owner atomically.</summary>
    Task SaveAsync(ManagedRequestOperation operation, long expectedRevision, string? problem, bool beforeDispatch, CancellationToken token);

    /// <summary>
    /// Accepts a pinned holding and its stable wanted targets in the same transaction as request progress.
    /// A finite child scope is accepted only when every target has one resolved remote identity.
    /// </summary>
    Task AcceptHoldingAsync(
        StoredManagedRequest work,
        ManagedItemSnapshot snapshot,
        IReadOnlyList<ManagedResolvedTarget>? resolvedTargets,
        CancellationToken token);

    /// <summary>Rechecks pinned identity, ownership, and mapped path before dispatching initial fulfillment controls.</summary>
    Task ValidateHoldingAsync(StoredManagedRequest work, ManagedItemSnapshot snapshot, CancellationToken token);

    /// <summary>Attaches only verified mapped files to the retained wanted identities, then enables ordinary managed tracking.</summary>
    Task<ManagedRequestMaterialization> MaterializeAsync(StoredManagedRequest work, ManagedItemSnapshot snapshot, CancellationToken token);

    /// <summary>Queues observation without resetting uncertain creation.</summary>
    Task QueueAsync(Guid id, CancellationToken token);

    /// <summary>Recovers due work from durable intent even after queue history has been removed.</summary>
    Task QueueDueAsync(CancellationToken token);

    #endregion
}
