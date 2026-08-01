using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Entities.Mappers.Capabilities;

internal sealed class ClassificationCapabilityMapper(PrismediaDbContext db) : IEntityCapabilityMapper {
    public async Task HydrateAsync(Entity entity, CancellationToken cancellationToken) {
        var row = await db.EntityClassifications.AsNoTracking()
            .FirstOrDefaultAsync(r => r.EntityId == entity.Id, cancellationToken);
        if (row is null) {
            return;
        }

        entity.RemoveCapability<CapabilityClassification>();
        entity.AddCapability(new CapabilityClassification(row.Value, row.System));
    }

}
