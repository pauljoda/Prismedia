using Microsoft.EntityFrameworkCore;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Taxonomy;
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

    public async Task PersistDetailAsync(Entity entity, CancellationToken cancellationToken) {
        if (entity is not Person person) {
            return;
        }

        var row = await db.PersonDetails.FindAsync([entity.Id], cancellationToken)
            ?? Track(new PersonDetailRow { EntityId = entity.Id });
        row.Disambiguation = person.Disambiguation;
        row.Gender = person.Gender;
        row.Country = person.Country;
        row.Ethnicity = person.Ethnicity;
        row.EyeColor = person.EyeColor;
        row.HairColor = person.HairColor;
        row.Height = person.Height;
        row.Weight = person.Weight;
        row.Measurements = person.Measurements;
        row.Tattoos = person.Tattoos;
        row.Piercings = person.Piercings;
    }

    public IEntityCard ProjectDetail(
        Entity entity,
        EntityCard card,
        IReadOnlyList<EntityCreditMetadata> creditMetadata) =>
        entity is Person person
            ? new PersonDetail {
                Id = card.Id,
                Kind = card.Kind,
                Title = card.Title,
                ParentEntityId = card.ParentEntityId,
                SortOrder = card.SortOrder,
                HasSourceMedia = card.HasSourceMedia,
                Capabilities = card.Capabilities,
                ChildrenByKind = card.ChildrenByKind,
                Relationships = card.Relationships,
                Disambiguation = person.Disambiguation,
                Gender = person.Gender,
                Country = person.Country,
                Ethnicity = person.Ethnicity,
                EyeColor = person.EyeColor,
                HairColor = person.HairColor,
                Height = person.Height,
                Weight = person.Weight,
                Measurements = person.Measurements,
                Tattoos = person.Tattoos,
                Piercings = person.Piercings,
            }
            : card;

    private PersonDetailRow Track(PersonDetailRow row) {
        db.PersonDetails.Add(row);
        return row;
    }
}
