using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Books;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Media.Books;

/// <summary>
/// Maintains the scan-owned book projections: the persisted readable chapter list (EPUB table of
/// contents) and the automatic audiobook chapter map. Both are guarded by signatures on
/// <c>book_content_states</c> so refresh passes no-op when nothing relevant changed, and manual
/// mapping rows are treated as immovable input, never output.
/// </summary>
internal sealed class EfBookChapterMapService(
    PrismediaDbContext db,
    EpubBookContentsCache epubCache) : IBookChapterMapService {
    /// <inheritdoc />
    public async Task<bool> IsRefreshNeededAsync(Guid bookId, CancellationToken cancellationToken) {
        var state = await db.BookContentStates.AsNoTracking()
            .SingleOrDefaultAsync(row => row.BookId == bookId, cancellationToken);
        var epub = await ResolveEpubSourceAsync(bookId, cancellationToken);
        if (epub is { Exists: true } && !string.Equals(
                SourceSignatureFor(epub), state?.SourceSignature, StringComparison.Ordinal)) {
            return true;
        }

        var inputs = await LoadMappingInputsAsync(bookId, epub, cancellationToken);
        if (inputs.ReadableChapters.Count == 0 && inputs.AudioTracks.Count == 0) {
            return state is not null &&
                (state.SourceSignature is not null || state.MappingSignature is not null);
        }

        return !string.Equals(MappingSignatureFor(inputs), state?.MappingSignature, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public async Task<BookChapterMapRefreshResult> RefreshAsync(Guid bookId, CancellationToken cancellationToken) {
        var isBook = await db.Entities.AsNoTracking().AnyAsync(
            row => row.Id == bookId && row.KindCode == EntityKind.Book.ToCode(),
            cancellationToken);
        if (!isBook) {
            return new BookChapterMapRefreshResult(false, false);
        }

        var state = await db.BookContentStates
            .SingleOrDefaultAsync(row => row.BookId == bookId, cancellationToken);
        var epub = await ResolveEpubSourceAsync(bookId, cancellationToken);
        if (epub is { Exists: false }) {
            // The source row exists but the file is unreachable (unmounted share, mid-move). Keep
            // whatever is persisted; the next scan re-enqueues once the file is back.
            return new BookChapterMapRefreshResult(false, false);
        }

        var contentsRefreshed = false;
        if (epub is not null) {
            var signature = SourceSignatureFor(epub);
            if (!string.Equals(signature, state?.SourceSignature, StringComparison.Ordinal)) {
                IReadOnlyList<Contracts.Books.BookContentsEntry> parsedItems;
                try {
                    var parsed = await epubCache.GetAsync(
                        new EpubBookContentsCacheKey(epub.Info.FullName, epub.Info.Length, epub.Info.LastWriteTimeUtc.Ticks),
                        cancellationToken);
                    parsedItems = parsed.Items;
                } catch (Exception) when (!cancellationToken.IsCancellationRequested) {
                    // A structurally broken EPUB stays broken until the file itself changes, so
                    // record the signature with no chapters instead of re-enqueueing this book on
                    // every verification scan. Replacing the file changes the signature and retries.
                    parsedItems = [];
                }

                await ReplaceReadingChaptersAsync(bookId, parsedItems, cancellationToken);
                state = await EnsureStateAsync(state, bookId, cancellationToken);
                state.SourceSignature = signature;
                contentsRefreshed = true;
            }
        } else if (state?.SourceSignature is not null) {
            db.BookReadingChapters.RemoveRange(
                await db.BookReadingChapters.Where(row => row.BookId == bookId).ToArrayAsync(cancellationToken));
            state.SourceSignature = null;
            contentsRefreshed = true;
        }

        var inputs = await LoadMappingInputsAsync(bookId, epub, cancellationToken);
        var mappingSignature = MappingSignatureFor(inputs);
        var autoReplaced = false;
        if (!string.Equals(mappingSignature, state?.MappingSignature, StringComparison.Ordinal)) {
            var autoPairs = BookChapterMatcher.ComputeAutoPairs(
                inputs.ReadableChapters,
                inputs.AudioTracks,
                inputs.ManualPairs);
            // Dangling manual rows (their chapter key vanished with a replaced EPUB) still pin
            // their track and key so the unique indexes can never collide with an auto row.
            var blockedKeys = inputs.AllManualChapterKeys.ToHashSet(StringComparer.Ordinal);
            var blockedTracks = inputs.AllManualTrackIds.ToHashSet();
            var nextAuto = autoPairs
                .Where(pair => !blockedKeys.Contains(pair.ChapterKey) && !blockedTracks.Contains(pair.AudioTrackId))
                .ToArray();

            var existingAuto = await db.BookChapterAudioMappings
                .Where(row => row.BookId == bookId && row.Origin == BookChapterMappingOrigin.Auto)
                .ToArrayAsync(cancellationToken);
            db.BookChapterAudioMappings.RemoveRange(existingAuto);
            var now = DateTimeOffset.UtcNow;
            db.BookChapterAudioMappings.AddRange(nextAuto.Select(pair => new BookChapterAudioMappingRow {
                Id = Guid.NewGuid(),
                BookId = bookId,
                ReadableChapterKey = pair.ChapterKey,
                AudioTrackEntityId = pair.AudioTrackId,
                Origin = BookChapterMappingOrigin.Auto,
                UpdatedAt = now
            }));
            state = await EnsureStateAsync(state, bookId, cancellationToken);
            state.MappingSignature = mappingSignature;
            autoReplaced = existingAuto.Length > 0 || nextAuto.Length > 0;
        }

        if (state is not null) {
            state.RefreshedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
        return new BookChapterMapRefreshResult(contentsRefreshed, autoReplaced);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StaleBookChapterMap>> ListStaleForRootAsync(
        string rootPath,
        CancellationToken cancellationToken) {
        var prefix = rootPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var bookKind = EntityKind.Book.ToCode();
        var trackKind = EntityKind.AudioTrack.ToCode();
        var candidates = await db.Entities.AsNoTracking()
            .Where(book => book.KindCode == bookKind && !book.IsWanted &&
                (db.EntityFiles.Any(file => file.EntityId == book.Id &&
                     file.Role == EntityFileRole.Source && file.Path.StartsWith(prefix)) ||
                 db.Entities.Any(track => track.ParentEntityId == book.Id && track.KindCode == trackKind &&
                     db.EntityFiles.Any(file => file.EntityId == track.Id &&
                         file.Role == EntityFileRole.Source && file.Path.StartsWith(prefix)))))
            .Select(book => new { book.Id, book.Title })
            .ToArrayAsync(cancellationToken);

        var stale = new List<StaleBookChapterMap>();
        foreach (var candidate in candidates) {
            if (await IsRefreshNeededAsync(candidate.Id, cancellationToken)) {
                stale.Add(new StaleBookChapterMap(candidate.Id, candidate.Title));
            }
        }

        return stale;
    }

    private async Task<BookContentStateRow> EnsureStateAsync(
        BookContentStateRow? state,
        Guid bookId,
        CancellationToken cancellationToken) {
        if (state is not null) {
            return state;
        }

        var tracked = await db.BookContentStates
            .SingleOrDefaultAsync(row => row.BookId == bookId, cancellationToken);
        if (tracked is not null) {
            return tracked;
        }

        var created = new BookContentStateRow { BookId = bookId, RefreshedAt = DateTimeOffset.UtcNow };
        db.BookContentStates.Add(created);
        return created;
    }

    private async Task ReplaceReadingChaptersAsync(
        Guid bookId,
        IReadOnlyList<Contracts.Books.BookContentsEntry> entries,
        CancellationToken cancellationToken) {
        db.BookReadingChapters.RemoveRange(
            await db.BookReadingChapters.Where(row => row.BookId == bookId).ToArrayAsync(cancellationToken));
        db.BookReadingChapters.AddRange(entries.Select(entry => new BookReadingChapterRow {
            BookId = bookId,
            ChapterKey = entry.Id,
            Title = entry.Title,
            Depth = entry.Depth,
            DisplayOrder = entry.Order,
            SectionIndex = entry.SectionIndex,
            StartFraction = entry.StartFraction,
            EndFraction = entry.EndFraction
        }));
    }

    private sealed record EpubSource(FileInfo Info, bool Exists);

    private async Task<EpubSource?> ResolveEpubSourceAsync(Guid bookId, CancellationToken cancellationToken) {
        var path = await db.EntityFiles.AsNoTracking()
            .Where(file => file.EntityId == bookId && file.Role == EntityFileRole.Source)
            .OrderBy(file => file.Path)
            .Select(file => file.Path)
            .FirstOrDefaultAsync(cancellationToken);
        if (path is null || !string.Equals(Path.GetExtension(path), ".epub", StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        var info = new FileInfo(path);
        return new EpubSource(info, info.Exists);
    }

    private static string SourceSignatureFor(EpubSource epub) =>
        $"epub:{epub.Info.Length}:{epub.Info.LastWriteTimeUtc.Ticks}:{ShortHash(epub.Info.FullName)}";

    private sealed record MappingInputs(
        IReadOnlyList<MatchableReadableChapter> ReadableChapters,
        IReadOnlyList<MatchableAudioTrack> AudioTracks,
        IReadOnlyList<(string ChapterKey, Guid AudioTrackId)> ManualPairs,
        IReadOnlyList<string> AllManualChapterKeys,
        IReadOnlyList<Guid> AllManualTrackIds);

    private async Task<MappingInputs> LoadMappingInputsAsync(
        Guid bookId,
        EpubSource? epub,
        CancellationToken cancellationToken) {
        IReadOnlyList<MatchableReadableChapter> readable;
        if (epub is not null) {
            readable = (await db.BookReadingChapters.AsNoTracking()
                    .Where(row => row.BookId == bookId)
                    .OrderBy(row => row.DisplayOrder)
                    .Select(row => new { row.ChapterKey, row.Title, row.DisplayOrder })
                    .ToArrayAsync(cancellationToken))
                .Select(row => new MatchableReadableChapter(row.ChapterKey, row.Title, row.DisplayOrder))
                .ToArray();
        } else {
            var chapterKind = EntityKind.BookChapter.ToCode();
            readable = (await db.Entities.AsNoTracking()
                    .Where(row => row.ParentEntityId == bookId && row.KindCode == chapterKind && !row.IsWanted)
                    .Select(row => new { row.Id, row.Title, row.SortOrder })
                    .ToArrayAsync(cancellationToken))
                .OrderBy(row => row.SortOrder ?? int.MaxValue)
                .ThenBy(row => row.Title, StringComparer.Ordinal)
                .Select((row, index) => new MatchableReadableChapter(row.Id.ToString("D"), row.Title, index))
                .ToArray();
        }

        var trackKind = EntityKind.AudioTrack.ToCode();
        var tracks = (await db.Entities.AsNoTracking()
                .Where(row => row.ParentEntityId == bookId && row.KindCode == trackKind && !row.IsWanted &&
                    db.EntityFiles.Any(file => file.EntityId == row.Id && file.Role == EntityFileRole.Source))
                .Select(row => new { row.Id, row.Title, row.SortOrder })
                .ToArrayAsync(cancellationToken))
            .Select(row => new MatchableAudioTrack(row.Id, row.Title, row.SortOrder ?? 0))
            .ToArray();

        var manualRows = await db.BookChapterAudioMappings.AsNoTracking()
            .Where(row => row.BookId == bookId && row.Origin == BookChapterMappingOrigin.Manual)
            .OrderBy(row => row.ReadableChapterKey)
            .Select(row => new { row.ReadableChapterKey, row.AudioTrackEntityId })
            .ToArrayAsync(cancellationToken);
        var manualPairs = manualRows
            .Select(row => (row.ReadableChapterKey, row.AudioTrackEntityId))
            .ToArray();

        return new MappingInputs(
            readable,
            tracks,
            manualPairs,
            manualRows.Select(row => row.ReadableChapterKey).ToArray(),
            manualRows.Select(row => row.AudioTrackEntityId).ToArray());
    }

    private static string MappingSignatureFor(MappingInputs inputs) {
        var builder = new StringBuilder();
        foreach (var chapter in inputs.ReadableChapters) {
            builder.Append("r|").Append(chapter.Key).Append('|').Append(chapter.Title).Append('\n');
        }
        foreach (var track in inputs.AudioTracks.OrderBy(track => track.Id)) {
            builder.Append("t|").Append(track.Id.ToString("D")).Append('|')
                .Append(track.SortOrder).Append('|').Append(track.Title).Append('\n');
        }
        foreach (var (chapterKey, trackId) in inputs.ManualPairs) {
            builder.Append("m|").Append(chapterKey).Append('|').Append(trackId.ToString("D")).Append('\n');
        }

        return ShortHash(builder.ToString());
    }

    private static string ShortHash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..32].ToLowerInvariant();
}
