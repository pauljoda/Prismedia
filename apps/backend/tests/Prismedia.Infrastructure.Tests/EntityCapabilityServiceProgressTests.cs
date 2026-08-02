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
    private static readonly Guid OtherBookId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    [Fact]
    public async Task BookProgressCanMoveForwardFromEarlierChapter() {
        var updatedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        var repository = new FakeEntityWriteRepository(new CapabilityProgress(
            currentEntityId: ChapterOneId,
            unit: ProgressUnit.Page,
            index: 1,
            total: 2,
            mode: ReaderMode.Paged,
            updatedAt: updatedAt));
        var service = new EntityCapabilityService(repository, new CanonicalEntityReadStub(), new TestProgressTopologyResolver());

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
    public async Task BookProgressFollowsEarlierCursorWithoutClearingConsumedCompletion() {
        var completedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        var repository = new FakeEntityWriteRepository(new CapabilityProgress(
            currentEntityId: ChapterTwoId,
            unit: ProgressUnit.Page,
            index: 1,
            total: 2,
            mode: ReaderMode.Paged,
            completedAt: completedAt,
            updatedAt: completedAt));
        var service = new EntityCapabilityService(repository, new CanonicalEntityReadStub(), new TestProgressTopologyResolver());

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

        var progress = Assert.IsType<Book>(repository.SavedEntity).Progress!;
        Assert.Equal(ChapterOneId, progress.CurrentEntityId);
        Assert.Equal(0, progress.Index);
        Assert.Equal(completedAt, progress.CompletedAt);
        Assert.Equal(2, progress.ConsumedCount);
    }

    [Fact]
    public async Task BookProgressRejectsAnUnrelatedCursorWithoutSaving() {
        var repository = new FakeEntityWriteRepository(new CapabilityProgress());
        var service = new EntityCapabilityService(repository, new CanonicalEntityReadStub(), new TestProgressTopologyResolver());

        var result = await service.UpdateProgressAsync(
            BookId,
            OtherBookId,
            ProgressUnit.Page,
            index: 1,
            total: 4,
            mode: ReaderMode.Paged,
            completed: null,
            reset: false,
            location: null,
            activitySeconds: null,
            activityKind: null,
            CancellationToken.None);

        Assert.Null(result);
        Assert.Null(repository.SavedEntity);
        Assert.Null(repository.Book.Progress!.CurrentEntityId);
    }

    [Fact]
    public async Task BookProgressReplacesAnInvalidStoredCursor() {
        var repository = new FakeEntityWriteRepository(new CapabilityProgress(
            currentEntityId: OtherBookId,
            unit: ProgressUnit.Page,
            index: 1,
            total: 4,
            mode: ReaderMode.Paged,
            updatedAt: DateTimeOffset.UtcNow.AddMinutes(-5)));
        var service = new EntityCapabilityService(repository, new CanonicalEntityReadStub(), new TestProgressTopologyResolver());

        var result = await service.UpdateProgressAsync(
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

        Assert.NotNull(result);
        Assert.Equal(ChapterOneId, Assert.IsType<Book>(repository.SavedEntity).Progress!.CurrentEntityId);
    }

    [Fact]
    public async Task SingleFileBookCurrentCursorMovesBackwardWithoutReducingCoverage() {
        var updatedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        var repository = new FakeEntityWriteRepository(new CapabilityProgress(
            currentEntityId: BookId,
            unit: ProgressUnit.Cfi,
            index: 6000,
            total: 10000,
            mode: ReaderMode.Paged,
            updatedAt: updatedAt,
            location: "epubcfi(/6/12!/4/2)"));
        var service = new EntityCapabilityService(repository, new CanonicalEntityReadStub(), new TestProgressTopologyResolver());

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

        var progress = Assert.IsType<Book>(repository.SavedEntity).Progress!;
        Assert.Equal(4000, progress.Index);
        Assert.Null(progress.Location);
        Assert.Equal(6001, progress.ConsumedCount);
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
            new CanonicalEntityReadStub(),
            new TestProgressTopologyResolver(),
            consumptionActivities: activities);

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
            activityKind: ConsumptionActivityKind.Reading,
            CancellationToken.None);

        var book = Assert.IsType<Book>(repository.SavedEntity);
        Assert.Equal(TimeSpan.FromSeconds(15), book.ConsumptionCapability?.Value.ActiveDuration);
        var activity = Assert.Single(activities.Events);
        Assert.Equal(BookId, activity.EntityId);
        Assert.Equal(ConsumptionActivityKind.Reading, activity.Kind);
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
            new CanonicalEntityReadStub(),
            new TestProgressTopologyResolver(),
            consumptionActivities: activities);

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
            activityKind: ConsumptionActivityKind.Listening,
            CancellationToken.None);

        Assert.Equal(60, Assert.Single(activities.Events).DurationSeconds);
        Assert.Equal(
            TimeSpan.FromSeconds(60),
            Assert.IsType<Book>(repository.SavedEntity).ConsumptionCapability?.Value.ActiveDuration);
    }

    [Fact]
    public async Task ImageRecordsAccessAndViewingTimeWithoutPlaybackState() {
        var image = new Image(Guid.NewGuid(), "Still");
        var repository = new SingleEntityWriteRepository(image);
        var playbackEvents = new RecordingPlaybackEventStore();
        var activityEvents = new RecordingEntityActivityStore();
        var service = new EntityCapabilityService(
            repository,
            new CanonicalEntityReadStub(),
            new TestProgressTopologyResolver(),
            consumptionEvents: playbackEvents,
            consumptionActivities: activityEvents);
        var occurredAt = DateTimeOffset.UtcNow;

        var accessed = await service.RecordAccessedAsync(
            image.Id,
            occurredAt,
            positionSeconds: null,
            durationSeconds: null,
            sessionId: "image-session",
            CancellationToken.None);
        var viewed = await service.UpdatePlaybackAsync(
            image.Id,
            resumeSeconds: null,
            durationSeconds: 30,
            completed: null,
            CancellationToken.None);

        Assert.NotNull(accessed);
        Assert.NotNull(viewed);
        Assert.Equal(2, repository.SaveCount);
        Assert.Equal(ConsumptionEventKind.Accessed, Assert.Single(playbackEvents.Events).Kind);
        var activity = Assert.Single(activityEvents.Events);
        Assert.Equal(ConsumptionActivityKind.Viewing, activity.Kind);
        Assert.Equal(30, activity.DurationSeconds);
        var consumption = image.RequireCapability<CapabilityConsumption>().Value;
        Assert.Equal(1, consumption.AccessCount);
        Assert.Equal(TimeSpan.FromSeconds(30), consumption.ActiveDuration);
        Assert.Equal(TimeSpan.Zero, consumption.ResumeTime);
        Assert.Equal(0, consumption.CompletionCount);
        Assert.Equal(0, consumption.SkipCount);
    }

    [Fact]
    public async Task ImageRejectsPlaybackPositionCompletionSkipsProgressAndMarkers() {
        var image = new Image(Guid.NewGuid(), "Still");
        var repository = new SingleEntityWriteRepository(image);
        var playbackEvents = new RecordingPlaybackEventStore();
        var activityEvents = new RecordingEntityActivityStore();
        var service = new EntityCapabilityService(
            repository,
            new CanonicalEntityReadStub(),
            new TestProgressTopologyResolver(),
            consumptionEvents: playbackEvents,
            consumptionActivities: activityEvents);
        var occurredAt = DateTimeOffset.UtcNow;

        var results = new EntityCard?[] {
            await service.UpdatePlaybackAsync(image.Id, 10, null, completed: null, CancellationToken.None),
            await service.UpdateVideoPlaybackAsync(image.Id, 10, 100, completed: false, CancellationToken.None),
            await service.RecordCompletedPlaybackAsync(image.Id, CancellationToken.None),
            await service.RecordCompletedPlaybackAsync(image.Id, occurredAt, 10, 100, CancellationToken.None),
            await service.RecordSkippedPlaybackAsync(image.Id, occurredAt, 10, 100, CancellationToken.None),
            await service.RecordPlaybackEventAsync(image.Id, ConsumptionEventKind.Completed, occurredAt, 10, 100, CancellationToken.None),
            await service.RecordPlaybackEventAsync(image.Id, ConsumptionEventKind.Skipped, occurredAt, 10, 100, CancellationToken.None),
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
                activityKind: ConsumptionActivityKind.Reading,
                CancellationToken.None),
            await service.AddMarkerAsync(image.Id, "Opening", 0, null, CancellationToken.None),
            await service.UpdateMarkerAsync(image.Id, Guid.NewGuid(), "Opening", 0, null, CancellationToken.None),
            await service.DeleteMarkerAsync(image.Id, Guid.NewGuid(), CancellationToken.None)
        };

        Assert.All(results, result => Assert.Null(result));
        Assert.Equal(0, repository.SaveCount);
        Assert.Empty(playbackEvents.Events);
        Assert.Empty(activityEvents.Events);
        var consumption = image.RequireCapability<CapabilityConsumption>().Value;
        Assert.Equal(0, consumption.AccessCount);
        Assert.Equal(TimeSpan.Zero, consumption.ActiveDuration);
        Assert.Equal(TimeSpan.Zero, consumption.ResumeTime);
        Assert.Equal(0, consumption.CompletionCount);
        Assert.Equal(0, consumption.SkipCount);
        Assert.Null(image.Progress);
        Assert.Null(image.MarkerCapability);
    }

    [Fact]
    public async Task BookPlaybackAndProgressRemainSupported() {
        var book = new Book(Guid.NewGuid(), "Book", BookType.Comic, coverPageId: null);
        var repository = new SingleEntityWriteRepository(book);
        var service = new EntityCapabilityService(repository, new CanonicalEntityReadStub(), new TestProgressTopologyResolver());

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
        Assert.Equal(TimeSpan.FromSeconds(10), book.ConsumptionCapability?.Value.ResumeTime);
        Assert.Equal(4, book.Progress?.Index);
    }

    [Fact]
    public async Task ProgressOnlyVideoScopeRecordsGeneralizedCompletionHistory() {
        var series = new VideoSeries(Guid.NewGuid(), "Series");
        var repository = new SingleEntityWriteRepository(series);
        var playbackEvents = new RecordingPlaybackEventStore();
        var service = new EntityCapabilityService(
            repository,
            new CanonicalEntityReadStub(),
            new TestProgressTopologyResolver(),
            consumptionEvents: playbackEvents);

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
        Assert.Equal(1, series.ConsumptionCapability?.Value.CompletionCount);
        Assert.Equal(ConsumptionEventKind.Completed, Assert.Single(playbackEvents.Events).Kind);
    }

    [Theory]
    [InlineData(EntityKind.Movie)]
    [InlineData(EntityKind.Video)]
    [InlineData(EntityKind.VideoEpisode)]
    public async Task PlayableVideoDefinitionsAllowMarkerAndPlaybackMutations(EntityKind kind) {
        var entity = CreatePlayableVideo(kind);
        var repository = new SingleEntityWriteRepository(entity);
        var service = new EntityCapabilityService(repository, new CanonicalEntityReadStub(), new TestProgressTopologyResolver());

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
        Assert.Equal(TimeSpan.FromSeconds(10), entity.ConsumptionCapability?.Value.ResumeTime);
    }

    [Fact]
    public async Task MissingMarkerUpdateAndDeleteDoNotAttachAnEmptyCapability() {
        var movie = new Movie(Guid.NewGuid(), "Movie", capabilities: []);
        var repository = new SingleEntityWriteRepository(movie);
        var service = new EntityCapabilityService(repository, new CanonicalEntityReadStub(), new TestProgressTopologyResolver());
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
        public Book OtherBook { get; } = new(OtherBookId, "Other comic", BookType.Comic, coverPageId: null);
        public Entity? SavedEntity { get; private set; }

        public Task<Entity?> FindAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Find(id));

        public Task<Entity?> FindShallowAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Find(id));

        public Task<Guid?> FindParentIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Find(id)?.ParentEntityId);

        public Task SaveMutableStateAsync(
            Entity entity,
            EntityMutableStateChange change,
            CancellationToken cancellationToken) {
            SavedEntity = entity;
            return Task.CompletedTask;
        }

        private Entity? Find(Guid id) =>
            id == BookId ? Book :
            id == OtherBookId ? OtherBook :
            id == ChapterOneId ? _chapterOne :
            id == ChapterTwoId ? _chapterTwo : null;
    }

    private sealed class SingleEntityWriteRepository(Entity entity) : IEntityWriteRepository {
        public int SaveCount { get; private set; }

        public Task<Entity?> FindAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(id == entity.Id ? entity : null);

        public Task<Entity?> FindShallowAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(id == entity.Id ? entity : null);

        public Task<Guid?> FindParentIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(id == entity.Id ? entity.ParentEntityId : null);

        public Task SaveMutableStateAsync(
            Entity savedEntity,
            EntityMutableStateChange change,
            CancellationToken cancellationToken) {
            Assert.Same(entity, savedEntity);
            SaveCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class CanonicalEntityReadStub : IEntityReadService {
        public Task<EntityListResponse> ListAsync(string? kind, string? query, string? cursor, bool? hideNsfw, int? limit, CancellationToken cancellationToken, Guid? referencedBy = null, string? relationshipCode = null, string? sort = null, string? sortDir = null, int? seed = null, bool? favorite = null, bool? organized = null, int? ratingMin = null, int? ratingMax = null, bool? unrated = null, string? status = null, string? bookType = null, string? bookFormat = null, bool? nsfw = null, bool? hasFile = null, bool? played = null, bool? orphaned = null, bool? wanted = null, AcquisitionStatus? acquisitionStatus = null) => Task.FromResult(new EntityListResponse([], null, 0));

        public Task<EntityCard?> GetAsync(Guid id, bool hideNsfw, CancellationToken cancellationToken) =>
            Task.FromResult<EntityCard?>(new EntityCard { Id = id, Kind = EntityKind.Book, Title = "Canonical", ParentEntityId = null, SortOrder = null, Capabilities = [], ChildrenByKind = [], Relationships = [] });

        public Task<EntityThumbnailBatchResponse> GetThumbnailsAsync(IReadOnlyList<Guid> ids, bool hideNsfw, CancellationToken cancellationToken) => Task.FromResult(new EntityThumbnailBatchResponse([]));
    }

    private sealed class TestProgressTopologyResolver : IEntityProgressTopologyResolver {
        public Task<ProgressOwnerResolution?> ResolveOwnerAsync(Guid requestedEntityId, CancellationToken cancellationToken) =>
            Task.FromResult<ProgressOwnerResolution?>(new(requestedEntityId));

        public Task<ProgressCursorResolution?> ResolveCursorAsync(Guid ownerId, Guid cursorId, CancellationToken cancellationToken) {
            var valid = ownerId != BookId || cursorId == BookId || cursorId == ChapterOneId || cursorId == ChapterTwoId;
            return Task.FromResult<ProgressCursorResolution?>(valid ? new(cursorId, cursorId) : null);
        }

        public Task<ProgressWorkPosition?> ResolveWorkPositionAsync(Guid ownerId, Guid cursorId, int index, int total, CancellationToken cancellationToken) =>
            Task.FromResult<ProgressWorkPosition?>(ownerId == BookId && cursorId == ChapterOneId
                ? new ProgressWorkPosition(ChapterOneId, index, 4)
                : ownerId == BookId && cursorId == ChapterTwoId
                    ? new ProgressWorkPosition(ChapterTwoId, index + 2, 4)
                    : null);

        public Task<IReadOnlyList<OrderedProgressScope>> ResolveOrderedScopesAsync(Guid itemId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<OrderedProgressScope>>([]);
    }

    private sealed class RecordingEntityActivityStore : IConsumptionActivityStore {
        public List<ConsumptionActivityAppend> Events { get; } = [];

        public Task StageAsync(ConsumptionActivityAppend entry, CancellationToken cancellationToken) {
            Events.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingPlaybackEventStore : IConsumptionEventStore {
        public List<ConsumptionEventAppend> Events { get; } = [];

        public Task StageAsync(ConsumptionEventAppend entry, CancellationToken cancellationToken) {
            Events.Add(entry);
            return Task.CompletedTask;
        }

        public Task AppendAsync(ConsumptionEventAppend entry, CancellationToken cancellationToken) =>
            StageAsync(entry, cancellationToken);
    }
}
