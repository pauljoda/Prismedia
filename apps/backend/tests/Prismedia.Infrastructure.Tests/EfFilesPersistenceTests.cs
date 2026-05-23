using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Files;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class EfFilesPersistenceTests {
    [Fact]
    public async Task ListHiddenPathsAsyncDoesNotHideSafeAncestorsOfNsfwSourceFiles() {
        await using var db = CreateContext();
        var chapterId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var rootPath = Path.GetFullPath("/media/safe");
        var comicsPath = Path.Combine(rootPath, "Comics");
        var bookPath = Path.Combine(comicsPath, "The Promised Neverland");
        var chapterPath = Path.Combine(bookPath, "Chapter 001.cbz");
        var now = DateTimeOffset.UtcNow;

        db.Entities.Add(new EntityRow {
            Id = chapterId,
            KindCode = EntityKindRegistry.BookChapter.Code,
            Title = "Chapter 001",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            EntityId = chapterId,
            Role = EntityFileRole.Source,
            Path = chapterPath,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.EntityFlags.Add(new EntityFlagRow {
            EntityId = chapterId,
            IsNsfw = true,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();

        var persistence = new EfFilesPersistence(db);

        var hidden = await persistence.ListHiddenPathsAsync(
            [rootPath, comicsPath, bookPath, chapterPath],
            CancellationToken.None);

        Assert.DoesNotContain(rootPath, hidden);
        Assert.DoesNotContain(comicsPath, hidden);
        Assert.DoesNotContain(bookPath, hidden);
        Assert.Contains(chapterPath, hidden);
    }

    private static PrismediaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase($"files-persistence-{Guid.NewGuid():N}")
            .Options);
}
