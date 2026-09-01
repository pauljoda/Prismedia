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

    /// <summary>
    /// Returns the distinct ancestors of every supplied Entity. Implementations should resolve the
    /// collection as one hierarchy walk when possible; the default preserves compatibility for
    /// non-database readers while still applying cycle-safe de-duplication.
    /// </summary>
    async Task<IReadOnlySet<Guid>> ListAncestorIdsAsync(
        IReadOnlyCollection<Guid> entityIds,
        CancellationToken cancellationToken) {
        var startingIds = entityIds.ToHashSet();
        var ancestors = new HashSet<Guid>();
        foreach (var entityId in startingIds) {
            foreach (var ancestorId in await ListAncestorIdsAsync(entityId, cancellationToken)) {
                if (!startingIds.Contains(ancestorId)) {
                    ancestors.Add(ancestorId);
                }
            }
        }

        return ancestors;
    }
}
