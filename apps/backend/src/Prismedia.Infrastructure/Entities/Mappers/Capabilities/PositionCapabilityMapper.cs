using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Entities.Mappers.Capabilities;

internal sealed class PositionCapabilityMapper(PrismediaDbContext db) : IEntityCapabilityMapper {
    public async Task HydrateAsync(Entity entity, CancellationToken cancellationToken) {
        var rows = await db.EntityPositions.AsNoTracking()
            .Where(r => r.EntityId == entity.Id)
            .OrderBy(r => r.Code)
            .ToArrayAsync(cancellationToken);
        if (rows.Length == 0) {
            return;
        }

        entity.RemoveCapability<CapabilityPosition>();
        entity.AddCapability(new CapabilityPosition(rows.Select(r =>
            new CapabilityPosition.Item(r.Code, r.Value, r.Label)).ToArray()));
    }

}
