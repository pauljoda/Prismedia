using Microsoft.EntityFrameworkCore;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Series;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Media;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Entities.Mappers.Kinds;

internal sealed class VideoSeriesKindMapper(PrismediaDbContext db) : IEntityKindMapper {
    public EntityKind Kind => EntityKind.VideoSeries;

    public async Task<Entity> ConstructAsync(EntityRow row, CancellationToken cancellationToken) {
        var detail = await db.VideoSeriesDetails.AsNoTracking()
            .FirstOrDefaultAsync(d => d.EntityId == row.Id, cancellationToken);
        return new VideoSeries(row.Id, row.Title, detail?.Status);
    }

    public async Task PersistDetailAsync(Entity entity, CancellationToken cancellationToken) {
        if (entity is not VideoSeries series) {
            return;
        }

        var row = await db.VideoSeriesDetails.FindAsync([entity.Id], cancellationToken)
            ?? Track(new VideoSeriesDetailRow { EntityId = entity.Id });
        row.Status = series.Status;
    }

    public IEntityCard ProjectDetail(
        Entity entity,
        EntityCard card,
        IReadOnlyList<EntityCreditMetadata> creditMetadata) =>
        new VideoSeriesDetail {
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

    private VideoSeriesDetailRow Track(VideoSeriesDetailRow row) {
        db.VideoSeriesDetails.Add(row);
        return row;
    }
}
