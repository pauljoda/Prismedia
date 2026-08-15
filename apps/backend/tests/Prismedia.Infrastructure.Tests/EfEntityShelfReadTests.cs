using Microsoft.EntityFrameworkCore;
using Prismedia.Contracts.Entities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Entities;
using Prismedia.Infrastructure.Entities.Mappers;
using Prismedia.Infrastructure.Entities.Thumbnails;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class EfEntityShelfReadTests {
    [Fact]
    public async Task ShelfReadSkipsTotalCountAndExpensiveThumbnailContributors() {
        await using var db = new PrismediaDbContext(
            new DbContextOptionsBuilder<PrismediaDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);
        var now = DateTimeOffset.UtcNow;
        var bookId = Guid.NewGuid();
        var olderBookId = Guid.NewGuid();
        var completedBookId = Guid.NewGuid();
        db.Entities.AddRange(new EntityRow {
            Id = bookId,
            KindCode = EntityKind.Book.ToCode(),
            Title = "Resume book",
            CreatedAt = now,
            UpdatedAt = now,
        }, new EntityRow {
            Id = olderBookId,
            KindCode = EntityKind.Book.ToCode(),
            Title = "Older resume book",
            CreatedAt = now.AddDays(-1),
            UpdatedAt = now.AddDays(-1),
        }, new EntityRow {
            Id = completedBookId,
            KindCode = EntityKind.Book.ToCode(),
            Title = "Completed book",
            CreatedAt = now.AddDays(-2),
            UpdatedAt = now,
        });
        db.BookDetails.AddRange(
            new BookDetailRow { EntityId = bookId },
            new BookDetailRow { EntityId = olderBookId },
            new BookDetailRow { EntityId = completedBookId });
        db.UserEntityStates.AddRange(new UserEntityStateRow {
            UserId = TestUserContext.UserId,
            EntityId = bookId,
            ProgressIndex = 5,
            ProgressTotal = 10,
            LastActiveAt = now,
            UpdatedAt = now,
        }, new UserEntityStateRow {
            UserId = TestUserContext.UserId,
            EntityId = olderBookId,
            ProgressIndex = 2,
            ProgressTotal = 10,
            LastActiveAt = now.AddDays(-1),
            UpdatedAt = now.AddDays(-1),
        }, new UserEntityStateRow {
            UserId = TestUserContext.UserId,
            EntityId = completedBookId,
            ProgressIndex = 10,
            ProgressTotal = 10,
            ProgressCompletedAt = now.AddMinutes(1),
            LastActiveAt = now.AddMinutes(1),
            UpdatedAt = now.AddMinutes(1),
        });
        await db.SaveChangesAsync();

        var contributor = new CountingContributor();
        var user = TestUserContext.Admin();
        var repository = new EfEntityRepository(
            db,
            user,
            EntityMappers.Kinds(db),
            EntityMappers.Capabilities(db, user));
        var service = new EfEntityReadService(
            db,
            user,
            repository,
            [contributor],
            new EfEntityProgressTopologyResolver(db));

        var shelf = await service.ListShelfAsync(
            new EntityListQuery {
                Status = "in-progress",
                Sort = EntityListSort.LastActive,
                SortDirection = EntitySortDirection.Descending,
                Limit = 20,
            },
            CancellationToken.None);

        Assert.Equal([bookId, olderBookId], shelf.Items.Select(item => item.Id));
        Assert.Null(shelf.NextCursor);
        Assert.Equal(0, contributor.InvocationCount);
    }

    private sealed class CountingContributor : IThumbnailContributor {
        public int InvocationCount { get; private set; }

        public Task ContributeAsync(
            ThumbnailContributions contributions,
            CancellationToken cancellationToken) {
            InvocationCount++;
            return Task.CompletedTask;
        }
    }
}
