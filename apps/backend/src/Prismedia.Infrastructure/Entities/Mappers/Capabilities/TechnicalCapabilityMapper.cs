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

}
