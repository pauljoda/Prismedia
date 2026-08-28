using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Media.Books;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class EfBookChapterMapServiceTests {
    [Fact]
    public async Task PersistsAutomaticTitleMatchesForChapterEntityBooks() {
        await using var db = CreateContext();
        var bookId = AddEntity(db, EntityKind.Book, "Book");
        var chapterId = AddEntity(db, EntityKind.BookChapter, "Prologue", bookId, 0);
        var trackId = AddEntity(db, EntityKind.AudioTrack, "00 - Prologue", bookId, 0);
        AddSource(db, trackId);
        await db.SaveChangesAsync();
        var service = new EfBookChapterMapService(db, new EpubBookContentsCache());

        Assert.True(await service.IsRefreshNeededAsync(bookId, CancellationToken.None));
        var result = await service.RefreshAsync(bookId, CancellationToken.None);

        Assert.True(result.AutoMappingsReplaced);
        var row = Assert.Single(db.BookChapterAudioMappings);
        Assert.Equal(chapterId.ToString("D"), row.ReadableChapterKey);
        Assert.Equal(trackId, row.AudioTrackEntityId);
        Assert.Equal(BookChapterMappingOrigin.Auto, row.Origin);
    }

    [Fact]
    public async Task RefreshNoOpsWhenInputsAreUnchanged() {
        await using var db = CreateContext();
        var bookId = AddEntity(db, EntityKind.Book, "Book");
        AddEntity(db, EntityKind.BookChapter, "Prologue", bookId, 0);
        var trackId = AddEntity(db, EntityKind.AudioTrack, "Prologue", bookId, 0);
        AddSource(db, trackId);
        await db.SaveChangesAsync();
        var service = new EfBookChapterMapService(db, new EpubBookContentsCache());

        await service.RefreshAsync(bookId, CancellationToken.None);
        Assert.False(await service.IsRefreshNeededAsync(bookId, CancellationToken.None));
        var second = await service.RefreshAsync(bookId, CancellationToken.None);

        Assert.False(second.ContentsRefreshed);
        Assert.False(second.AutoMappingsReplaced);
        Assert.Single(db.BookChapterAudioMappings);
    }

    [Fact]
    public async Task ManualRowsSurviveRefreshAndPinTheirTrack() {
        await using var db = CreateContext();
        var bookId = AddEntity(db, EntityKind.Book, "Book");
        var prologueId = AddEntity(db, EntityKind.BookChapter, "Prologue", bookId, 0);
        var chapterOneId = AddEntity(db, EntityKind.BookChapter, "Chapter 1", bookId, 1);
        var trackId = AddEntity(db, EntityKind.AudioTrack, "Prologue", bookId, 0);
        AddSource(db, trackId);
        db.BookChapterAudioMappings.Add(new BookChapterAudioMappingRow {
            Id = Guid.NewGuid(),
            BookId = bookId,
            ReadableChapterKey = chapterOneId.ToString("D"),
            AudioTrackEntityId = trackId,
            Origin = BookChapterMappingOrigin.Manual,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var service = new EfBookChapterMapService(db, new EpubBookContentsCache());

        await service.RefreshAsync(bookId, CancellationToken.None);

        // The manual pair consumed the only track, so no auto row may appear even though the
        // track title exactly matches the prologue chapter.
        var row = Assert.Single(db.BookChapterAudioMappings);
        Assert.Equal(BookChapterMappingOrigin.Manual, row.Origin);
        Assert.Equal(chapterOneId.ToString("D"), row.ReadableChapterKey);
        Assert.NotEqual(prologueId.ToString("D"), row.ReadableChapterKey);
    }

    [Fact]
    public async Task TrackChangesInvalidateAndRecomputeTheAutoLayer() {
        await using var db = CreateContext();
        var bookId = AddEntity(db, EntityKind.Book, "Book");
        AddEntity(db, EntityKind.BookChapter, "Prologue", bookId, 0);
        AddEntity(db, EntityKind.BookChapter, "Epilogue", bookId, 1);
        var firstTrack = AddEntity(db, EntityKind.AudioTrack, "Prologue", bookId, 0);
        AddSource(db, firstTrack);
        await db.SaveChangesAsync();
        var service = new EfBookChapterMapService(db, new EpubBookContentsCache());
        await service.RefreshAsync(bookId, CancellationToken.None);
        Assert.Single(db.BookChapterAudioMappings);

        var secondTrack = AddEntity(db, EntityKind.AudioTrack, "Epilogue", bookId, 1);
        AddSource(db, secondTrack);
        await db.SaveChangesAsync();

        Assert.True(await service.IsRefreshNeededAsync(bookId, CancellationToken.None));
        await service.RefreshAsync(bookId, CancellationToken.None);
        Assert.Equal(2, db.BookChapterAudioMappings.Count());
        Assert.All(db.BookChapterAudioMappings, row => Assert.Equal(BookChapterMappingOrigin.Auto, row.Origin));
    }

    [Fact]
    public async Task PersistsMultipleEmbeddedChapterMatchesForOnePhysicalTrack() {
        await using var db = CreateContext();
        var bookId = AddEntity(db, EntityKind.Book, "Book");
        var openingId = AddEntity(db, EntityKind.BookChapter, "Opening Credits", bookId, 0);
        var chapterId = AddEntity(db, EntityKind.BookChapter, "Chapter One", bookId, 1);
        var trackId = AddEntity(db, EntityKind.AudioTrack, "Whole Book", bookId, 0);
        AddSource(db, trackId);
        var openingMarkerId = AddMarker(db, trackId, "Opening Credits", 0, 12.5);
        var chapterMarkerId = AddMarker(db, trackId, "Chapter One", 12.5, 180);
        await db.SaveChangesAsync();
        var service = new EfBookChapterMapService(db, new EpubBookContentsCache());

        var result = await service.RefreshAsync(bookId, CancellationToken.None);

        Assert.True(result.AutoMappingsReplaced);
        var rows = await db.BookChapterAudioMappings.OrderBy(row => row.ReadableChapterKey).ToArrayAsync();
        Assert.Equal(2, rows.Length);
        Assert.Contains(rows, row => row.ReadableChapterKey == openingId.ToString("D") && row.AudioMarkerId == openingMarkerId);
        Assert.Contains(rows, row => row.ReadableChapterKey == chapterId.ToString("D") && row.AudioMarkerId == chapterMarkerId);
        Assert.All(rows, row => Assert.Equal(trackId, row.AudioTrackEntityId));
    }

    [Fact]
    public async Task ListsOnlyStaleBooksUnderTheGivenRoot() {
        await using var db = CreateContext();
        var insideId = AddEntity(db, EntityKind.Book, "Inside");
        AddEntity(db, EntityKind.BookChapter, "Prologue", insideId, 0);
        var insideTrack = AddEntity(db, EntityKind.AudioTrack, "Prologue", insideId, 0);
        AddSource(db, insideTrack, "/library/books/inside/prologue.mp3");
        var outsideId = AddEntity(db, EntityKind.Book, "Outside");
        var outsideTrack = AddEntity(db, EntityKind.AudioTrack, "Elsewhere", outsideId, 0);
        AddSource(db, outsideTrack, "/other-root/elsewhere.mp3");
        await db.SaveChangesAsync();
        var service = new EfBookChapterMapService(db, new EpubBookContentsCache());

        var stale = await service.ListStaleForRootAsync("/library/books", CancellationToken.None);
        Assert.Equal(insideId, Assert.Single(stale).BookId);

        await service.RefreshAsync(insideId, CancellationToken.None);
        Assert.Empty(await service.ListStaleForRootAsync("/library/books", CancellationToken.None));
    }

    [Fact]
    public async Task BooksWithNoChaptersOrTracksNeedNoRefresh() {
        await using var db = CreateContext();
        var bookId = AddEntity(db, EntityKind.Book, "Book");
        await db.SaveChangesAsync();
        var service = new EfBookChapterMapService(db, new EpubBookContentsCache());

        Assert.False(await service.IsRefreshNeededAsync(bookId, CancellationToken.None));
    }

    private static PrismediaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase($"book-chapter-map-{Guid.NewGuid():N}")
            .Options);

    private static Guid AddEntity(
        PrismediaDbContext db,
        EntityKind kind,
        string title,
        Guid? parentId = null,
        int? sortOrder = null) {
        var id = Guid.NewGuid();
        db.Entities.Add(new EntityRow {
            Id = id,
            KindCode = kind.ToCode(),
            Title = title,
            ParentEntityId = parentId,
            SortOrder = sortOrder,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        return id;
    }

    private static void AddSource(PrismediaDbContext db, Guid entityId, string? path = null) {
        db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(),
            EntityId = entityId,
            Role = EntityFileRole.Source,
            Path = path ?? $"/media/{entityId:N}.mp3",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
    }

    private static Guid AddMarker(
        PrismediaDbContext db,
        Guid entityId,
        string title,
        double seconds,
        double endSeconds) {
        var id = Guid.NewGuid();
        db.EntityMarkers.Add(new EntityMarkerRow {
            Id = id,
            EntityId = entityId,
            Title = title,
            Seconds = seconds,
            EndSeconds = endSeconds,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        return id;
    }
}
