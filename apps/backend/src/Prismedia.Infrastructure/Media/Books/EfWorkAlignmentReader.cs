using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Books;
using Prismedia.Contracts.Books;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Media.Books;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Media.Books;

/// <summary>
/// Loads a Book's reading/listening alignment from the same projections the contents and
/// chapter-mapping routes use: the readable chapter list, the playable tracks with their source
/// chapter markers, and the persisted manual and automatic chapter pairings.
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
        var pairings = (await db.BookChapterAudioMappings.AsNoTracking()
                .Where(row => row.BookId == workId)
                .Select(row => new { row.ReadableChapterKey, row.AudioTrackEntityId, row.AudioMarkerId, row.Origin })
                .ToArrayAsync(cancellationToken))
            .Select(row => new ChapterPairing(row.ReadableChapterKey, row.AudioTrackEntityId, row.AudioMarkerId, row.Origin))
            .ToArray();
        return new WorkAlignment(workId, hasReadableSource, readable, audio.Tracks, audio.Windows, pairings);
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
