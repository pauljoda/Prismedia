using Microsoft.EntityFrameworkCore;
using Prismedia.Contracts.Entities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Entities.Thumbnails;

/// <summary>
/// Adds the user-facing book category from the book-detail row to book thumbnails. The contributor
/// is discovered with the rest of the thumbnail extensions and performs one batched lookup only
/// when a page contains its declared kind.
/// </summary>
internal sealed class BookTypeThumbnailContributor(PrismediaDbContext db) : IThumbnailContributor {
    private static readonly string BookCode = EntityKind.Book.ToCode();

    /// <inheritdoc />
    public async Task ContributeAsync(
        ThumbnailContributions contributions,
        CancellationToken cancellationToken) {
        var bookIds = contributions.Rows
            .Where(row => row.KindCode == BookCode)
            .Select(row => row.Id)
            .ToArray();
        if (bookIds.Length == 0) {
            return;
        }

        var types = await db.BookDetails.AsNoTracking()
            .Where(detail => bookIds.Contains(detail.EntityId))
            .Select(detail => new { detail.EntityId, detail.BookType })
            .ToArrayAsync(cancellationToken);
        foreach (var type in types) {
            contributions.AddMeta(type.EntityId, EntityThumbnailMetaIcons.Book, FormatBookType(type.BookType));
        }
    }

    private static string? FormatBookType(BookType bookType) =>
        bookType switch {
            BookType.Book => "Book",
            BookType.Novel => "Novel",
            _ => null
        };
}
