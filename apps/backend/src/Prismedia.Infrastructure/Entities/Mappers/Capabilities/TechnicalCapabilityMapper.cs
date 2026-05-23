using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Entities.Mappers.Capabilities;

internal sealed class TechnicalCapabilityMapper(PrismediaDbContext db) : IEntityCapabilityMapper {
    public async Task HydrateAsync(Entity entity, CancellationToken cancellationToken) {
        var row = await db.EntityTechnical.AsNoTracking()
            .FirstOrDefaultAsync(r => r.EntityId == entity.Id, cancellationToken);
        if (row is null) {
            return;
        }

        var capability = new CapabilityTechnical();
        capability.Apply(
            row.DurationSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : null,
            row.Width,
            row.Height,
            row.FrameRate,
            row.BitRate,
            row.SampleRate,
            row.Channels,
            row.Codec,
            row.Container,
            row.Format);

        entity.RemoveCapability<CapabilityTechnical>();
        entity.AddCapability(capability);
    }

    public Task ClearAsync(Entity entity, CancellationToken cancellationToken) {
        db.EntityTechnical.RemoveRange(db.EntityTechnical.Where(r => r.EntityId == entity.Id));
        return Task.CompletedTask;
    }

    public Task PersistAsync(Entity entity, CancellationToken cancellationToken) {
        if (entity.Technical is { } technical && HasData(technical)) {
            db.EntityTechnical.Add(new EntityTechnicalRow {
                EntityId = entity.Id,
                DurationSeconds = technical.Duration?.TotalSeconds,
                Width = technical.Width,
                Height = technical.Height,
                FrameRate = technical.FrameRate,
                BitRate = technical.BitRate,
                SampleRate = technical.SampleRate,
                Channels = technical.Channels,
                Codec = technical.Codec,
                Container = technical.Container,
                Format = technical.Format,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
        }

        return Task.CompletedTask;
    }

    private static bool HasData(CapabilityTechnical technical) =>
        technical.Duration is not null || technical.Width is not null || technical.Height is not null ||
        technical.FrameRate is not null || technical.BitRate is not null || technical.SampleRate is not null ||
        technical.Channels is not null || technical.Codec is not null || technical.Container is not null ||
        technical.Format is not null;
}
