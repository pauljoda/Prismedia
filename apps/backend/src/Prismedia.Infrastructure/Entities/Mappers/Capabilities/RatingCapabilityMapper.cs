using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Entities.Mappers.Capabilities;

internal sealed class RatingCapabilityMapper(PrismediaDbContext db) : IEntityCapabilityMapper {
    public async Task HydrateAsync(Entity entity, CancellationToken cancellationToken) {
        var row = await db.EntityRatings.AsNoTracking()
            .FirstOrDefaultAsync(r => r.EntityId == entity.Id, cancellationToken);
        if (row is not null && entity.Rating is not null) {
            entity.Rating.Rate(row.Value);
        }
    }

    public Task ClearAsync(Entity entity, CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task PersistAsync(Entity entity, CancellationToken cancellationToken) {
        var existing = await db.EntityRatings.FindAsync([entity.Id], cancellationToken);
        var now = DateTimeOffset.UtcNow;

        if (entity.Rating?.Value is { } value) {
            if (existing is null) {
                db.EntityRatings.Add(new EntityRatingRow {
                    EntityId = entity.Id,
                    Value = value,
                    UpdatedAt = now,
                });
            } else {
                existing.Value = value;
                existing.UpdatedAt = now;
            }
        } else if (existing is not null) {
            db.EntityRatings.Remove(existing);
        }
    }
}
