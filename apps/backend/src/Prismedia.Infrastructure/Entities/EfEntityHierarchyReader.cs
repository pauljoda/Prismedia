using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Entities;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Entities;

/// <summary>
/// EF-backed reader for the canonical <c>entities.parent_entity_id</c> hierarchy. Traversal is batched
/// one breadth level at a time and guarded by visited ids, so it supports arbitrary Entity kinds and
/// depth while terminating safely if legacy/corrupt rows contain a cycle.
/// </summary>
public sealed class EfEntityHierarchyReader(PrismediaDbContext db) : IEntityHierarchyReader {
    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> ListSubtreeIdsAsync(
        Guid rootEntityId,
        CancellationToken cancellationToken) {
        if (!await db.Entities.AsNoTracking().AnyAsync(row => row.Id == rootEntityId, cancellationToken)) {
            return [];
        }

        var result = new List<Guid> { rootEntityId };
        var visited = new HashSet<Guid> { rootEntityId };
        IReadOnlyList<Guid> frontier = [rootEntityId];
        while (frontier.Count > 0) {
            var parentIds = frontier.ToArray();
            var children = await db.Entities.AsNoTracking()
                .Where(row => row.ParentEntityId != null && parentIds.Contains(row.ParentEntityId.Value))
                .OrderBy(row => row.SortOrder)
                .ThenBy(row => row.CreatedAt)
                .Select(row => row.Id)
                .ToArrayAsync(cancellationToken);

            var next = new List<Guid>(children.Length);
            foreach (var childId in children) {
                if (!visited.Add(childId)) {
                    continue;
                }

                result.Add(childId);
                next.Add(childId);
            }

            frontier = next;
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> ListAncestorIdsAsync(
        Guid entityId,
        CancellationToken cancellationToken) {
        var result = new List<Guid>();
        var visited = new HashSet<Guid> { entityId };
        var currentId = entityId;
        while (true) {
            var parentId = await db.Entities.AsNoTracking()
                .Where(row => row.Id == currentId)
                .Select(row => row.ParentEntityId)
                .FirstOrDefaultAsync(cancellationToken);
            if (parentId is not { } parent || !visited.Add(parent)) {
                return result;
            }

            result.Add(parent);
            currentId = parent;
        }
    }
}
