using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Prismedia.Application.Books;
using Prismedia.Application.Entities;
using Prismedia.Contracts.Books;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Media.Books;

/// <summary>
/// Persists associations between readable chapter keys and addressable audiobook chapter windows.
/// </summary>
internal sealed class EfBookChapterMappingService(
    PrismediaDbContext db,
    IEntityVisibilityChecker visibility,
    IBookChapterMapService chapterMap,
    IBookContentsService contents) : IBookChapterMappingService {
    #region Static Variables

    private const int MaximumReadableChapterKeyLength = 2048;

    #endregion

    #region Actions - Mappings

    /// <inheritdoc />
    public async Task<BookChapterMappingsResponse?> GetAsync(
        Guid bookId,
        CancellationToken cancellationToken) {
        if (!await IsVisibleBookAsync(bookId, cancellationToken)) {
            return null;
        }

        return await ReadAsync(bookId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<BookChapterMappingSaveResult> ReplaceAsync(
        Guid bookId,
        ReplaceBookChapterMappingsRequest request,
        CancellationToken cancellationToken) {
        if (!await IsVisibleBookAsync(bookId, cancellationToken)) {
            return new BookChapterMappingSaveResult(BookChapterMappingSaveStatus.NotFound, null);
        }

        var normalized = Normalize(request.Mappings);
        if (normalized.Error is not null) {
            return new BookChapterMappingSaveResult(BookChapterMappingSaveStatus.Invalid, normalized.Error);
        }

        if (normalized.Mappings.Count > 0) {
            var availableContents = await contents.GetAsync(bookId, cancellationToken);
            var readableKeys = availableContents?.Items
                .Select(item => item.Id)
                .ToHashSet(StringComparer.Ordinal);
            if (readableKeys is null || normalized.Mappings.Any(mapping =>
                    !readableKeys.Contains(mapping.Pair.ReadableChapterKey))) {
                return new BookChapterMappingSaveResult(
                    BookChapterMappingSaveStatus.Invalid,
                    "Every mapped readable chapter must belong to the Book's current contents.");
            }
        }

        var trackIds = normalized.Mappings.Select(mapping => mapping.Pair.AudioTrackId).Distinct().ToArray();
        var validTrackCount = await db.Entities
            .AsNoTracking()
            .CountAsync(row =>
                trackIds.Contains(row.Id) &&
                row.ParentEntityId == bookId &&
                row.KindCode == EntityKind.AudioTrack.ToCode() &&
                !row.IsWanted &&
                db.EntityFiles.Any(file =>
                    file.EntityId == row.Id &&
                    file.Role == EntityFileRole.Source),
                cancellationToken);
        if (validTrackCount != trackIds.Length) {
            return new BookChapterMappingSaveResult(
                BookChapterMappingSaveStatus.Invalid,
                "Every mapped audiobook file must be a playable source owned directly by this Book.");
        }

        var audio = await BookAudioChapterProjection.LoadAsync(db, bookId, cancellationToken);
        var availableAudioChapterIds = audio.Windows
            .Select(window => (window.TrackEntityId, window.MarkerId))
            .ToHashSet();
        if (normalized.Mappings.Any(mapping =>
                !availableAudioChapterIds.Contains((mapping.Pair.AudioTrackId, mapping.Pair.AudioMarkerId)))) {
            return new BookChapterMappingSaveResult(
                BookChapterMappingSaveStatus.Invalid,
                "Every mapped audiobook chapter must identify an available whole file or embedded chapter.");
        }

        IDbContextTransaction? transaction = null;
        if (db.Database.IsRelational()) {
            transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        }

        try {
            var existing = await db.BookChapterAudioMappings
                .Where(row => row.BookId == bookId)
                .ToArrayAsync(cancellationToken);
            db.BookChapterAudioMappings.RemoveRange(existing);
            await db.SaveChangesAsync(cancellationToken);

            var now = DateTimeOffset.UtcNow;
            db.BookChapterAudioMappings.AddRange(normalized.Mappings.Select(mapping =>
                new BookChapterAudioMappingRow {
                    Id = Guid.NewGuid(),
                    BookId = bookId,
                    ReadableChapterKey = mapping.Pair.ReadableChapterKey,
                    AudioTrackEntityId = mapping.Pair.AudioTrackId,
                    AudioMarkerId = mapping.Pair.AudioMarkerId,
                    Origin = mapping.Origin,
                    UpdatedAt = now
                }));
            await db.SaveChangesAsync(cancellationToken);

            if (transaction is not null) {
                await transaction.CommitAsync(cancellationToken);
            }
        } finally {
            if (transaction is not null) {
                await transaction.DisposeAsync();
            }
        }

        // The saved manual pairs consume readable and audio chapters, so the automatic layer is stale by
        // definition; refill it inline so the caller's next alignment read is the complete merged map.
        await chapterMap.RefreshAsync(bookId, cancellationToken);
        return new BookChapterMappingSaveResult(BookChapterMappingSaveStatus.Saved, null);
    }

    #endregion

    #region Actions - Reads

    private async Task<bool> IsVisibleBookAsync(Guid bookId, CancellationToken cancellationToken) =>
        await visibility.IsVisibleAsync(bookId, cancellationToken) &&
        await db.Entities.AsNoTracking().AnyAsync(
            row => row.Id == bookId && row.KindCode == EntityKind.Book.ToCode(),
            cancellationToken);

    private async Task<BookChapterMappingsResponse> ReadAsync(
        Guid bookId,
        CancellationToken cancellationToken) {
        var rows = await db.BookChapterAudioMappings
            .AsNoTracking()
            .Where(row => row.BookId == bookId)
            .OrderBy(row => row.ReadableChapterKey)
            .Select(row => new {
                row.ReadableChapterKey,
                row.AudioTrackEntityId,
                row.AudioMarkerId,
                row.Origin
            })
            .ToArrayAsync(cancellationToken);
        var audioChapters = BookAudioChapterProjection.Matchable(
            await BookAudioChapterProjection.LoadAsync(db, bookId, cancellationToken));
        return new BookChapterMappingsResponse(
            rows.Select(row => new BookChapterAudioMapping(
                    row.ReadableChapterKey,
                    row.AudioTrackEntityId,
                    row.Origin.ToCode(),
                    row.AudioMarkerId))
                .ToArray(),
            audioChapters.Select(chapter => new BookAudioChapter(
                    chapter.AudioTrackId,
                    chapter.AudioMarkerId,
                    chapter.Title,
                    chapter.StartSeconds,
                    chapter.EndSeconds))
                .ToArray());
    }

    #endregion

    #region Actions - Validation

    /// <summary>
    /// Validates a save request: one-to-one keys, and a person-confirmed origin on every row
    /// (<c>manual</c> when omitted, or <c>ordered</c> for a reviewed in-order fill). Automatic rows are
    /// server-owned and are never accepted from a client.
    /// </summary>
    private static NormalizedMappings Normalize(IReadOnlyList<BookChapterAudioMapping>? mappings) {
        if (mappings is null) {
            return new NormalizedMappings([], "A chapter mapping list is required.");
        }

        var normalized = new List<ConfirmedMapping>(mappings.Count);
        var chapterKeys = new HashSet<string>(StringComparer.Ordinal);
        var audioChapterIds = new HashSet<(Guid AudioTrackId, Guid? AudioMarkerId)>();
        foreach (var mapping in mappings) {
            var origin = BookChapterMappingOrigin.Manual;
            if (mapping.Origin is { } code &&
                (!code.TryDecodeAs(out origin) || origin == BookChapterMappingOrigin.Auto)) {
                return new NormalizedMappings(
                    [],
                    $"Saved chapter pairs must be '{BookChapterMappingOrigin.Manual.ToCode()}' or '{BookChapterMappingOrigin.Ordered.ToCode()}'; received '{code}'.");
            }
            var chapterKey = mapping.ReadableChapterKey?.Trim() ?? string.Empty;
            if (chapterKey.Length == 0 || chapterKey.Length > MaximumReadableChapterKeyLength) {
                return new NormalizedMappings(
                    [],
                    $"Readable chapter keys must contain between 1 and {MaximumReadableChapterKeyLength} characters.");
            }
            if (mapping.AudioTrackId == Guid.Empty) {
                return new NormalizedMappings([], "A mapped audiobook file identifier is required.");
            }
            if (!chapterKeys.Add(chapterKey)) {
                return new NormalizedMappings([], "A readable chapter can map to only one audiobook file.");
            }
            if (!audioChapterIds.Add((mapping.AudioTrackId, mapping.AudioMarkerId))) {
                return new NormalizedMappings([], "An audiobook chapter can map to only one readable chapter.");
            }

            normalized.Add(new ConfirmedMapping(
                new BookChapterAudioMapping(
                    chapterKey,
                    mapping.AudioTrackId,
                    AudioMarkerId: mapping.AudioMarkerId),
                origin));
        }

        return new NormalizedMappings(normalized, null);
    }

    /// <summary>One validated saved pair and how the person confirmed it.</summary>
    private sealed record ConfirmedMapping(BookChapterAudioMapping Pair, BookChapterMappingOrigin Origin);

    private sealed record NormalizedMappings(
        IReadOnlyList<ConfirmedMapping> Mappings,
        string? Error);

    #endregion
}
