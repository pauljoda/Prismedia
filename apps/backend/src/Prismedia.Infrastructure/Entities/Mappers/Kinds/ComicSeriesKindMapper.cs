using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Media;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Entities.Mappers.Kinds;

internal sealed class ComicSeriesKindMapper(PrismediaDbContext db) : IEntityKindMapper {
    public EntityKind Kind => EntityKind.ComicSeries;

    public async Task<Entity> ConstructAsync(EntityRow row, CancellationToken cancellationToken) {
        var detail = await db.ComicSeriesDetails.AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.EntityId == row.Id, cancellationToken);
        return new ComicSeries(row.Id, row.Title, detail?.Status);
    }
}
