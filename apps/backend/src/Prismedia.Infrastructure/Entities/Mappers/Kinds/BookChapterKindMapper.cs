using Prismedia.Domain.Entities;
using Prismedia.Domain.Media;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Entities.Mappers.Kinds;

internal sealed class BookChapterKindMapper : IEntityKindMapper {
    public BookChapterKindMapper(PrismediaDbContext db) {
        ArgumentNullException.ThrowIfNull(db);
    }

    public EntityKind Kind => EntityKind.BookChapter;

    public Task<Entity> ConstructAsync(EntityRow row, CancellationToken cancellationToken) {
        return Task.FromResult<Entity>(new BookChapter(
            row.Id,
            row.Title,
            parentEntityId: row.ParentEntityId,
            sortOrder: row.SortOrder));
    }

}
