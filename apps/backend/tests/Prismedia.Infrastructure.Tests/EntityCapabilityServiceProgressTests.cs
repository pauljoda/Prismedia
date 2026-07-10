using Prismedia.Application.Entities;
using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Media;

namespace Prismedia.Infrastructure.Tests;

public sealed class EntityCapabilityServiceProgressTests {
    private static readonly Guid BookId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ChapterOneId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ChapterTwoId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public async Task BookProgressCanMoveForwardFromCompletedEarlierChapter() {
        var completedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        var repository = new FakeEntityWriteRepository(new CapabilityProgress(
            currentEntityId: ChapterOneId,
            unit: ProgressUnit.Page,
            index: 1,
            total: 2,
            mode: ReaderMode.Paged,
            completedAt: completedAt,
            updatedAt: completedAt));
        var service = new EntityCapabilityService(repository, new NoSourceOwnershipReader());

        await service.UpdateProgressAsync(
            BookId,
            ChapterTwoId,
            ProgressUnit.Page,
            index: 0,
            total: 2,
            mode: ReaderMode.Paged,
            completed: null,
            reset: false,
            location: null,
            CancellationToken.None);

        var progress = Assert.IsType<Book>(repository.SavedEntity).Progress!;
        Assert.Equal(ChapterTwoId, progress.CurrentEntityId);
        Assert.Equal(0, progress.Index);
        Assert.Equal(2, progress.Total);
        Assert.Null(progress.CompletedAt);
    }

    [Fact]
    public async Task BookProgressDoesNotClearCompletedStateForEarlierCursor() {
        var completedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        var repository = new FakeEntityWriteRepository(new CapabilityProgress(
            currentEntityId: ChapterTwoId,
            unit: ProgressUnit.Page,
            index: 1,
            total: 2,
            mode: ReaderMode.Paged,
            completedAt: completedAt,
            updatedAt: completedAt));
        var service = new EntityCapabilityService(repository, new NoSourceOwnershipReader());

        await service.UpdateProgressAsync(
            BookId,
            ChapterOneId,
            ProgressUnit.Page,
            index: 0,
            total: 2,
            mode: ReaderMode.Paged,
            completed: null,
            reset: false,
            location: null,
            CancellationToken.None);

        Assert.Null(repository.SavedEntity);
        Assert.Equal(ChapterTwoId, repository.Book.Progress!.CurrentEntityId);
        Assert.Equal(completedAt, repository.Book.Progress.CompletedAt);
    }

    private sealed class FakeEntityWriteRepository : IEntityWriteRepository {
        private readonly BookChapter _chapterOne = new(ChapterOneId, "Chapter 1", coverPageId: null, parentEntityId: BookId, sortOrder: 0);
        private readonly BookChapter _chapterTwo = new(ChapterTwoId, "Chapter 2", coverPageId: null, parentEntityId: BookId, sortOrder: 1);

        public FakeEntityWriteRepository(CapabilityProgress progress) {
            Book = new Book(
                BookId,
                "Comic",
                BookType.Comic,
                coverPageId: null,
                capabilities: [progress]);
        }

        public Book Book { get; }
        public Entity? SavedEntity { get; private set; }

        public Task<Entity?> FindAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Find(id));

        public Task<Entity?> FindShallowAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Find(id));

        public Task<Guid?> FindParentIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Find(id)?.ParentEntityId);

        public Task<BookProgressPosition?> ResolveBookProgressPositionAsync(
            Guid bookId,
            Guid currentEntityId,
            int index,
            int total,
            CancellationToken cancellationToken) =>
            Task.FromResult<BookProgressPosition?>(
                bookId == BookId && currentEntityId == ChapterOneId
                    ? new BookProgressPosition(ChapterOneId, index, Total: 4)
                    : bookId == BookId && currentEntityId == ChapterTwoId
                        ? new BookProgressPosition(ChapterTwoId, index + 2, Total: 4)
                        : null);

        public Task SaveAsync(Entity entity, CancellationToken cancellationToken) {
            SavedEntity = entity;
            return Task.CompletedTask;
        }

        private Entity? Find(Guid id) =>
            id == BookId ? Book : id == ChapterOneId ? _chapterOne : id == ChapterTwoId ? _chapterTwo : null;
    }

    private sealed class NoSourceOwnershipReader : IEntitySourceOwnershipReader {
        public Task<IReadOnlySet<Guid>> ResolveAsync(
            IReadOnlyCollection<Guid> entityIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<Guid>>(new HashSet<Guid>());
    }
}
