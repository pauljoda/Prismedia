using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Entities;
using Prismedia.Application.Playback;
using Prismedia.Application.Videos;
using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Media;
using Prismedia.Infrastructure.Entities;
using Prismedia.Infrastructure.Entities.Mappers;
using Prismedia.Infrastructure.Entities.Thumbnails;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Playback;

namespace Prismedia.Infrastructure.Tests;

/// <summary>
/// Verifies that playback sessions and direct entity playback mutations write identical state.
/// Also guards completion thresholds, resume semantics, and durable playback history.
/// </summary>
public sealed class PlaybackSessionServiceTests {
    private static readonly Guid VideoId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid MovieId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid AudioTrackId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public async Task SessionProgressAndEntityUpdateProduceIdenticalState() {
        var sessionState = await RunAsync(async (sessions, _) =>
            await sessions.ProgressAsync(
                new VideoPlaybackSessionCommand { EntityId = VideoId, PositionSeconds = 90 },
                CancellationToken.None));

        var nativeState = await RunAsync(async (_, capabilities) =>
            await capabilities.UpdateConsumptionAsync(VideoId, positionSeconds: 90, activitySeconds: null, completed: null, CancellationToken.None));

        // Compare the deterministic playback fields; LastActiveAt is wall-clock "now" of each run.
        Assert.Equal(nativeState!.CompletionCount, sessionState!.CompletionCount);
        Assert.Equal(nativeState.ActiveDuration, sessionState.ActiveDuration);
        Assert.Equal(nativeState.ResumeTime, sessionState.ResumeTime);
        Assert.Equal(nativeState.CompletedAt, sessionState.CompletedAt);
        Assert.Equal(TimeSpan.FromSeconds(90), sessionState.ResumeTime);
    }

    [Fact]
    public async Task SessionMediaDurationDerivesCompletionWithoutInflatingTimeWatched() {
        var (state, events) = await RunWithEventsAsync(async (sessions, _) =>
            await sessions.ProgressAsync(
                new VideoPlaybackSessionCommand {
                    EntityId = VideoId,
                    PositionSeconds = 95,
                    DurationSeconds = 100
                },
                CancellationToken.None));

        Assert.NotNull(state!.CompletedAt);
        Assert.Equal(1, state.CompletionCount);
        Assert.Equal(TimeSpan.Zero, state.ActiveDuration);
        var completed = Assert.Single(events);
        Assert.Equal(ConsumptionEventKind.Completed, completed.Kind);
        Assert.Equal(100, completed.DurationSeconds);
    }

    [Fact]
    public async Task EntityCompletionRecordsWatchedState() {
        var state = await RunAsync(async (_, capabilities) =>
            await capabilities.UpdateConsumptionAsync(
                VideoId,
                positionSeconds: 0,
                activitySeconds: null,
                completed: true,
                CancellationToken.None));

        Assert.NotNull(state!.CompletedAt);
        Assert.Equal(TimeSpan.Zero, state.ResumeTime);
    }

    [Theory]
    [InlineData(95, true, 1)]   // >= 95% completes and counts
    [InlineData(94, false, 0)]  // credits-friendly, but not completed yet
    [InlineData(50, false, 0)]  // mid-watch stores a resume point only
    [InlineData(2, false, 0)]   // < 5% is treated as not started
    public async Task ProgressThresholdsDeriveCompletion(int percent, bool expectCompleted, int expectCompletionCount) {
        const double runtimeSeconds = 1000;
        var state = await RunAsync(
            async (sessions, _) => await sessions.ProgressAsync(
                new VideoPlaybackSessionCommand {
                    EntityId = VideoId,
                    PositionSeconds = runtimeSeconds * percent / 100
                },
                CancellationToken.None),
            runtimeSeconds);

        Assert.Equal(expectCompleted, state!.CompletedAt is not null);
        Assert.Equal(expectCompletionCount, state.CompletionCount);
        if (percent is >= 5 and < 95) {
            Assert.True(state.ResumeTime > TimeSpan.Zero);
        }
    }

    [Theory]
    [InlineData(EntityKind.Video)]
    [InlineData(EntityKind.Movie)]
    public async Task VideoAndMovieProgressAtNinetyFivePercentDerivesCompletion(EntityKind kind) {
        const double runtimeSeconds = 1000;
        var id = kind == EntityKind.Movie ? MovieId : VideoId;

        var state = await RunAsync(
            async (_, capabilities) => await capabilities.UpdateConsumptionAsync(
                id,
                positionSeconds: runtimeSeconds * 0.95,
                activitySeconds: null,
                completed: null,
                CancellationToken.None),
            runtimeSeconds,
            id,
            kind);

        Assert.NotNull(state!.CompletedAt);
        Assert.Equal(1, state.CompletionCount);
        Assert.Equal(TimeSpan.Zero, state.ResumeTime);
    }

    [Fact]
    public async Task AudioTrackProgressAtNinetyFivePercentDoesNotDeriveCompletion() {
        const double runtimeSeconds = 1000;

        var state = await RunAsync(
            async (_, capabilities) => await capabilities.UpdateConsumptionAsync(
                AudioTrackId,
                positionSeconds: runtimeSeconds * 0.95,
                activitySeconds: null,
                completed: null,
                CancellationToken.None),
            runtimeSeconds,
            AudioTrackId,
            EntityKind.AudioTrack);

        Assert.Null(state!.CompletedAt);
        Assert.Equal(0, state.CompletionCount);
        Assert.Equal(TimeSpan.FromSeconds(950), state.ResumeTime);
    }

    [Fact]
    public async Task ProgressAfterCompletionDoesNotClearWatchedState() {
        const double runtimeSeconds = 1000;
        var state = await RunAsync(
            async (sessions, capabilities) => {
                await capabilities.UpdateConsumptionAsync(
                    VideoId,
                    positionSeconds: 0,
                    activitySeconds: null,
                    completed: true,
                    CancellationToken.None);
                await sessions.ProgressAsync(
                    new VideoPlaybackSessionCommand {
                        EntityId = VideoId,
                        PositionSeconds = runtimeSeconds * 0.5
                    },
                    CancellationToken.None);
            },
            runtimeSeconds);

        // A resume-range progress tick stores the position but leaves the watched flag.
        Assert.NotNull(state!.CompletedAt);
        Assert.Equal(1, state.CompletionCount);
    }

    [Fact]
    public async Task CompletedPlaybackEventsIncrementRepeatedAudioPlays() {
        var (state, events) = await RunWithEventsAsync(
            async (_, capabilities) => {
                await capabilities.RecordCompletedConsumptionAsync(AudioTrackId, CancellationToken.None);
                await capabilities.RecordCompletedConsumptionAsync(AudioTrackId, CancellationToken.None);
            },
            entityId: AudioTrackId,
            kind: EntityKind.AudioTrack);

        Assert.NotNull(state!.CompletedAt);
        Assert.Equal(TimeSpan.Zero, state.ResumeTime);
        Assert.Equal(2, state.CompletionCount);
        Assert.Equal(2, events.Count(e => e.Kind == ConsumptionEventKind.Completed));
    }

    [Fact]
    public async Task SkippedPlaybackEventIncrementsSkipCountAndAppendsHistory() {
        var skippedAt = DateTimeOffset.Parse("2026-06-18T12:00:00Z");

        var (state, events) = await RunWithEventsAsync(
            async (_, capabilities) => await capabilities.RecordConsumptionEventAsync(
                AudioTrackId,
                ConsumptionEventKind.Skipped,
                skippedAt,
                positionSeconds: 4,
                durationSeconds: 120,
                CancellationToken.None),
            entityId: AudioTrackId,
            kind: EntityKind.AudioTrack);

        Assert.Equal(0, state!.CompletionCount);
        Assert.Equal(1, state.SkipCount);
        var evt = Assert.Single(events);
        Assert.Equal(AudioTrackId, evt.EntityId);
        Assert.Equal(ConsumptionEventKind.Skipped, evt.Kind);
        Assert.Equal(skippedAt, evt.OccurredAt);
        Assert.Equal(4, evt.PositionSeconds);
        Assert.Equal(120, evt.DurationSeconds);
    }

    [Fact]
    public async Task PlaybackEventPersistsWithCapabilityMutationInEfUnitOfWork() {
        await using var db = CreateContext();
        var now = DateTimeOffset.Parse("2026-06-18T12:00:00Z");
        db.Entities.Add(new Persistence.Entities.EntityRow {
            Id = AudioTrackId,
            KindCode = EntityKindRegistry.ToCode(EntityKind.AudioTrack),
            Title = "Track",
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();

        var repository = new EfEntityRepository(db, TestUserContext.Admin(), EntityMappers.Kinds(db), EntityMappers.Capabilities(db, TestUserContext.Admin()));
        var capabilities = new EntityCapabilityService(
            repository,
            new EfEntityReadService(
                db,
                TestUserContext.Admin(),
                repository,
                ThumbnailContributors.For(db),
                new EfEntityProgressTopologyResolver(db)),
            new EfEntityProgressTopologyResolver(db),
            consumptionEvents: new EfConsumptionEventStore(db, TestUserContext.Admin()));

        await capabilities.RecordConsumptionEventAsync(
            AudioTrackId,
            ConsumptionEventKind.Skipped,
            now,
            positionSeconds: 4,
            durationSeconds: 120,
            CancellationToken.None);

        var entity = await repository.FindShallowAsync(AudioTrackId, CancellationToken.None);
        var evt = await db.EntityConsumptionEvents.SingleAsync();

        Assert.Equal(1, entity!.RequireCapability<CapabilityConsumption>().Value.SkipCount);
        Assert.Equal(AudioTrackId, evt.EntityId);
        Assert.Equal(ConsumptionEventKind.Skipped, evt.Kind);
        Assert.Equal(now, evt.OccurredAt);
    }

    [Fact]
    public async Task RepeatedProgressDoesNotInflateCompletionCount() {
        var state = await RunAsync(async (sessions, _) => {
            for (var i = 1; i <= 5; i++) {
                await sessions.ProgressAsync(
                    new VideoPlaybackSessionCommand { EntityId = VideoId, PositionSeconds = i * 10 },
                    CancellationToken.None);
            }
        });

        // Resume-only progress (no completion) never advances the completion count; it only
        // increments when a session reaches the watched threshold.
        Assert.Equal(0, state!.CompletionCount);
    }

    [Fact]
    public async Task StartAtPositionZeroClearsResume() {
        const double runtimeSeconds = 1000;
        var state = await RunAsync(
            async (sessions, _) => {
                // Build a resume point, then send the native start-over signal at position zero.
                await sessions.ProgressAsync(
                    new VideoPlaybackSessionCommand {
                        EntityId = VideoId,
                        PositionSeconds = runtimeSeconds * 0.5
                    },
                    CancellationToken.None);
                await sessions.StartAsync(
                    new VideoPlaybackSessionCommand { EntityId = VideoId, PositionSeconds = 0 },
                    CancellationToken.None);
            },
            runtimeSeconds);

        Assert.Equal(TimeSpan.Zero, state!.ResumeTime);
    }

    [Fact]
    public async Task StartAtResumePositionKeepsResume() {
        const double runtimeSeconds = 1000;
        var resumeSeconds = runtimeSeconds * 0.5;
        var state = await RunAsync(
            async (sessions, _) => {
                await sessions.ProgressAsync(
                    new VideoPlaybackSessionCommand { EntityId = VideoId, PositionSeconds = resumeSeconds },
                    CancellationToken.None);
                // Resuming (not starting over) reports the saved position — the resume must survive.
                await sessions.StartAsync(
                    new VideoPlaybackSessionCommand { EntityId = VideoId, PositionSeconds = resumeSeconds },
                    CancellationToken.None);
            },
            runtimeSeconds);

        Assert.Equal(TimeSpan.FromSeconds(500), state!.ResumeTime);
    }

    [Fact]
    public async Task RepeatedStartForOneSessionRecordsOneAccess() {
        var (state, events) = await RunWithEventsAsync(async (sessions, _) => {
            var request = new VideoPlaybackSessionCommand {
                EntityId = VideoId,
                SessionId = "session-once",
                PositionSeconds = 20
            };
            await sessions.StartAsync(request, CancellationToken.None);
            await sessions.StartAsync(request, CancellationToken.None);
        });

        Assert.Equal(1, state!.AccessCount);
        Assert.Equal(ConsumptionEventKind.Accessed, Assert.Single(events).Kind);
    }

    private static async Task<CapabilityConsumption.State?> RunAsync(
        Func<PlaybackSessionService, EntityCapabilityService, Task> act,
        double? runtimeSeconds = null,
        Guid? entityId = null,
        EntityKind kind = EntityKind.Video) {
        var (state, _) = await RunWithEventsAsync(act, runtimeSeconds, entityId, kind);
        return state;
    }

    private static async Task<(CapabilityConsumption.State? State, IReadOnlyList<ConsumptionEventAppend> Events)> RunWithEventsAsync(
        Func<PlaybackSessionService, EntityCapabilityService, Task> act,
        double? runtimeSeconds = null,
        Guid? entityId = null,
        EntityKind kind = EntityKind.Video) {
        var id = entityId ?? VideoId;
        await using var db = CreateContext();
        db.Entities.Add(new Persistence.Entities.EntityRow {
            Id = id,
            KindCode = EntityKindRegistry.ToCode(kind),
            Title = "Test Entity",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        if (runtimeSeconds is { } seconds) {
            db.EntityTechnical.Add(new Persistence.Entities.EntityTechnicalRow {
                EntityId = id,
                DurationSeconds = seconds,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }
        await db.SaveChangesAsync();

        var repository = new EfEntityRepository(db, TestUserContext.Admin(), EntityMappers.Kinds(db), EntityMappers.Capabilities(db, TestUserContext.Admin()));
        var events = new RecordingPlaybackEventStore();
        var capabilities = new EntityCapabilityService(
            repository,
            new EfEntityReadService(
                db,
                TestUserContext.Admin(),
                repository,
                ThumbnailContributors.For(db),
                new EfEntityProgressTopologyResolver(db)),
            new EfEntityProgressTopologyResolver(db),
            consumptionEvents: events);
        var sessions = new PlaybackSessionService(capabilities, new NoOpTranscodeSessionService());

        await act(sessions, capabilities);

        var entity = await repository.FindShallowAsync(id, CancellationToken.None);
        return (entity?.GetCapability<CapabilityConsumption>()?.Value, events.Events);
    }

    private static PrismediaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private sealed class NoOpTranscodeSessionService : ITranscodeSessionService {
        public void Register(string playSessionId, Guid itemId) { }
        public void Ping(string playSessionId) { }
        public Task CancelAsync(string playSessionId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<int> CancelAllAsync(CancellationToken cancellationToken) => Task.FromResult(0);
        public IReadOnlySet<Guid> LiveItemIds(TimeSpan within) => new HashSet<Guid>();
        public int ReapStaleSessions(TimeSpan ttl) => 0;
    }

    private sealed class RecordingPlaybackEventStore : IConsumptionEventStore {
        private readonly List<ConsumptionEventAppend> _events = [];

        public IReadOnlyList<ConsumptionEventAppend> Events => _events;

        public Task<bool> ContainsSessionEventAsync(
            Guid entityId,
            string sessionId,
            ConsumptionEventKind kind,
            CancellationToken cancellationToken) =>
            Task.FromResult(_events.Any(entry => entry.SessionId == sessionId && entry.Kind == kind));

        public Task StageAsync(ConsumptionEventAppend entry, CancellationToken cancellationToken) {
            _events.Add(entry);
            return Task.CompletedTask;
        }

    }
}
