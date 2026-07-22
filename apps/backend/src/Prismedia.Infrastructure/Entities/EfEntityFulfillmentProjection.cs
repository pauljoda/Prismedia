using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Entities;

/// <summary>
/// Resolves Entity roots whose complete structural subtree is no longer wanted and owns at least one
/// source file. This is the durable signal that a passive request was fulfilled outside its own import
/// lifecycle, such as when an album import also satisfies separately requested tracks.
/// </summary>
internal sealed class EfEntityFulfillmentProjection(PrismediaDbContext db) {
    /// <summary>Returns the fulfilled roots within one bounded request or monitoring batch.</summary>
    public async Task<IReadOnlySet<Guid>> ResolveAsync(
        IReadOnlyCollection<Guid> rootIds,
        CancellationToken cancellationToken) {
        var distinctIds = rootIds.Distinct().ToArray();
        if (distinctIds.Length == 0) {
            return new HashSet<Guid>();
        }

        if (db.Database.IsNpgsql()) {
            var sourceRole = EntityFileRole.Source.ToCode();
            return await db.Database.SqlQuery<Guid>($"""
                WITH RECURSIVE requested_roots(root_id, entity_id, path) AS (
                    SELECT entity.id, entity.id, ARRAY[entity.id]
                    FROM entities AS entity
                    WHERE entity.id = ANY ({distinctIds})
                ),
                entity_tree(root_id, entity_id, path) AS (
                    SELECT root_id, entity_id, path
                    FROM requested_roots
                    UNION ALL
                    SELECT tree.root_id, child.id, tree.path || child.id
                    FROM entity_tree AS tree
                    INNER JOIN entities AS child ON child.parent_entity_id = tree.entity_id
                    WHERE NOT child.id = ANY (tree.path)
                )
                SELECT tree.root_id AS "Value"
                FROM entity_tree AS tree
                INNER JOIN entities AS entity ON entity.id = tree.entity_id
                LEFT JOIN entity_files AS file
                    ON file.entity_id = tree.entity_id
                   AND file.role = {sourceRole}
                GROUP BY tree.root_id
                HAVING BOOL_OR(file.id IS NOT NULL)
                   AND NOT BOOL_OR(entity.is_wanted)
                """).ToHashSetAsync(cancellationToken);
        }

        var entities = await db.Entities.AsNoTracking()
            .Select(entity => new { entity.Id, entity.ParentEntityId, entity.IsWanted })
            .ToArrayAsync(cancellationToken);
        var entitiesById = entities.ToDictionary(entity => entity.Id);
        var childrenByParent = entities
            .Where(entity => entity.ParentEntityId != null)
            .GroupBy(entity => entity.ParentEntityId!.Value)
            .ToDictionary(group => group.Key, group => group.Select(entity => entity.Id).ToArray());
        var sourceOwners = await db.EntityFiles.AsNoTracking()
            .Where(file => file.Role == EntityFileRole.Source)
            .Select(file => file.EntityId)
            .ToHashSetAsync(cancellationToken);

        var fulfilled = new HashSet<Guid>();
        foreach (var rootId in distinctIds) {
            if (!entitiesById.ContainsKey(rootId)) {
                continue;
            }

            var pending = new Stack<Guid>();
            var visited = new HashSet<Guid>();
            var hasSource = false;
            var hasWanted = false;
            pending.Push(rootId);
            while (pending.TryPop(out var entityId) && visited.Add(entityId)) {
                var entity = entitiesById[entityId];
                hasWanted |= entity.IsWanted;
                hasSource |= sourceOwners.Contains(entityId);
                if (hasWanted) {
                    break;
                }

                if (childrenByParent.TryGetValue(entityId, out var children)) {
                    foreach (var child in children) {
                        pending.Push(child);
                    }
                }
            }

            if (hasSource && !hasWanted) {
                fulfilled.Add(rootId);
            }
        }

        return fulfilled;
    }
}
