using Microsoft.EntityFrameworkCore;
using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Integrations;

/// <summary>
/// Maps tracked source entities to the canonical work that owns an independently fulfilled rendition. A rendition
/// delivered as the work itself is owned by that work; one delivered in parts is owned by the parts' parent work.
/// </summary>
internal sealed class ManagedReservationScopeResolver(PrismediaDbContext db) {
    internal async Task<Guid[]> ResolveAsync(
        IReadOnlyList<Guid> sourceEntityIds, ManagedItemInput item, CancellationToken token) {
        var ids = sourceEntityIds.Distinct().ToArray();
        if (!ManagedFulfillmentPolicy.Supports(item.EntityKind) || !ManagedFulfillmentPolicy.For(item.EntityKind).RequiresRendition) {
            return ids;
        }

        var policy = ManagedFulfillmentPolicy.For(item.EntityKind);
        if (!policy.AcceptsRendition(item.BookRendition) || ids.Length == 0)
            throw new ArgumentException("A connected holding with renditions requires one exact rendition and local work.");
        var target = policy.TargetFor(item.BookRendition);
        var targetCode = target.Kind.ToCode();
        var workCode = item.EntityKind.ToCode();
        var sources = await db.Entities.AsNoTracking().Where(entity => ids.Contains(entity.Id))
            .Select(entity => new { entity.Id, entity.KindCode, entity.ParentEntityId }).ToArrayAsync(token);
        if (sources.Length != ids.Length) throw new ArgumentException("A selected source no longer exists.");
        if (target.Shape.IsItem) {
            if (sources.Length != 1 || sources[0].KindCode != targetCode)
                throw new ArgumentException("Select the one file that belongs to this work.");
            return [sources[0].Id];
        }

        if (sources.Any(source => source.KindCode != targetCode || source.ParentEntityId is null))
            throw new ArgumentException("Select the parts beneath one work.");
        var workIds = sources.Select(source => source.ParentEntityId!.Value).Distinct().ToArray();
        if (workIds.Length != 1 || !await db.Entities.AsNoTracking().AnyAsync(entity =>
                entity.Id == workIds[0] && entity.KindCode == workCode, token))
            throw new ArgumentException("The selected parts no longer belong to one work.");
        return workIds;
    }
}
