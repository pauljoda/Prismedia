using Microsoft.EntityFrameworkCore;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Media;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Media;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Entities.Mappers.Kinds;

internal sealed class BookKindMapper(PrismediaDbContext db) : IEntityKindMapper {
    public EntityKind Kind => EntityKind.Book;

    public async Task<Entity> ConstructAsync(EntityRow row, CancellationToken cancellationToken) {
        var detail = await db.BookDetails.AsNoTracking()
            .FirstOrDefaultAsync(d => d.EntityId == row.Id, cancellationToken);
        return new Book(
            row.Id,
            row.Title,
            detail?.BookType ?? BookType.Book,
            detail?.CoverPageEntityId,
            detail?.Format ?? BookFormat.ImageArchive,
            parentEntityId: row.ParentEntityId,
            sortOrder: row.SortOrder);
    }

    public async Task PersistDetailAsync(Entity entity, CancellationToken cancellationToken) {
        if (entity is not Book book) {
            return;
        }

        var row = await db.BookDetails.FindAsync([entity.Id], cancellationToken)
            ?? Track(new BookDetailRow { EntityId = entity.Id });
        row.BookType = book.BookType;
        row.Format = book.Format;
        row.CoverPageEntityId = book.CoverPageId;
    }

    public IEntityCard ProjectDetail(
        Entity entity,
        EntityCard card,
        IReadOnlyList<EntityCreditMetadata> creditMetadata) =>
        entity is Book book
            ? new BookDetail {
                Id = card.Id,
                Kind = card.Kind,
                Title = card.Title,
                ParentEntityId = card.ParentEntityId,
                SortOrder = card.SortOrder,
                HasSourceMedia = card.HasSourceMedia,
                Capabilities = card.Capabilities,
                ChildrenByKind = card.ChildrenByKind,
                Relationships = card.Relationships,
                BookType = book.BookType,
                Format = book.Format,
                CoverPageId = book.CoverPageId,
            }
            : card;

    private BookDetailRow Track(BookDetailRow row) {
        db.BookDetails.Add(row);
        return row;
    }
}
