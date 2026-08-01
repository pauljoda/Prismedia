using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Taxonomy;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Entities.Mappers.Kinds;

internal sealed class PersonKindMapper(PrismediaDbContext db) : IEntityKindMapper {
    public EntityKind Kind => EntityKind.Person;

    public async Task<Entity> ConstructAsync(EntityRow row, CancellationToken cancellationToken) {
        var detail = await db.PersonDetails.AsNoTracking()
            .FirstOrDefaultAsync(d => d.EntityId == row.Id, cancellationToken);
        return new Person(
            row.Id,
            row.Title,
            detail?.Disambiguation,
            detail?.Gender,
            detail?.Country,
            detail?.Ethnicity,
            detail?.EyeColor,
            detail?.HairColor,
            detail?.Height,
            detail?.Weight,
            detail?.Measurements,
            detail?.Tattoos,
            detail?.Piercings);
    }

}
