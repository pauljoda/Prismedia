using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Entities.Mappers.Capabilities;

internal sealed class StatsCapabilityMapper(PrismediaDbContext db) : IEntityCapabilityMapper {
    public async Task HydrateAsync(Entity entity, CancellationToken cancellationToken) {
        var rows = await db.EntityStats.AsNoTracking()
            .Where(r => r.EntityId == entity.Id)
            .OrderBy(r => r.Code)
            .ToArrayAsync(cancellationToken);
        if (rows.Length == 0) {
            return;
        }

        entity.RemoveCapability<CapabilityStats>();
        entity.AddCapability(new CapabilityStats(
            rows.Select(r => new CapabilityStats.Item(r.Code, r.Value)).ToArray()));
    }

    public Task ClearAsync(Entity entity, CancellationToken cancellationToken) {
        db.EntityStats.RemoveRange(db.EntityStats.Where(r => r.EntityId == entity.Id));
        return Task.CompletedTask;
    }

    public Task PersistAsync(Entity entity, CancellationToken cancellationToken) {
        if (entity.Stats is not { } stats) {
            return Task.CompletedTask;
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var stat in stats.Items) {
            db.EntityStats.Add(new EntityStatRow {
                EntityId = entity.Id,
                Code = stat.Code,
                Value = stat.Value,
                UpdatedAt = now,
            });
        }

        return Task.CompletedTask;
    }
}
