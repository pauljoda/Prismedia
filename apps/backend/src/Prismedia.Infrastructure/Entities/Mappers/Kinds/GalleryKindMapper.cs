using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Media;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Entities.Mappers.Kinds;

internal sealed class GalleryKindMapper(PrismediaDbContext db) : IEntityKindMapper {
    public EntityKind Kind => EntityKind.Gallery;

    public async Task<Entity> ConstructAsync(EntityRow row, CancellationToken cancellationToken) {
        var detail = await db.GalleryDetails.AsNoTracking()
            .FirstOrDefaultAsync(d => d.EntityId == row.Id, cancellationToken);
        return new Gallery(
            row.Id,
            row.Title,
            detail?.GalleryType ?? GalleryType.Virtual,
            detail?.CoverImageEntityId);
    }

    public async Task PersistDetailAsync(Entity entity, CancellationToken cancellationToken) {
        if (entity is not Gallery gallery) {
            return;
        }

        var row = await db.GalleryDetails.FindAsync([entity.Id], cancellationToken)
            ?? Track(new GalleryDetailRow { EntityId = entity.Id });
        row.GalleryType = gallery.GalleryType;
        row.CoverImageEntityId = gallery.CoverImageId;
    }

    private GalleryDetailRow Track(GalleryDetailRow row) {
        db.GalleryDetails.Add(row);
        return row;
    }
}
