using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Movies;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Media;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Entities.Mappers.Kinds;

internal sealed class MovieKindMapper : IEntityKindMapper {
    public MovieKindMapper(PrismediaDbContext db) {
        ArgumentNullException.ThrowIfNull(db);
    }

    public EntityKind Kind => EntityKind.Movie;

    public Task<Entity> ConstructAsync(EntityRow row, CancellationToken cancellationToken) =>
        Task.FromResult<Entity>(new Movie(row.Id, row.Title));

    public Task PersistDetailAsync(Entity entity, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public IEntityCard ProjectDetail(
        Entity entity,
        EntityCard card,
        IReadOnlyList<EntityCreditMetadata> creditMetadata) =>
        new MovieDetail {
            Id = card.Id,
            Kind = card.Kind,
            Title = card.Title,
            ParentEntityId = card.ParentEntityId,
            SortOrder = card.SortOrder,
            HasSourceMedia = card.HasSourceMedia,
            Capabilities = card.Capabilities,
            ChildrenByKind = card.ChildrenByKind,
            Relationships = card.Relationships,
            CreditMetadata = creditMetadata,
        };
}
