namespace Prismedia.Contracts.Books;

/// <summary>
/// One navigable EPUB table-of-contents entry with its position in the book's reading order.
/// </summary>
/// <param name="Id">Stable entry identifier, equal to the EPUB navigation target.</param>
/// <param name="Title">Human-readable chapter or section title.</param>
/// <param name="Location">EPUB-relative navigation target that can be passed to the reader.</param>
/// <param name="Depth">Zero-based nesting depth in the EPUB table of contents.</param>
/// <param name="Order">Zero-based display order after flattening nested navigation.</param>
/// <param name="SectionIndex">Zero-based reading-order section containing this entry, when resolvable.</param>
/// <param name="StartFraction">Normalized whole-book start position, when section sizes are available.</param>
/// <param name="EndFraction">Normalized whole-book end position, when section sizes are available.</param>
/// <param name="PageCount">Readable page count for chapter-entity books; <c>null</c> for EPUB entries.</param>
public sealed record BookContentsEntry(
    string Id,
    string Title,
    string Location,
    int Depth,
    int Order,
    int? SectionIndex,
    double? StartFraction,
    double? EndFraction,
    int? PageCount = null);

/// <summary>
/// Compact readable-chapter metadata for one Book: the persisted EPUB table of contents for
/// single-file books, or chapter-entity summaries (with page counts) for paged books.
/// </summary>
/// <param name="Items">Flattened readable chapters in display order.</param>
public sealed record BookContentsResponse(IReadOnlyList<BookContentsEntry> Items);
