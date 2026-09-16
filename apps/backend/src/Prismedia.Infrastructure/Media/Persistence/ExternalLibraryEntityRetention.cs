using Microsoft.EntityFrameworkCore;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Media.Persistence;

/// <summary>External owners retain catalog identity and user history across missing mounts, removals, and upgrades.</summary>
internal static class ExternalLibraryEntityRetention {
    internal static async Task<HashSet<Guid>> ListProtectedIdsAsync(PrismediaDbContext db, CancellationToken token) {
        var retained = (await db.EntityLibraryRoots.AsNoTracking()
            .Where(link => db.ExternalLibraryMounts.Any(mount => mount.LibraryRootId == link.LibraryRootId))
            .Select(link => link.EntityId).ToArrayAsync(token)).ToHashSet();
        var frontier = retained.ToArray();
        while (frontier.Length > 0) {
            var children = await db.Entities.AsNoTracking().Where(entity => entity.ParentEntityId != null && frontier.Contains(entity.ParentEntityId.Value))
                .Select(entity => entity.Id).ToArrayAsync(token);
            frontier = children.Where(retained.Add).ToArray();
        }
        frontier = retained.ToArray();
        while (frontier.Length > 0) {
            var parents = await db.Entities.AsNoTracking().Where(entity => frontier.Contains(entity.Id) && entity.ParentEntityId != null)
                .Select(entity => entity.ParentEntityId!.Value).ToArrayAsync(token);
            frontier = parents.Where(retained.Add).ToArray();
        }
        return retained;
    }
}
