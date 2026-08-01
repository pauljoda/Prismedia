using Prismedia.Domain.Entities;

namespace Prismedia.Application.Entities;

/// <summary>
/// Application port that loads the bounded domain state needed for a user mutation and
/// persists only that state. The implementation owns the EF unit-of-work boundary, but it
/// deliberately is not an aggregate graph writer: capability actions must never replace
/// files, relationships, credits, or metadata that they did not change.
/// </summary>
public interface IEntityWriteRepository {
    /// <summary>
    /// Opens a retry attempt for one logical entity mutation. If a save conflicts, the caller
    /// must roll back the attempt before reloading and applying the action again; this removes
    /// only work staged by that attempt and leaves unrelated work in the request unit of work
    /// intact.
    /// </summary>
    /// <remarks>
    /// Implementations that do not share a unit of work with staged side effects can use the
    /// default no-op attempt. EF-backed implementations use this to detach failed-attempt rows
    /// such as playback and activity events before a retry.
    /// </remarks>
    IEntityWriteAttempt BeginAttempt() => NoOpEntityWriteAttempt.Instance;

    /// <summary>
    /// Finds an active entity and hydrates only its own mutable state, excluding children
    /// and relationships. Use for user-state writes that should not load a whole subtree.
    /// </summary>
    /// <param name="id">Entity identifier.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    Task<Entity?> FindShallowAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Finds the persisted structural parent identifier for an active entity without
    /// hydrating the full parent slice. Returns null for root entities or missing rows.
    /// </summary>
    /// <param name="id">Entity identifier.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    Task<Guid?> FindParentIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Persists the explicit mutable slice of a shallow entity. The entity row is touched only
    /// for its optimistic-concurrency token, timestamp, and requested curation fields; only
    /// the requested user-state and capability mappers may write dependent rows.
    /// </summary>
    /// <param name="entity">Entity to persist.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    Task SaveMutableStateAsync(
        Entity entity,
        EntityMutableStateChange change,
        CancellationToken cancellationToken);
}

/// <summary>
/// Explicit persistence ownership for one Entity mutation. Domain capability types travel with
/// the mutation so Infrastructure can discover the mapper that owns them; no central list needs
/// editing when a new mutable capability is introduced.
/// </summary>
public sealed class EntityMutableStateChange {
    /// <summary>Creates a bounded mutation selection.</summary>
    public EntityMutableStateChange(
        bool userOpinionChanged = false,
        bool curationFlagsChanged = false,
        params Type[] changedCapabilityTypes) {
        UserOpinionChanged = userOpinionChanged;
        CurationFlagsChanged = curationFlagsChanged;
        ChangedCapabilityTypes = new HashSet<Type>(changedCapabilityTypes);
    }

    /// <summary>Whether the current user's favorite/rating fields changed.</summary>
    public bool UserOpinionChanged { get; }

    /// <summary>Whether the Entity's NSFW or organized fields changed.</summary>
    public bool CurationFlagsChanged { get; }

    /// <summary>Exact domain capability types whose persistence mappers may run.</summary>
    public IReadOnlySet<Type> ChangedCapabilityTypes { get; }

    /// <summary>Whether this selection has a persistent effect.</summary>
    public bool HasChanges => UserOpinionChanged || CurationFlagsChanged || ChangedCapabilityTypes.Count > 0;
}

/// <summary>
/// Owns the staged persistence work for one logical entity mutation attempt.
/// </summary>
public interface IEntityWriteAttempt : IDisposable {
    /// <summary>
    /// Reverts the entries introduced or changed by this attempt after an optimistic-concurrency
    /// conflict, so a subsequent attempt starts from a freshly loaded persistence state.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the rollback.</param>
    Task RollbackAsync(CancellationToken cancellationToken);
}

internal sealed class NoOpEntityWriteAttempt : IEntityWriteAttempt {
    internal static NoOpEntityWriteAttempt Instance { get; } = new();

    private NoOpEntityWriteAttempt() {
    }

    public void Dispose() {
    }

    public Task RollbackAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// Raised by <see cref="IEntityWriteRepository.SaveMutableStateAsync"/> when a concurrent writer modified the
/// same entity between load and save (optimistic concurrency conflict). Callers that own the
/// mutation can reload and re-apply it. This is a persistence-agnostic abstraction so the
/// application layer can retry without depending on EF Core's <c>DbUpdateConcurrencyException</c>.
/// </summary>
public sealed class EntityConcurrencyConflictException : Exception {
    /// <summary>Creates the conflict exception wrapping the underlying persistence failure.</summary>
    public EntityConcurrencyConflictException(string message, Exception innerException)
        : base(message, innerException) {
    }
}
