using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Books;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Media.Books;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class EfBookChapterMapServiceTests {
    [Fact]
    public async Task PersistsAutomaticMatchesFromTitleTagsButNeverFromFileNames() {
        await using var db = CreateContext();
        var bookId = AddEntity(db, EntityKind.Book, "Book");
        var prologueId = AddEntity(db, EntityKind.BookChapter, "Prologue", bookId, 0);
        AddEntity(db, EntityKind.BookChapter, "Epilogue", bookId, 1);
        // The first file's tag names its chapter; the second is titled only by its file name.
        var taggedTrack = AddTrack(db, bookId, "00 - Intro", 0, titleTag: "Prologue");
        AddTrack(db, bookId, "Epilogue", 1);
        await db.SaveChangesAsync();
        var service = new EfBookChapterMapService(db, new EpubBookContentsCache());

        Assert.True(await service.IsRefreshNeededAsync(bookId, CancellationToken.None));
        var result = await service.RefreshAsync(bookId, CancellationToken.None);

        Assert.True(result.AutoMappingsReplaced);
        var row = Assert.Single(db.BookChapterAudioMappings);
        Assert.Equal(prologueId.ToString("D"), row.ReadableChapterKey);
        Assert.Equal(taggedTrack, row.AudioTrackEntityId);
        Assert.Equal(BookChapterMappingOrigin.Auto, row.Origin);
        Assert.True(BookChapterMatcher.IsCurrentSignature(db.BookContentStates.Single().MappingSignature));
    }

    [Fact]
    public async Task RefreshNoOpsWhenInputsAreUnchanged() {
        await using var db = CreateContext();
        var bookId = AddEntity(db, EntityKind.Book, "Book");
        AddEntity(db, EntityKind.BookChapter, "Prologue", bookId, 0);
        var trackId = AddTrack(db, bookId, "Book", 0);
        AddMarker(db, trackId, "Prologue", 0, 60);
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
    public async Task ConfirmedRowsSurviveRefreshAndPinTheirTrack() {
        await using var db = CreateContext();
        var bookId = AddEntity(db, EntityKind.Book, "Book");
        var prologueId = AddEntity(db, EntityKind.BookChapter, "Prologue", bookId, 0);
        var chapterOneId = AddEntity(db, EntityKind.BookChapter, "Chapter 1", bookId, 1);
        var chapterTwoId = AddEntity(db, EntityKind.BookChapter, "Chapter 2", bookId, 2);
        var firstTrack = AddTrack(db, bookId, "01", 0, titleTag: "Prologue");
        var secondTrack = AddTrack(db, bookId, "02", 1, titleTag: "Chapter 2");
        AddConfirmed(db, bookId, chapterOneId, firstTrack, BookChapterMappingOrigin.Manual);
        AddConfirmed(db, bookId, chapterTwoId, secondTrack, BookChapterMappingOrigin.Ordered);
        await db.SaveChangesAsync();
        var service = new EfBookChapterMapService(db, new EpubBookContentsCache());

        await service.RefreshAsync(bookId, CancellationToken.None);

        // The confirmed pairs consumed both tracks, so no auto row may appear even though the first
        // track's title tag exactly matches the prologue chapter.
        var rows = await db.BookChapterAudioMappings.ToArrayAsync();
        Assert.Equal(2, rows.Length);
        Assert.DoesNotContain(rows, row => row.Origin == BookChapterMappingOrigin.Auto);
        Assert.DoesNotContain(rows, row => row.ReadableChapterKey == prologueId.ToString("D"));
    }

    [Fact]
    public async Task PartSplitsAndSingleChapterlessFilesAreNeverPairedAutomatically() {
        await using var db = CreateContext();
        var bookId = AddEntity(db, EntityKind.Book, "Book");
        AddEntity(db, EntityKind.BookChapter, "Prologue", bookId, 0);
        AddEntity(db, EntityKind.BookChapter, "Epilogue", bookId, 1);
        AddTrack(db, bookId, "Book Part 1", 0, titleTag: "Prologue");
        await db.SaveChangesAsync();
        var service = new EfBookChapterMapService(db, new EpubBookContentsCache());

        // One chapterless file: no boundary to pair, whatever its tag says.
        await service.RefreshAsync(bookId, CancellationToken.None);
        Assert.Empty(db.BookChapterAudioMappings);

        // Two files named as parts: still no chapter boundaries.
        AddTrack(db, bookId, "Book Part 2", 1, titleTag: "Epilogue");
        await db.SaveChangesAsync();
        Assert.True(await service.IsRefreshNeededAsync(bookId, CancellationToken.None));
        await service.RefreshAsync(bookId, CancellationToken.None);
        Assert.Empty(db.BookChapterAudioMappings);
    }

    [Fact]
    public async Task TrackChangesInvalidateAndRecomputeTheAutoLayer() {
        await using var db = CreateContext();
        var bookId = AddEntity(db, EntityKind.Book, "Book");
        AddEntity(db, EntityKind.BookChapter, "Prologue", bookId, 0);
        AddEntity(db, EntityKind.BookChapter, "Epilogue", bookId, 1);
        AddTrack(db, bookId, "01", 0, titleTag: "Prologue");
        await db.SaveChangesAsync();
        var service = new EfBookChapterMapService(db, new EpubBookContentsCache());
        await service.RefreshAsync(bookId, CancellationToken.None);
        Assert.Empty(db.BookChapterAudioMappings);

        AddTrack(db, bookId, "02", 1, titleTag: "Epilogue");
        await db.SaveChangesAsync();

        Assert.True(await service.IsRefreshNeededAsync(bookId, CancellationToken.None));
        await service.RefreshAsync(bookId, CancellationToken.None);
        Assert.Equal(2, db.BookChapterAudioMappings.Count());
        Assert.All(db.BookChapterAudioMappings, row => Assert.Equal(BookChapterMappingOrigin.Auto, row.Origin));
    }

    [Fact]
    public async Task MapsBuiltByAnOlderMatcherAreListedAndRecomputedOnce() {
        await using var db = CreateContext();
        var bookId = AddEntity(db, EntityKind.Book, "Book");
        var chapterId = AddEntity(db, EntityKind.BookChapter, "Prologue", bookId, 0);
        var trackId = AddTrack(db, bookId, "Whole Book", 0);
        // Written by the old matcher: a positional guess, under an unversioned signature.
        db.BookContentStates.Add(new BookContentStateRow {
            BookId = bookId,
            MappingSignature = "0123456789abcdef0123456789abcdef",
            RefreshedAt = DateTimeOffset.UtcNow
        });
        db.BookChapterAudioMappings.Add(new BookChapterAudioMappingRow {
            Id = Guid.NewGuid(),
            BookId = bookId,
            ReadableChapterKey = chapterId.ToString("D"),
            AudioTrackEntityId = trackId,
            Origin = BookChapterMappingOrigin.Auto,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var service = new EfBookChapterMapService(db, new EpubBookContentsCache());

        Assert.Equal(bookId, Assert.Single(await service.ListOutdatedMatcherMapsAsync(CancellationToken.None)).BookId);
        Assert.True(await service.IsRefreshNeededAsync(bookId, CancellationToken.None));

        var result = await service.RefreshAsync(bookId, CancellationToken.None);

        Assert.True(result.AutoMappingsReplaced);
        Assert.Empty(db.BookChapterAudioMappings);
        Assert.Empty(await service.ListOutdatedMatcherMapsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task PersistsMultipleEmbeddedChapterMatchesForOnePhysicalTrack() {
        await using var db = CreateContext();
        var bookId = AddEntity(db, EntityKind.Book, "Book");
        var openingId = AddEntity(db, EntityKind.BookChapter, "Opening Credits", bookId, 0);
        var chapterId = AddEntity(db, EntityKind.BookChapter, "Chapter One", bookId, 1);
        var trackId = AddTrack(db, bookId, "Whole Book", 0);
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
    public async Task UntitledEmbeddedChaptersNeverMatchTheirPlaceholder() {
        await using var db = CreateContext();
        var bookId = AddEntity(db, EntityKind.Book, "Book");
        AddEntity(db, EntityKind.BookChapter, "Chapter 1", bookId, 0);
        var trackId = AddTrack(db, bookId, "Whole Book", 0);
        AddMarker(db, trackId, "Chapter 1", 0, 60, untitled: true);
        await db.SaveChangesAsync();
        var service = new EfBookChapterMapService(db, new EpubBookContentsCache());

        await service.RefreshAsync(bookId, CancellationToken.None);

        Assert.Empty(db.BookChapterAudioMappings);
    }

    [Fact]
    public async Task ListsOnlyStaleBooksUnderTheGivenRoot() {
        await using var db = CreateContext();
        var insideId = AddEntity(db, EntityKind.Book, "Inside");
        AddEntity(db, EntityKind.BookChapter, "Prologue", insideId, 0);
        AddTrack(db, insideId, "Prologue", 0, path: "/library/books/inside/prologue.mp3");
        var outsideId = AddEntity(db, EntityKind.Book, "Outside");
        AddTrack(db, outsideId, "Elsewhere", 0, path: "/other-root/elsewhere.mp3");
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

    /// <summary>A playable audiobook file titled by its name, with an optional embedded title tag.</summary>
    private static Guid AddTrack(
        PrismediaDbContext db,
        Guid bookId,
        string fileStem,
        int sortOrder,
        string? titleTag = null,
        string? path = null) {
        var id = AddEntity(db, EntityKind.AudioTrack, fileStem, bookId, sortOrder);
        db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(),
            EntityId = id,
            Role = EntityFileRole.Source,
            Path = path ?? $"/media/{bookId:N}/{fileStem}.mp3",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        db.AudioTrackDetails.Add(new AudioTrackDetailRow {
            EntityId = id,
            EmbeddedTitle = titleTag,
            TagsRecordedAt = DateTimeOffset.UtcNow
        });
        return id;
    }

    private static void AddConfirmed(
        PrismediaDbContext db,
        Guid bookId,
        Guid chapterId,
        Guid trackId,
        BookChapterMappingOrigin origin) =>
        db.BookChapterAudioMappings.Add(new BookChapterAudioMappingRow {
            Id = Guid.NewGuid(),
            BookId = bookId,
            ReadableChapterKey = chapterId.ToString("D"),
            AudioTrackEntityId = trackId,
            Origin = origin,
            UpdatedAt = DateTimeOffset.UtcNow
        });

    private static Guid AddMarker(
        PrismediaDbContext db,
        Guid entityId,
        string title,
        double seconds,
        double endSeconds,
        bool untitled = false) {
        var id = Guid.NewGuid();
        db.EntityMarkers.Add(new EntityMarkerRow {
            Id = id,
            EntityId = entityId,
            Title = title,
            Seconds = seconds,
            EndSeconds = endSeconds,
            // Container-imported chapters are the only markers that split a track into chapters.
            SourceIndex = db.EntityMarkers.Local.Count(marker => marker.EntityId == entityId),
            Untitled = untitled,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        return id;
    }
}
