using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Media.Persistence;

/// <summary>
/// Removes inactive derived Entity shells according to their discovered kind definitions. The
/// caller supplies its save boundary so scan mutations can retain lifecycle-lease validation.
/// </summary>
internal static class DerivedEntityContainerPruner {
    private static readonly string[] ContainerCodes = EntityKindRegistry.All
        .Where(definition => definition.PrunesWhenEmpty)
        .Select(definition => definition.Code)
        .ToArray();

    /// <summary>Prunes empty shells until parent chains reach a fixed point.</summary>
    internal static async Task<int> PruneAsync(
        PrismediaDbContext db,
        Func<CancellationToken, Task> saveChanges,
        CancellationToken cancellationToken) {
        var removed = 0;
        while (true) {
            var orphanContainers = await db.Entities
                .Where(entity => ContainerCodes.Contains(entity.KindCode)
                    && !entity.IsWanted
                    && !db.Monitors.Any(monitor =>
                        monitor.EntityId == entity.Id && monitor.Status == MonitorStatus.Active)
                    && !db.Entities.Any(child => child.ParentEntityId == entity.Id))
                .ToArrayAsync(cancellationToken);
            if (orphanContainers.Length == 0) {
                return removed;
            }

            db.Entities.RemoveRange(orphanContainers);
            await saveChanges(cancellationToken);
            removed += orphanContainers.Length;
        }
    }
}
