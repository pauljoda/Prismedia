using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

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

    public Task ClearAsync(Entity entity, CancellationToken cancellationToken) {
        db.EntityPositions.RemoveRange(db.EntityPositions.Where(r => r.EntityId == entity.Id));
        return Task.CompletedTask;
    }

    public Task PersistAsync(Entity entity, CancellationToken cancellationToken) {
        if (entity.Position is not { } position) {
            return Task.CompletedTask;
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var item in position.Items) {
            db.EntityPositions.Add(new EntityPositionRow {
                EntityId = entity.Id,
                Code = item.Code,
                Value = item.Value,
                Label = item.Label,
                UpdatedAt = now,
            });
        }

        return Task.CompletedTask;
    }
}
