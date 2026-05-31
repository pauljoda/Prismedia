using Prismedia.Domain.Entities;

namespace Prismedia.Application.Entities;

/// <summary>
/// Application port that loads a hydrated domain <see cref="Entity"/> for mutation and
/// persists it back. The implementation lives in Infrastructure (EF Core) and owns the
/// row-to-domain hydration and unit-of-work boundary.
/// </summary>
public interface IEntityWriteRepository {
    /// <summary>
    /// Finds an active entity and hydrates its domain relationships plus mutable state capabilities.
    /// Returns null when no active entity exists for the given identifier.
    /// </summary>
    /// <param name="id">Entity identifier.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    Task<Entity?> FindAsync(Guid id, CancellationToken cancellationToken);

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
    /// Resolves a chapter-local reading cursor into its absolute page position within a
    /// book, using persisted structural children and page counts.
    /// </summary>
    /// <param name="bookId">Owning book identifier.</param>
    /// <param name="currentEntityId">Current chapter or page identifier.</param>
    /// <param name="index">Chapter-local zero-based page index.</param>
    /// <param name="total">Chapter-local page count reported by the reader.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    Task<BookProgressPosition?> ResolveBookProgressPositionAsync(
        Guid bookId,
        Guid currentEntityId,
        int index,
        int total,
        CancellationToken cancellationToken);

    /// <summary>
    /// Persists one hydrated domain entity slice, including structural links, relationships,
    /// and mutable capabilities. Commits as a single unit of work.
    /// </summary>
    /// <param name="entity">Entity to persist.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    Task SaveAsync(Entity entity, CancellationToken cancellationToken);
}

/// <summary>
/// Absolute page cursor for a chapter inside a complete book work.
/// </summary>
/// <param name="ChapterId">Chapter that owns the local cursor.</param>
/// <param name="Index">Zero-based page index across the whole book.</param>
/// <param name="Total">Total page count across the whole book.</param>
public sealed record BookProgressPosition(Guid ChapterId, int Index, int Total);

/// <summary>
/// Raised by <see cref="IEntityWriteRepository.SaveAsync"/> when a concurrent writer modified the
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
