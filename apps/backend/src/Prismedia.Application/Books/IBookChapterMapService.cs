namespace Prismedia.Application.Books;

/// <summary>Outcome of one chapter-map refresh pass.</summary>
/// <param name="ContentsRefreshed">Whether the persisted readable chapters were re-projected.</param>
/// <param name="AutoMappingsReplaced">Whether the automatic mapping rows changed.</param>
public sealed record BookChapterMapRefreshResult(bool ContentsRefreshed, bool AutoMappingsReplaced);

/// <summary>One book whose persisted chapter map no longer matches its inputs.</summary>
/// <param name="BookId">Identifier of the Book entity.</param>
/// <param name="Title">The book's display title, for job labels.</param>
public sealed record StaleBookChapterMap(Guid BookId, string Title);

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

    /// <summary>
    /// Finds this root's books whose persisted chapter state is stale. Backs the unchanged-scan
    /// integrity hook: catalog-only edits (metadata retitles, first-deploy backfill) change no
    /// files, so the snapshot fast path skips the detailed scan and only this sweep would notice.
    /// </summary>
    /// <param name="rootPath">Absolute path of the library root being verified.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Books that need a chapter-map refresh, with titles for job labels.</returns>
    Task<IReadOnlyList<StaleBookChapterMap>> ListStaleForRootAsync(
        string rootPath,
        CancellationToken cancellationToken);

    /// <summary>
    /// Finds every Book whose persisted automatic chapter map was computed by an older matcher
    /// version. Their automatic pairs are not exact evidence until the map is recomputed, so the
    /// startup backfill refreshes them once. The query reads signatures only and parses nothing.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Books whose automatic map predates the current matcher, with titles for job labels.</returns>
    Task<IReadOnlyList<StaleBookChapterMap>> ListOutdatedMatcherMapsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Finds audiobook tracks owned by a Book whose file was probed before the probe recorded the
    /// exact facts the chapter map depends on (title and track-number tags, and which embedded chapters
    /// are untitled). The startup backfill probes each once more.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The tracks, with titles for job labels.</returns>
    Task<IReadOnlyList<AudiobookTrackAwaitingProbe>> ListTracksAwaitingProbeFactsAsync(CancellationToken cancellationToken);
}

/// <summary>One probed audiobook track whose file facts predate the current probe.</summary>
/// <param name="TrackId">Identifier of the audio-track Entity.</param>
/// <param name="Title">The track's display title, for job labels.</param>
public sealed record AudiobookTrackAwaitingProbe(Guid TrackId, string Title);
