namespace Prismedia.Application.Books;

/// <summary>Outcome of one chapter-map refresh pass.</summary>
/// <param name="ContentsRefreshed">Whether the persisted readable chapters were re-projected.</param>
/// <param name="AutoMappingsReplaced">Whether the automatic mapping rows changed.</param>
public sealed record BookChapterMapRefreshResult(bool ContentsRefreshed, bool AutoMappingsReplaced);

/// <summary>
/// Owns the persisted readable-chapter projection and the automatic audiobook chapter map for one
/// Book. The scan pipeline calls <see cref="IsRefreshNeededAsync"/> to decide whether to enqueue
/// work, and the map-book-chapters job calls <see cref="RefreshAsync"/> to bring both current.
/// Manual mapping rows are never touched here beyond being respected as consumed pairs.
/// </summary>
public interface IBookChapterMapService {
    /// <summary>
    /// Cheap staleness probe used by scans: compares the stored source and mapping signatures
    /// against the book's current readable file and chapter/track inputs without parsing anything.
    /// </summary>
    /// <param name="bookId">Identifier of the Book entity.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns><c>true</c> when a refresh job would change persisted state.</returns>
    Task<bool> IsRefreshNeededAsync(Guid bookId, CancellationToken cancellationToken);

    /// <summary>
    /// Re-projects the readable chapter list when the source file changed and recomputes the
    /// automatic chapter map when any matching input changed. No-ops via signatures otherwise.
    /// </summary>
    /// <param name="bookId">Identifier of the Book entity.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>What actually changed, for job logging.</returns>
    Task<BookChapterMapRefreshResult> RefreshAsync(Guid bookId, CancellationToken cancellationToken);
}
