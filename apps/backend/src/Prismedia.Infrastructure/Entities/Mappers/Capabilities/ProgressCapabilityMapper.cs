using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Entities.Mappers.Capabilities;

internal sealed class ProgressCapabilityMapper(PrismediaDbContext db) : IEntityCapabilityMapper {
    public async Task HydrateAsync(Entity entity, CancellationToken cancellationToken) {
        var row = await db.EntityProgress.AsNoTracking()
            .FirstOrDefaultAsync(r => r.EntityId == entity.Id, cancellationToken);
        if (row is null) {
            return;
        }

        entity.RemoveCapability<CapabilityProgress>();
        // Tolerant decode: rows written before the typed vocabulary may carry legacy values
        // (e.g. mode "paginated" from earlier EPUB saves); those hydrate to the safe defaults
        // instead of failing the whole entity read.
        entity.AddCapability(new CapabilityProgress(
            row.CurrentEntityId,
            row.Unit.TryDecodeAs<ProgressUnit>(out var unit) ? unit : ProgressUnit.Item,
            row.Index,
            row.Total,
            row.Mode is not null && row.Mode.TryDecodeAs<ReaderMode>(out var mode) ? mode : null,
            row.CompletedAt,
            row.UpdatedAt,
            row.Location));
    }

    public Task ClearAsync(Entity entity, CancellationToken cancellationToken) {
        db.EntityProgress.RemoveRange(db.EntityProgress.Where(r => r.EntityId == entity.Id));
        return Task.CompletedTask;
    }

    public Task PersistAsync(Entity entity, CancellationToken cancellationToken) {
        if (entity.Progress is { } progress &&
            (progress.UpdatedAt is not null || progress.CurrentEntityId is not null || progress.Index != 0 || progress.Total != 0 || progress.Location is not null)) {
            db.EntityProgress.Add(new EntityProgressRow {
                EntityId = entity.Id,
                CurrentEntityId = progress.CurrentEntityId,
                Unit = progress.Unit.ToCode(),
                Index = progress.Index,
                Total = progress.Total,
                Mode = progress.Mode?.ToCode(),
                Location = progress.Location,
                CompletedAt = progress.CompletedAt,
                UpdatedAt = progress.UpdatedAt ?? DateTimeOffset.UtcNow,
            });
        }

        return Task.CompletedTask;
    }
}
