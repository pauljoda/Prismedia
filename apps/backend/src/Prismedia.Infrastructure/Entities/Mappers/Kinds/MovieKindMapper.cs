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

}
