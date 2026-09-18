using Microsoft.EntityFrameworkCore;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Media.Persistence;

/// <summary>External owners retain catalog identity and user history across missing mounts, removals, and upgrades.</summary>
internal static class ExternalLibraryEntityRetention {
    /// <summary>
    /// Returns only candidates whose ancestor chain or subtree contains an Entity directly owned
    /// by an external library mount. This preserves externally owned trees and their native
    /// structural containers without loading unrelated external catalog rows.
    /// </summary>
    internal static async Task<HashSet<Guid>> ListProtectedCandidateIdsAsync(
        PrismediaDbContext db,
        IReadOnlyCollection<Guid> candidateIds,
        CancellationToken token) {
        var candidates = candidateIds.Distinct().ToArray();
        if (candidates.Length == 0) {
            return [];
        }

        if (db.Database.IsNpgsql()) {
            return await db.Database.SqlQuery<Guid>($"""
                WITH RECURSIVE candidates(candidate_id) AS (
                    SELECT unnest({candidates})
                ),
                ancestors(candidate_id, entity_id, path) AS (
                    SELECT candidate_id, candidate_id, ARRAY[candidate_id]
                    FROM candidates
                    UNION ALL
                    SELECT ancestor.candidate_id, parent.id, ancestor.path || parent.id
                    FROM ancestors AS ancestor
                    INNER JOIN entities AS current_entity ON current_entity.id = ancestor.entity_id
                    INNER JOIN entities AS parent ON parent.id = current_entity.parent_entity_id
                    WHERE NOT parent.id = ANY (ancestor.path)
                ),
                descendants(candidate_id, entity_id, path) AS (
                    SELECT candidate_id, candidate_id, ARRAY[candidate_id]
                    FROM candidates
                    UNION ALL
                    SELECT descendant.candidate_id, child.id, descendant.path || child.id
                    FROM descendants AS descendant
                    INNER JOIN entities AS child ON child.parent_entity_id = descendant.entity_id
                    WHERE NOT child.id = ANY (descendant.path)
                ),
                lineage(candidate_id, entity_id) AS (
                    SELECT candidate_id, entity_id FROM ancestors
                    UNION
                    SELECT candidate_id, entity_id FROM descendants
                )
                SELECT DISTINCT lineage.candidate_id AS "Value"
                FROM lineage
                INNER JOIN entity_library_roots AS link ON link.entity_id = lineage.entity_id
                INNER JOIN external_library_mounts AS mount ON mount.library_root_id = link.library_root_id
                """).ToHashSetAsync(token);
        }

        var protectedIds = new HashSet<Guid>();
        foreach (var candidateId in candidates) {
            if (await HasExternalOwnerInLineageAsync(db, candidateId, token)) {
                protectedIds.Add(candidateId);
            }
        }

        return protectedIds;
    }

    private static async Task<bool> HasExternalOwnerInLineageAsync(
        PrismediaDbContext db,
        Guid candidateId,
        CancellationToken token) {
        if (await ContainsExternalOwnerAsync(db, [candidateId], token)) {
            return true;
        }

        var seen = new HashSet<Guid> { candidateId };
        var frontier = new[] { candidateId };
        while (frontier.Length > 0) {
            var parents = await db.Entities.AsNoTracking()
                .Where(entity => frontier.Contains(entity.Id) && entity.ParentEntityId != null)
                .Select(entity => entity.ParentEntityId!.Value)
                .ToArrayAsync(token);
            frontier = parents.Where(seen.Add).ToArray();
            if (await ContainsExternalOwnerAsync(db, frontier, token)) {
                return true;
            }
        }

        seen.Clear();
        seen.Add(candidateId);
        frontier = [candidateId];
        while (frontier.Length > 0) {
            var children = await db.Entities.AsNoTracking()
                .Where(entity => entity.ParentEntityId != null && frontier.Contains(entity.ParentEntityId.Value))
                .Select(entity => entity.Id)
                .ToArrayAsync(token);
            frontier = children.Where(seen.Add).ToArray();
            if (await ContainsExternalOwnerAsync(db, frontier, token)) {
                return true;
            }
        }

        return false;
    }

    private static Task<bool> ContainsExternalOwnerAsync(
        PrismediaDbContext db,
        IReadOnlyCollection<Guid> entityIds,
        CancellationToken token) {
        if (entityIds.Count == 0) {
            return Task.FromResult(false);
        }

        return db.EntityLibraryRoots.AsNoTracking().AnyAsync(
            link => entityIds.Contains(link.EntityId)
                && db.ExternalLibraryMounts.Any(mount => mount.LibraryRootId == link.LibraryRootId),
            token);
    }
}
