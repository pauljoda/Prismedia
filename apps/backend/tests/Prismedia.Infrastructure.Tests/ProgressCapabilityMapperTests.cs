using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Media;
using Prismedia.Infrastructure.Entities.Mappers.Capabilities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

/// <summary>Protects exact Book cursors across the database read/write boundary.</summary>
public sealed class ProgressCapabilityMapperTests {
    [Fact]
    public async Task ListeningReportRetainsLegacyReadableLocationAcrossReload() {
        await using var db = new PrismediaDbContext(
            new DbContextOptionsBuilder<PrismediaDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);
        var bookId = Guid.NewGuid();
        var trackId = Guid.NewGuid();
        var markerId = Guid.NewGuid();
        var readingAt = DateTimeOffset.Parse("2026-09-20T12:00:00Z");
        var listeningAt = readingAt.AddMinutes(5);
        db.UserEntityStates.Add(new UserEntityStateRow {
            UserId = TestUserContext.UserId,
            EntityId = bookId,
            ProgressCurrentEntityId = bookId,
            ProgressUnit = ProgressUnit.Page.ToCode(),
            ProgressIndex = 18,
            ProgressTotal = 100,
            ProgressLocation = "epubcfi(/6/14!/4/2/8)",
            ProgressUpdatedAt = readingAt,
            UpdatedAt = readingAt
        });
        await db.SaveChangesAsync();

        var mapper = new ProgressCapabilityMapper(db, TestUserContext.Admin());
        var book = new Book(bookId, "Fixture Book", BookType.Book);
        await mapper.HydrateAsync(book, CancellationToken.None);
        var progress = book.RequireCapability<CapabilityProgress>();
        Assert.Equal("epubcfi(/6/14!/4/2/8)", progress.Reading?.Location);

        progress.RecordListening(new BookListeningCheckpoint(
            trackId, markerId, 417.25, bookId, ProgressUnit.Page, 21, 100, listeningAt));
        progress.TryMoveTo(bookId, ProgressUnit.Page, 21, 100, null, listeningAt);
        await mapper.PersistAsync(book, CancellationToken.None);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var reloaded = new Book(bookId, "Fixture Book", BookType.Book);
        await mapper.HydrateAsync(reloaded, CancellationToken.None);
        var restored = reloaded.RequireCapability<CapabilityProgress>();
        Assert.Equal("epubcfi(/6/14!/4/2/8)", restored.Reading?.Location);
        Assert.Equal(18, restored.Reading?.Index);
        Assert.Equal(trackId, restored.Listening?.TrackEntityId);
        Assert.Equal(markerId, restored.Listening?.MarkerId);
        Assert.Equal(417.25, restored.Listening?.OffsetSeconds);
        Assert.Equal(21, restored.Index);
    }
}
