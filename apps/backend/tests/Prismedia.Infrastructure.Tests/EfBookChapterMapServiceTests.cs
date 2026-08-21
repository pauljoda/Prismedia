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

    private static void AddSource(PrismediaDbContext db, Guid entityId) {
        db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(),
            EntityId = entityId,
            Role = EntityFileRole.Source,
            Path = $"/media/{entityId:N}.mp3",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
    }
}
