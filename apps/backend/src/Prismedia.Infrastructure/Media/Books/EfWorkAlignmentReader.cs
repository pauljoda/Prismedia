using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Books;
using Prismedia.Contracts.Books;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Media.Books;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Media.Books;

/// <summary>
/// Loads a Book's reading/listening alignment from the same projections the contents and
/// chapter-mapping routes use: the readable chapter list, the audio rendition with its source chapter
/// markers and tags, and the persisted chapter pairings. Automatic pairs computed by an older matcher
/// are left out until the chapter map is recomputed, so they are never treated as exact evidence.
/// </summary>
internal sealed class EfWorkAlignmentReader(
    PrismediaDbContext db,
    IBookContentsService contents) : IWorkAlignmentReader {
    #region Actions - Loading

    /// <inheritdoc />
    public async Task<WorkAlignment?> LoadAsync(Guid workId, CancellationToken cancellationToken) {
        if (!await db.Entities.AsNoTracking().AnyAsync(row => row.Id == workId, cancellationToken)) {
            return null;
        }

        var hasReadableSource = await db.EntityFiles.AsNoTracking().AnyAsync(
            file => file.EntityId == workId && file.Role == EntityFileRole.Source,
            cancellationToken);
        var readable = (await LoadContentsAsync(workId, cancellationToken)).Select(ToWindow).ToArray();
        var audio = await BookAudioChapterProjection.LoadAsync(db, workId, cancellationToken);
        var pairings = (await LoadPairingsAsync(db, [workId], cancellationToken)).GetValueOrDefault(workId) ?? [];
        return new WorkAlignment(workId, hasReadableSource, readable, audio, pairings);
    }

    /// <summary>
    /// Loads the alignments of several Books from persisted projections only (the stored table of
    /// contents and chapter Entities), with a fixed number of queries and without opening any file.
    /// List projections use it to decide Linked or Separate and each format's progress.
    /// </summary>
    /// <param name="db">Database context.</param>
    /// <param name="bookIds">Books to load.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>Alignments keyed by Book id.</returns>
    internal static async Task<IReadOnlyDictionary<Guid, WorkAlignment>> LoadPersistedManyAsync(
        PrismediaDbContext db,
        IReadOnlyCollection<Guid> bookIds,
        CancellationToken cancellationToken) {
        if (bookIds.Count == 0) {
            return new Dictionary<Guid, WorkAlignment>();
        }

        var withReadableSource = (await db.EntityFiles.AsNoTracking()
                .Where(file => bookIds.Contains(file.EntityId) && file.Role == EntityFileRole.Source)
                .Select(file => file.EntityId)
                .Distinct()
                .ToArrayAsync(cancellationToken))
            .ToHashSet();
        var tableOfContents = (await db.BookReadingChapters.AsNoTracking()
                .Where(row => bookIds.Contains(row.BookId))
                .ToArrayAsync(cancellationToken))
            .ToLookup(row => row.BookId);
        var chapterKind = EntityKind.BookChapter.ToCode();
        var parentIds = bookIds.Select(id => (Guid?)id).ToArray();
        var chapters = (await db.Entities.AsNoTracking()
                .Where(row => parentIds.Contains(row.ParentEntityId) && row.KindCode == chapterKind && !row.IsWanted)
                .Select(row => new { BookId = row.ParentEntityId!.Value, row.Id, row.Title, row.SortOrder })
                .ToArrayAsync(cancellationToken))
            .ToLookup(row => row.BookId);
        var chapterIds = chapters.SelectMany(group => group.Select(chapter => chapter.Id)).ToArray();
        var pageCounts = chapterIds.Length == 0
            ? new Dictionary<Guid, int>()
            : await db.BookChapterDetails.AsNoTracking()
                .Where(row => chapterIds.Contains(row.EntityId) && row.PageCount != null)
                .ToDictionaryAsync(row => row.EntityId, row => row.PageCount!.Value, cancellationToken);
        var audio = await BookAudioChapterProjection.LoadManyAsync(db, bookIds, cancellationToken);
        var pairings = await LoadPairingsAsync(db, bookIds, cancellationToken);

        return bookIds.Distinct().ToDictionary(
            bookId => bookId,
            bookId => {
                IReadOnlyList<ReadableChapterWindow> readable = tableOfContents[bookId].Any()
                    ? tableOfContents[bookId]
                        .OrderBy(row => row.DisplayOrder)
                        .Select(row => new ReadableChapterWindow(
                            row.ChapterKey,
                            row.Title,
                            row.Depth,
                            row.ChapterKey,
                            null,
                            row.StartFraction,
                            row.EndFraction,
                            null))
                        .ToArray()
                    : chapters[bookId]
                        .OrderBy(chapter => chapter.SortOrder ?? int.MaxValue)
                        .ThenBy(chapter => chapter.Title, StringComparer.Ordinal)
                        .Select(chapter => new ReadableChapterWindow(
                            chapter.Id.ToString("D"),
                            chapter.Title,
                            0,
                            null,
                            chapter.Id,
                            null,
                            null,
                            pageCounts.GetValueOrDefault(chapter.Id)))
                        .ToArray();
                return new WorkAlignment(
                    bookId,
                    withReadableSource.Contains(bookId),
                    readable,
                    audio.GetValueOrDefault(bookId) ?? AudiobookRendition.None,
                    pairings.GetValueOrDefault(bookId) ?? []);
            });
    }

    /// <summary>
    /// Persisted pairings keyed by Book. Confirmed pairs always count; automatic pairs count only when
    /// the Book's stored mapping signature carries the current matcher version, because older
    /// automatic pairs may come from rules that guessed.
    /// </summary>
    private static async Task<IReadOnlyDictionary<Guid, ChapterPairing[]>> LoadPairingsAsync(
        PrismediaDbContext db,
        IReadOnlyCollection<Guid> bookIds,
        CancellationToken cancellationToken) {
        var currentAutomatic = (await db.BookContentStates.AsNoTracking()
                .Where(state => bookIds.Contains(state.BookId) && state.MappingSignature != null)
                .Select(state => new { state.BookId, state.MappingSignature })
                .ToArrayAsync(cancellationToken))
            .Where(state => BookChapterMatcher.IsCurrentSignature(state.MappingSignature))
            .Select(state => state.BookId)
            .ToHashSet();
        return (await db.BookChapterAudioMappings.AsNoTracking()
                .Where(row => bookIds.Contains(row.BookId))
                .Select(row => new { row.BookId, row.ReadableChapterKey, row.AudioTrackEntityId, row.AudioMarkerId, row.Origin })
                .ToArrayAsync(cancellationToken))
            .Where(row => row.Origin != BookChapterMappingOrigin.Auto || currentAutomatic.Contains(row.BookId))
            .GroupBy(row => row.BookId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(row => new ChapterPairing(row.ReadableChapterKey, row.AudioTrackEntityId, row.AudioMarkerId, row.Origin))
                    .ToArray());
    }

    /// <summary>
    /// Reads the readable chapter list. An EPUB that cannot be parsed has no chapter windows rather
    /// than failing alignment reads and listening heartbeats; replacing the file retries.
    /// </summary>
    private async Task<IReadOnlyList<BookContentsEntry>> LoadContentsAsync(
        Guid workId,
        CancellationToken cancellationToken) {
        try {
            return (await contents.GetAsync(workId, cancellationToken))?.Items ?? [];
        } catch (Exception) when (!cancellationToken.IsCancellationRequested) {
            return [];
        }
    }

    /// <summary>
    /// Paged chapters are Entities keyed by their id and carry a page count; EPUB entries are keyed by
    /// their navigation target and carry whole-work fractions.
    /// </summary>
    private static ReadableChapterWindow ToWindow(BookContentsEntry entry) {
        var chapterEntityId = entry.PageCount is not null && Guid.TryParse(entry.Id, out var id) ? id : (Guid?)null;
        return new ReadableChapterWindow(
            entry.Id,
            entry.Title,
            entry.Depth,
            chapterEntityId is null ? entry.Location : null,
            chapterEntityId,
            entry.StartFraction,
            entry.EndFraction,
            chapterEntityId is null ? null : entry.PageCount);
    }

    #endregion
}
