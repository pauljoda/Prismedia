using Microsoft.EntityFrameworkCore;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Videos;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Media;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Entities.Mappers.Kinds;

internal sealed class VideoKindMapper(PrismediaDbContext db) : IEntityKindMapper {
    public EntityKind Kind => EntityKind.Video;

    public async Task<Entity> ConstructAsync(EntityRow row, CancellationToken cancellationToken) {
        var detail = await db.VideoDetails.AsNoTracking()
            .FirstOrDefaultAsync(d => d.EntityId == row.Id, cancellationToken);
        return new Video(row.Id, row.Title, detail?.SubtitlesExtractedAt);
    }

    public async Task PersistDetailAsync(Entity entity, CancellationToken cancellationToken) {
        if (entity is not Video video) {
            return;
        }

        var row = await db.VideoDetails.FindAsync([entity.Id], cancellationToken)
            ?? Track(new VideoDetailRow { EntityId = entity.Id });
        row.SubtitlesExtractedAt = video.SubtitlesExtractedAt;
    }

    public IEntityCard ProjectDetail(
        Entity entity,
        EntityCard card,
        IReadOnlyList<EntityCreditMetadata> creditMetadata) =>
        new VideoDetail {
            Id = card.Id,
            Kind = card.Kind,
            Title = card.Title,
            ParentEntityId = card.ParentEntityId,
            SortOrder = card.SortOrder,
            Capabilities = card.Capabilities,
            ChildrenByKind = card.ChildrenByKind,
            Relationships = card.Relationships,
            CreditMetadata = creditMetadata,
            SubtitlesExtractedAt = (entity as Video)?.SubtitlesExtractedAt,
        };

    private VideoDetailRow Track(VideoDetailRow row) {
        db.VideoDetails.Add(row);
        return row;
    }
}
