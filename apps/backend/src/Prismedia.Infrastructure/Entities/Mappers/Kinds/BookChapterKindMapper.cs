using Microsoft.EntityFrameworkCore;
using Prismedia.Contracts.Entities;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Media;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Entities.Mappers.Kinds;

internal sealed class BookChapterKindMapper(PrismediaDbContext db) : IEntityKindMapper {
    public EntityKind Kind => EntityKind.BookChapter;

    public async Task<Entity> ConstructAsync(EntityRow row, CancellationToken cancellationToken) {
        var detail = await db.BookChapterDetails.AsNoTracking()
            .FirstOrDefaultAsync(d => d.EntityId == row.Id, cancellationToken);
        return new BookChapter(
            row.Id,
            row.Title,
            detail?.CoverPageEntityId,
            parentEntityId: row.ParentEntityId,
            sortOrder: row.SortOrder);
    }

    public async Task PersistDetailAsync(Entity entity, CancellationToken cancellationToken) {
        if (entity is not BookChapter chapter) {
            return;
        }

        var row = await db.BookChapterDetails.FindAsync([entity.Id], cancellationToken)
            ?? Track(new BookChapterDetailRow { EntityId = entity.Id });
        row.CoverPageEntityId = chapter.CoverPageId;
    }

    public IEntityCard ProjectDetail(
        Entity entity,
        EntityCard card,
        IReadOnlyList<EntityCreditMetadata> creditMetadata) =>
        card;

    private BookChapterDetailRow Track(BookChapterDetailRow row) {
        db.BookChapterDetails.Add(row);
        return row;
    }
}
