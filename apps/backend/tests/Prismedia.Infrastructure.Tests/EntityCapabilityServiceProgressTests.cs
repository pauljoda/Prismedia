using Prismedia.Application.Entities;
using Prismedia.Application.Playback;
using Prismedia.Contracts.Entities;
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
            activitySeconds: null,
            activityKind: null,
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
            activitySeconds: null,
            activityKind: null,
            CancellationToken.None);

        Assert.Null(repository.SavedEntity);
        Assert.Equal(ChapterTwoId, repository.Book.Progress!.CurrentEntityId);
        Assert.Equal(completedAt, repository.Book.Progress.CompletedAt);
    }

    [Fact]
    public async Task SingleFileBookProgressDoesNotMoveBackwardWithinTheCanonicalCursor() {
        var updatedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        var repository = new FakeEntityWriteRepository(new CapabilityProgress(
            currentEntityId: BookId,
            unit: ProgressUnit.Cfi,
            index: 6000,
            total: 10000,
            mode: ReaderMode.Paged,
            updatedAt: updatedAt,
            location: "epubcfi(/6/12!/4/2)"));
        var service = new EntityCapabilityService(repository, new NoSourceOwnershipReader());

        await service.UpdateProgressAsync(
            BookId,
            BookId,
            ProgressUnit.Cfi,
            index: 4000,
            total: 10000,
            mode: ReaderMode.Paged,
            completed: null,
            reset: false,
            location: null,
            activitySeconds: null,
            activityKind: null,
            CancellationToken.None);

        Assert.Null(repository.SavedEntity);
        Assert.Equal(6000, repository.Book.Progress!.Index);
        Assert.Equal("epubcfi(/6/12!/4/2)", repository.Book.Progress.Location);
    }

    [Fact]
    public async Task ReadingHeartbeatAccumulatesActivityWithoutAdvancingTheCursor() {
        var progress = new CapabilityProgress(
            currentEntityId: ChapterOneId,
            unit: ProgressUnit.Page,
            index: 1,
            total: 2,
            mode: ReaderMode.Paged,
            updatedAt: DateTimeOffset.UtcNow.AddMinutes(-1));
        var repository = new FakeEntityWriteRepository(progress);
        var activities = new RecordingEntityActivityStore();
        var service = new EntityCapabilityService(
            repository,
            new NoSourceOwnershipReader(),
            activityEvents: activities);

        await service.UpdateProgressAsync(
            BookId,
            ChapterOneId,
            ProgressUnit.Page,
            index: 1,
            total: 2,
            mode: ReaderMode.Paged,
            completed: null,
            reset: false,
            location: null,
            activitySeconds: 15,
            activityKind: BookActivityKind.Reading,
            CancellationToken.None);

        var book = Assert.IsType<Book>(repository.SavedEntity);
        Assert.Equal(TimeSpan.FromSeconds(15), book.PlaybackCapability?.Value.PlayDuration);
        var activity = Assert.Single(activities.Events);
        Assert.Equal(BookId, activity.EntityId);
        Assert.Equal(BookActivityKind.Reading, activity.Kind);
        Assert.Equal(15, activity.DurationSeconds);
    }

    [Fact]
    public async Task BookHeartbeatCapsOneClientReportToOneMinute() {
        var repository = new FakeEntityWriteRepository(new CapabilityProgress(
            currentEntityId: ChapterOneId,
            unit: ProgressUnit.Page,
            index: 1,
            total: 2,
            mode: ReaderMode.Paged,
            updatedAt: DateTimeOffset.UtcNow.AddMinutes(-1)));
        var activities = new RecordingEntityActivityStore();
        var service = new EntityCapabilityService(
            repository,
            new NoSourceOwnershipReader(),
            activityEvents: activities);

        await service.UpdateProgressAsync(
            BookId,
            ChapterOneId,
            ProgressUnit.Page,
            index: 1,
            total: 2,
            mode: ReaderMode.Paged,
            completed: null,
            reset: false,
            location: null,
            activitySeconds: 600,
            activityKind: BookActivityKind.Listening,
            CancellationToken.None);

        Assert.Equal(60, Assert.Single(activities.Events).DurationSeconds);
        Assert.Equal(
            TimeSpan.FromSeconds(60),
            Assert.IsType<Book>(repository.SavedEntity).PlaybackCapability?.Value.PlayDuration);
    }

    [Fact]
    public async Task UnsupportedImageCapabilityMutationsDoNotSaveOrStageEvents() {
        var image = new Image(Guid.NewGuid(), "Still");
        var repository = new SingleEntityWriteRepository(image);
        var playbackEvents = new RecordingPlaybackEventStore();
        var activityEvents = new RecordingEntityActivityStore();
        var service = new EntityCapabilityService(
            repository,
            new NoSourceOwnershipReader(),
            playbackEvents: playbackEvents,
            activityEvents: activityEvents);
        var occurredAt = DateTimeOffset.UtcNow;

        var results = new EntityCard?[] {
            await service.UpdatePlaybackAsync(image.Id, 10, 1, completed: null, CancellationToken.None),
            await service.UpdateVideoPlaybackAsync(image.Id, 10, 100, completed: false, CancellationToken.None),
            await service.RecordCompletedPlaybackAsync(image.Id, CancellationToken.None),
            await service.RecordCompletedPlaybackAsync(image.Id, occurredAt, 10, 100, CancellationToken.None),
            await service.RecordSkippedPlaybackAsync(image.Id, occurredAt, 10, 100, CancellationToken.None),
            await service.RecordPlaybackEventAsync(image.Id, PlaybackEventKind.Completed, occurredAt, 10, 100, CancellationToken.None),
            await service.RecordPlaybackEventAsync(image.Id, PlaybackEventKind.Skipped, occurredAt, 10, 100, CancellationToken.None),
            await service.UpdateProgressAsync(
                image.Id,
                image.Id,
                ProgressUnit.Item,
                index: 0,
                total: 1,
                mode: null,
                completed: true,
                reset: false,
                location: null,
                activitySeconds: 30,
                activityKind: BookActivityKind.Reading,
                CancellationToken.None),
            await service.AddMarkerAsync(image.Id, "Opening", 0, null, CancellationToken.None),
            await service.UpdateMarkerAsync(image.Id, Guid.NewGuid(), "Opening", 0, null, CancellationToken.None),
            await service.DeleteMarkerAsync(image.Id, Guid.NewGuid(), CancellationToken.None)
        };

        Assert.All(results, result => Assert.Null(result));
        Assert.Equal(0, repository.SaveCount);
        Assert.Empty(playbackEvents.Events);
        Assert.Empty(activityEvents.Events);
        Assert.Null(image.PlaybackCapability);
        Assert.Null(image.Progress);
        Assert.Null(image.MarkerCapability);
    }

    [Fact]
    public async Task BookPlaybackAndProgressRemainSupported() {
        var book = new Book(Guid.NewGuid(), "Book", BookType.Comic, coverPageId: null);
        var repository = new SingleEntityWriteRepository(book);
        var service = new EntityCapabilityService(repository, new NoSourceOwnershipReader());

        var playback = await service.UpdatePlaybackAsync(
            book.Id,
            resumeSeconds: 10,
            durationSeconds: null,
            completed: null,
            CancellationToken.None);
        var progress = await service.UpdateProgressAsync(
            book.Id,
            book.Id,
            ProgressUnit.Page,
            index: 4,
            total: 10,
            mode: ReaderMode.Paged,
            completed: null,
            reset: false,
            location: null,
            activitySeconds: null,
            activityKind: null,
            CancellationToken.None);

        Assert.NotNull(playback);
        Assert.NotNull(progress);
        Assert.Equal(2, repository.SaveCount);
        Assert.Equal(TimeSpan.FromSeconds(10), book.PlaybackCapability?.Value.ResumeTime);
        Assert.Equal(4, book.Progress?.Index);
    }

    [Fact]
    public async Task ProgressOnlyVideoScopeCanCompleteWithoutCreatingPlaybackHistory() {
        var series = new VideoSeries(Guid.NewGuid(), "Series");
        var repository = new SingleEntityWriteRepository(series);
        var playbackEvents = new RecordingPlaybackEventStore();
        var service = new EntityCapabilityService(
            repository,
            new NoSourceOwnershipReader(),
            playbackEvents: playbackEvents);

        var result = await service.UpdateProgressAsync(
            series.Id,
            series.Id,
            ProgressUnit.Item,
            index: 0,
            total: 1,
            mode: null,
            completed: true,
            reset: false,
            location: null,
            activitySeconds: null,
            activityKind: null,
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(1, repository.SaveCount);
        Assert.NotNull(series.Progress?.CompletedAt);
        Assert.Null(series.PlaybackCapability);
        Assert.Empty(playbackEvents.Events);
    }

    [Theory]
    [InlineData(EntityKind.Movie)]
    [InlineData(EntityKind.Video)]
    [InlineData(EntityKind.VideoEpisode)]
    public async Task PlayableVideoDefinitionsAllowMarkerAndPlaybackMutations(EntityKind kind) {
        var entity = CreatePlayableVideo(kind);
        var repository = new SingleEntityWriteRepository(entity);
        var service = new EntityCapabilityService(repository, new NoSourceOwnershipReader());

        var marker = await service.AddMarkerAsync(entity.Id, "Opening", 0, null, CancellationToken.None);
        var playback = await service.UpdatePlaybackAsync(
            entity.Id,
            resumeSeconds: 10,
            durationSeconds: null,
            completed: null,
            CancellationToken.None);

        Assert.NotNull(marker);
        Assert.NotNull(playback);
        Assert.Equal(2, repository.SaveCount);
        Assert.Single(entity.MarkerCapability!.Items);
        Assert.Equal(TimeSpan.FromSeconds(10), entity.PlaybackCapability?.Value.ResumeTime);
    }

    [Theory]
    [InlineData(EntityKind.Movie, 0)]
    [InlineData(EntityKind.Video, 0)]
    [InlineData(EntityKind.VideoEpisode, 1)]
    public async Task OnlyEpisodicPlayableKindsResolveContainerProgressScopes(EntityKind kind, int expectedScopeCalls) {
        var entity = CreatePlayableVideo(kind);
        var repository = new SingleEntityWriteRepository(entity);
        var service = new EntityCapabilityService(repository, new NoSourceOwnershipReader());

        await service.UpdateVideoPlaybackAsync(
            entity.Id,
            positionSeconds: 90,
            mediaDurationSeconds: 100,
            completed: true,
            CancellationToken.None);

        Assert.Equal(expectedScopeCalls, repository.VideoProgressScopeCalls);
    }

    [Fact]
    public async Task MissingMarkerUpdateAndDeleteDoNotAttachAnEmptyCapability() {
        var movie = new Movie(Guid.NewGuid(), "Movie", capabilities: []);
        var repository = new SingleEntityWriteRepository(movie);
        var service = new EntityCapabilityService(repository, new NoSourceOwnershipReader());
        var markerId = Guid.NewGuid();

        var updated = await service.UpdateMarkerAsync(movie.Id, markerId, "Opening", 0, null, CancellationToken.None);
        var deleted = await service.DeleteMarkerAsync(movie.Id, markerId, CancellationToken.None);

        Assert.Null(updated);
        Assert.Null(deleted);
        Assert.Equal(0, repository.SaveCount);
        Assert.Null(movie.MarkerCapability);
    }

    private static Entity CreatePlayableVideo(EntityKind kind) => kind switch {
        EntityKind.Movie => new Movie(Guid.NewGuid(), "Movie"),
        EntityKind.Video => new Video(Guid.NewGuid(), "Video"),
        EntityKind.VideoEpisode => new VideoEpisode(Guid.NewGuid(), "Episode", Guid.NewGuid()),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

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

        public Task<IReadOnlyList<VideoProgressScopePosition>> ResolveVideoProgressScopesAsync(
            Guid videoId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<VideoProgressScopePosition>>([]);

        public Task SaveAsync(Entity entity, CancellationToken cancellationToken) {
            SavedEntity = entity;
            return Task.CompletedTask;
        }

        private Entity? Find(Guid id) =>
            id == BookId ? Book : id == ChapterOneId ? _chapterOne : id == ChapterTwoId ? _chapterTwo : null;
    }

    private sealed class SingleEntityWriteRepository(Entity entity) : IEntityWriteRepository {
        public int SaveCount { get; private set; }
        public int VideoProgressScopeCalls { get; private set; }

        public Task<Entity?> FindAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(id == entity.Id ? entity : null);

        public Task<Entity?> FindShallowAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(id == entity.Id ? entity : null);

        public Task<Guid?> FindParentIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(id == entity.Id ? entity.ParentEntityId : null);

        public Task<BookProgressPosition?> ResolveBookProgressPositionAsync(
            Guid bookId,
            Guid currentEntityId,
            int index,
            int total,
            CancellationToken cancellationToken) =>
            Task.FromResult<BookProgressPosition?>(null);

        public Task<IReadOnlyList<VideoProgressScopePosition>> ResolveVideoProgressScopesAsync(
            Guid videoId,
            CancellationToken cancellationToken) {
            VideoProgressScopeCalls++;
            return Task.FromResult<IReadOnlyList<VideoProgressScopePosition>>([]);
        }

        public Task SaveAsync(Entity savedEntity, CancellationToken cancellationToken) {
            Assert.Same(entity, savedEntity);
            SaveCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class NoSourceOwnershipReader : IEntitySourceOwnershipReader {
        public Task<IReadOnlySet<Guid>> ResolveAsync(
            IReadOnlyCollection<Guid> entityIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<Guid>>(new HashSet<Guid>());
    }

    private sealed class RecordingEntityActivityStore : IEntityActivityStore {
        public List<EntityActivityAppend> Events { get; } = [];

        public Task StageAsync(EntityActivityAppend entry, CancellationToken cancellationToken) {
            Events.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingPlaybackEventStore : IPlaybackEventStore {
        public List<PlaybackEventAppend> Events { get; } = [];

        public Task StageAsync(PlaybackEventAppend entry, CancellationToken cancellationToken) {
            Events.Add(entry);
            return Task.CompletedTask;
        }

        public Task AppendAsync(PlaybackEventAppend entry, CancellationToken cancellationToken) =>
            StageAsync(entry, cancellationToken);
    }
}
