namespace Prismedia.Application.Entities;

/// <summary>
/// Reads the canonical <c>Entity.ParentEntityId</c> hierarchy without interpreting media kinds. Shared
/// by every recursive Entity lifecycle so a series, artist, author, or future container follows the same
/// traversal rules.
/// </summary>
public interface IEntityHierarchyReader {
    /// <summary>
    /// Returns <paramref name="rootEntityId"/> followed by every descendant in breadth-first order. A
    /// missing root returns an empty list. Implementations must visit each id at most once so corrupt
    /// cycles cannot loop forever.
    /// </summary>
    Task<IReadOnlyList<Guid>> ListSubtreeIdsAsync(
        Guid rootEntityId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns the parent, grandparent, and remaining ancestors nearest-first. Implementations must visit
    /// each id at most once so corrupt cycles cannot loop forever; the starting entity is never returned.
    /// </summary>
    Task<IReadOnlyList<Guid>> ListAncestorIdsAsync(
        Guid entityId,
        CancellationToken cancellationToken);
}
