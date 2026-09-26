using Prismedia.Domain.Integrations;

namespace Prismedia.Application.Integrations;

/// <summary>Transactional intent, exclusive action slots, scope revalidation, and revision-fenced persistence.</summary>
public interface IManagedControlStore {
    #region Abstract Methods

    /// <summary>Derives exact targets from established bindings and verifies their retained fulfillment owner.</summary>
    Task<OwnedManagedControlScope> RequireScopeAsync(Guid connectionId, Guid holdingId, CancellationToken token);

    /// <summary>Loads retained progress without contacting the connected application.</summary>
    Task<StoredManagedControl?> FindAsync(Guid id, CancellationToken token);

    /// <summary>Lists recent actions for this holding.</summary>
    Task<IReadOnlyList<StoredManagedControl>> ListAsync(Guid connectionId, Guid holdingId, CancellationToken token);

    /// <summary>Accepts intent and first queue run in one transaction, deduplicating identical operation IDs.</summary>
    Task<StoredManagedControl> CreateAsync(ManagedControlOperation operation, ManagedControlPlan plan, CancellationToken token);

    /// <summary>Saves one revision; before dispatch, atomically revalidates the local owned scope.</summary>
    Task SaveAsync(ManagedControlOperation operation, long expectedRevision, string? problem, bool beforeDispatch, CancellationToken token);

    /// <summary>Queues observation of the existing action; never resets an uncertain dispatch.</summary>
    Task QueueAsync(Guid id, CancellationToken token);

    /// <summary>Recovers due finite actions without depending on queue history retention.</summary>
    Task QueueDueAsync(CancellationToken token);

    #endregion

    #region Actions - Scope

    /// <summary>Derives an exact append-only subset from an established holding.</summary>
    Task<OwnedManagedControlScope> RequireScopeAsync(Guid connectionId, Guid holdingId,
        IReadOnlyList<Guid> entityIds, CancellationToken token) =>
        throw new NotSupportedException("Subset manager controls are not supported by this store.");

    #endregion
}
