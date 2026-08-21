using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Books;
using Prismedia.Application.Entities;
using Prismedia.Contracts.Books;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Media.Books;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class EfBookChapterMappingServiceTests {
    [Fact]
    public async Task ReplacesTheBooksExplicitChapterMappings() {
        await using var db = CreateContext();
        var bookId = AddEntity(db, EntityKind.Book, "Book");
        var firstTrackId = AddEntity(db, EntityKind.AudioTrack, "Part 1", bookId, 0);
        var secondTrackId = AddEntity(db, EntityKind.AudioTrack, "Part 2", bookId, 1);
        AddSource(db, firstTrackId);
        AddSource(db, secondTrackId);
        await db.SaveChangesAsync();
        var service = CreateService(db, new VisibleEntityScope());

        var firstSave = await service.ReplaceAsync(
            bookId,
            new ReplaceBookChapterMappingsRequest([
                new BookChapterAudioMapping("Text/prologue.xhtml", firstTrackId),
                new BookChapterAudioMapping("Text/chapter-01.xhtml", secondTrackId)
            ]),
            CancellationToken.None);
        var secondSave = await service.ReplaceAsync(
            bookId,
            new ReplaceBookChapterMappingsRequest([
                new BookChapterAudioMapping("Text/prologue.xhtml", secondTrackId)
            ]),
            CancellationToken.None);

        Assert.Equal(BookChapterMappingSaveStatus.Saved, firstSave.Status);
        Assert.Equal(BookChapterMappingSaveStatus.Saved, secondSave.Status);
        var response = await service.GetAsync(bookId, CancellationToken.None);
        var mapping = Assert.Single(response!.Mappings);
        Assert.Equal("Text/prologue.xhtml", mapping.ReadableChapterKey);
        Assert.Equal(secondTrackId, mapping.AudioTrackId);
    }

    [Fact]
    public async Task RejectsDuplicateTracksAndTracksOwnedByAnotherBook() {
        await using var db = CreateContext();
        var bookId = AddEntity(db, EntityKind.Book, "Book");
        var otherBookId = AddEntity(db, EntityKind.Book, "Other Book");
        var ownedTrackId = AddEntity(db, EntityKind.AudioTrack, "Owned Part", bookId, 0);
        var foreignTrackId = AddEntity(db, EntityKind.AudioTrack, "Foreign Part", otherBookId, 0);
        AddSource(db, ownedTrackId);
        AddSource(db, foreignTrackId);
        await db.SaveChangesAsync();
        var service = CreateService(db, new VisibleEntityScope());

        var duplicate = await service.ReplaceAsync(
            bookId,
            new ReplaceBookChapterMappingsRequest([
                new BookChapterAudioMapping("Text/prologue.xhtml", ownedTrackId),
                new BookChapterAudioMapping("Text/chapter-01.xhtml", ownedTrackId)
            ]),
            CancellationToken.None);
        var foreign = await service.ReplaceAsync(
            bookId,
            new ReplaceBookChapterMappingsRequest([
                new BookChapterAudioMapping("Text/prologue.xhtml", foreignTrackId)
            ]),
            CancellationToken.None);

        Assert.Equal(BookChapterMappingSaveStatus.Invalid, duplicate.Status);
        Assert.Equal(BookChapterMappingSaveStatus.Invalid, foreign.Status);
        Assert.Empty(db.BookChapterAudioMappings);
    }

    [Fact]
    public async Task RejectsAggregateTracksWithoutSourceMedia() {
        await using var db = CreateContext();
        var bookId = AddEntity(db, EntityKind.Book, "Book");
        var aggregateTrackId = AddEntity(db, EntityKind.AudioTrack, "Book", bookId, 0);
        await db.SaveChangesAsync();
        var service = CreateService(db, new VisibleEntityScope());

        var result = await service.ReplaceAsync(
            bookId,
            new ReplaceBookChapterMappingsRequest([
                new BookChapterAudioMapping("Text/prologue.xhtml", aggregateTrackId)
            ]),
            CancellationToken.None);

        Assert.Equal(BookChapterMappingSaveStatus.Invalid, result.Status);
        Assert.Empty(db.BookChapterAudioMappings);
    }

    [Fact]
    public async Task HiddenOrNonBookEntitiesBehaveAsMissing() {
        await using var db = CreateContext();
        var videoId = AddEntity(db, EntityKind.Video, "Video");
        await db.SaveChangesAsync();
        var hiddenService = CreateService(db, new HiddenEntityScope());
        var visibleService = CreateService(db, new VisibleEntityScope());

        var hidden = await hiddenService.GetAsync(videoId, CancellationToken.None);
        var nonBook = await visibleService.ReplaceAsync(
            videoId,
            new ReplaceBookChapterMappingsRequest([]),
            CancellationToken.None);

        Assert.Null(hidden);
        Assert.Equal(BookChapterMappingSaveStatus.NotFound, nonBook.Status);
    }

    private static PrismediaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase($"book-chapter-mapping-{Guid.NewGuid():N}")
            .Options);

    private static EfBookChapterMappingService CreateService(
        PrismediaDbContext db,
        IEntityVisibilityChecker visibility) =>
        new(db, visibility, new EfBookChapterMapService(db, new EpubBookContentsCache()));

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

    private sealed class VisibleEntityScope : IEntityVisibilityChecker {
        public Task<bool> IsVisibleAsync(Guid entityId, CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }

    private sealed class HiddenEntityScope : IEntityVisibilityChecker {
        public Task<bool> IsVisibleAsync(Guid entityId, CancellationToken cancellationToken) =>
            Task.FromResult(false);
    }
}
