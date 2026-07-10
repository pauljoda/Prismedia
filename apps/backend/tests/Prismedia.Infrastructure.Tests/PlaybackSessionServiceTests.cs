using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Entities;
using Prismedia.Application.Playback;
using Prismedia.Application.Videos;
using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Media;
using Prismedia.Infrastructure.Entities;
using Prismedia.Infrastructure.Entities.Mappers;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Playback;

namespace Prismedia.Infrastructure.Tests;

/// <summary>
/// Verifies that the Jellyfin playback session path and the native entity playback path write
/// identical playback state, now that both route through <see cref="EntityCapabilityService" />.
/// Also guards the two bugs the unification fixed: Jellyfin "mark played" now records completion,
/// and repeated progress reports no longer inflate the play count.
/// </summary>
public sealed class PlaybackSessionServiceTests {
    private static readonly Guid VideoId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid MovieId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid AudioTrackId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public async Task JellyfinProgressAndNativeUpdateProduceIdenticalState() {
        var jellyfinState = await RunAsync(async (sessions, _) =>
            await sessions.ProgressAsync(
                new PlaybackSessionCommand { ItemId = VideoId, PositionTicks = 90 * TimeSpan.TicksPerSecond },
                CancellationToken.None));

        var nativeState = await RunAsync(async (_, capabilities) =>
            await capabilities.UpdatePlaybackAsync(VideoId, resumeSeconds: 90, durationSeconds: null, completed: null, CancellationToken.None));

        // Compare the deterministic playback fields; LastPlayedAt is wall-clock "now" of each run.
        Assert.Equal(nativeState!.PlayCount, jellyfinState!.PlayCount);
        Assert.Equal(nativeState.PlayDuration, jellyfinState.PlayDuration);
        Assert.Equal(nativeState.ResumeTime, jellyfinState.ResumeTime);
        Assert.Equal(nativeState.CompletedAt, jellyfinState.CompletedAt);
        Assert.Equal(TimeSpan.FromSeconds(90), jellyfinState.ResumeTime);
    }

    [Fact]
    public async Task JellyfinMarkPlayedRecordsCompletion() {
        var state = await RunAsync(async (sessions, _) =>
            await sessions.MarkPlayedAsync(VideoId, CancellationToken.None));

        Assert.NotNull(state!.CompletedAt);
        Assert.Equal(TimeSpan.Zero, state.ResumeTime);
    }

    [Theory]
    [InlineData(95, true, 1)]   // >= 95% completes and counts
    [InlineData(94, false, 0)]  // credits-friendly, but not completed yet
    [InlineData(50, false, 0)]  // mid-watch stores a resume point only
    [InlineData(2, false, 0)]   // < 5% is treated as not started
    public async Task ProgressThresholdsDeriveCompletion(int percent, bool expectCompleted, int expectPlayCount) {
        const double runtimeSeconds = 1000;
        var state = await RunAsync(
            async (sessions, _) => await sessions.ProgressAsync(
                new PlaybackSessionCommand {
                    ItemId = VideoId,
                    PositionTicks = (long)(runtimeSeconds * percent / 100 * TimeSpan.TicksPerSecond)
                },
                CancellationToken.None),
            runtimeSeconds);

        Assert.Equal(expectCompleted, state!.CompletedAt is not null);
        Assert.Equal(expectPlayCount, state.PlayCount);
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
            async (_, capabilities) => await capabilities.UpdatePlaybackAsync(
                id,
                resumeSeconds: runtimeSeconds * 0.95,
                durationSeconds: null,
                completed: null,
                CancellationToken.None),
            runtimeSeconds,
            id,
            kind);

        Assert.NotNull(state!.CompletedAt);
        Assert.Equal(1, state.PlayCount);
        Assert.Equal(TimeSpan.Zero, state.ResumeTime);
    }

    [Fact]
    public async Task AudioTrackProgressAtNinetyFivePercentDoesNotDeriveCompletion() {
        const double runtimeSeconds = 1000;

        var state = await RunAsync(
            async (_, capabilities) => await capabilities.UpdatePlaybackAsync(
                AudioTrackId,
                resumeSeconds: runtimeSeconds * 0.95,
                durationSeconds: null,
                completed: null,
                CancellationToken.None),
            runtimeSeconds,
            AudioTrackId,
            EntityKind.AudioTrack);

        Assert.Null(state!.CompletedAt);
        Assert.Equal(0, state.PlayCount);
        Assert.Equal(TimeSpan.FromSeconds(950), state.ResumeTime);
    }

    [Fact]
    public async Task ProgressAfterCompletionDoesNotClearWatchedState() {
        const double runtimeSeconds = 1000;
        var state = await RunAsync(
            async (sessions, _) => {
                await sessions.MarkPlayedAsync(VideoId, CancellationToken.None);
                await sessions.ProgressAsync(
                    new PlaybackSessionCommand {
                        ItemId = VideoId,
                        PositionTicks = (long)(runtimeSeconds * 0.5 * TimeSpan.TicksPerSecond)
                    },
                    CancellationToken.None);
            },
            runtimeSeconds);

        // A resume-range progress tick stores the position but leaves the watched flag.
        Assert.NotNull(state!.CompletedAt);
        Assert.Equal(1, state.PlayCount);
    }

    [Fact]
    public async Task CompletedPlaybackEventsIncrementRepeatedAudioPlays() {
        var (state, events) = await RunWithEventsAsync(
            async (_, capabilities) => {
                await capabilities.RecordCompletedPlaybackAsync(AudioTrackId, CancellationToken.None);
                await capabilities.RecordCompletedPlaybackAsync(AudioTrackId, CancellationToken.None);
            },
            entityId: AudioTrackId,
            kind: EntityKind.AudioTrack);

        Assert.NotNull(state!.CompletedAt);
        Assert.Equal(TimeSpan.Zero, state.ResumeTime);
        Assert.Equal(2, state.PlayCount);
        Assert.Equal(2, events.Count(e => e.Kind == PlaybackEventKind.Completed));
    }

    [Fact]
    public async Task SkippedPlaybackEventIncrementsSkipCountAndAppendsHistory() {
        var skippedAt = DateTimeOffset.Parse("2026-06-18T12:00:00Z");

        var (state, events) = await RunWithEventsAsync(
            async (_, capabilities) => await capabilities.RecordPlaybackEventAsync(
                AudioTrackId,
                PlaybackEventKind.Skipped,
                skippedAt,
                positionSeconds: 4,
                durationSeconds: 120,
                CancellationToken.None),
            entityId: AudioTrackId,
            kind: EntityKind.AudioTrack);

        Assert.Equal(0, state!.PlayCount);
        Assert.Equal(1, state.SkipCount);
        var evt = Assert.Single(events);
        Assert.Equal(AudioTrackId, evt.EntityId);
        Assert.Equal(PlaybackEventKind.Skipped, evt.Kind);
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
            new EfEntitySourceOwnershipProjection(db),
            playbackEvents: new EfPlaybackEventStore(db, TestUserContext.Admin()));

        await capabilities.RecordPlaybackEventAsync(
            AudioTrackId,
            PlaybackEventKind.Skipped,
            now,
            positionSeconds: 4,
            durationSeconds: 120,
            CancellationToken.None);

        var entity = await repository.FindAsync(AudioTrackId, CancellationToken.None);
        var evt = await db.EntityPlaybackEvents.SingleAsync();

        Assert.Equal(1, entity!.RequireCapability<CapabilityPlayback>().Value.SkipCount);
        Assert.Equal(AudioTrackId, evt.EntityId);
        Assert.Equal(PlaybackEventKind.Skipped, evt.Kind);
        Assert.Equal(now, evt.OccurredAt);
    }

    [Fact]
    public async Task RepeatedProgressDoesNotInflatePlayCount() {
        var state = await RunAsync(async (sessions, _) => {
            for (var i = 1; i <= 5; i++) {
                await sessions.ProgressAsync(
                    new PlaybackSessionCommand { ItemId = VideoId, PositionTicks = i * 10L * TimeSpan.TicksPerSecond },
                    CancellationToken.None);
            }
        });

        // Resume-only progress (no completion) never advances the play count; it only
        // increments when a session reaches the watched threshold.
        Assert.Equal(0, state!.PlayCount);
    }

    [Fact]
    public async Task StartAtPositionZeroClearsResume() {
        const double runtimeSeconds = 1000;
        var state = await RunAsync(
            async (sessions, _) => {
                // Build a resume point, then send the Start-Over signal Infuse fires: a Playing
                // report at position 0 (it reports the real resume position when resuming).
                await sessions.ProgressAsync(
                    new PlaybackSessionCommand {
                        ItemId = VideoId,
                        PositionTicks = (long)(runtimeSeconds * 0.5 * TimeSpan.TicksPerSecond)
                    },
                    CancellationToken.None);
                await sessions.StartAsync(
                    new PlaybackSessionCommand { ItemId = VideoId, PositionTicks = 0 },
                    CancellationToken.None);
            },
            runtimeSeconds);

        Assert.Equal(TimeSpan.Zero, state!.ResumeTime);
    }

    [Fact]
    public async Task StartAtResumePositionKeepsResume() {
        const double runtimeSeconds = 1000;
        var resumeTicks = (long)(runtimeSeconds * 0.5 * TimeSpan.TicksPerSecond);
        var state = await RunAsync(
            async (sessions, _) => {
                await sessions.ProgressAsync(
                    new PlaybackSessionCommand { ItemId = VideoId, PositionTicks = resumeTicks },
                    CancellationToken.None);
                // Resuming (not starting over) reports the saved position — the resume must survive.
                await sessions.StartAsync(
                    new PlaybackSessionCommand { ItemId = VideoId, PositionTicks = resumeTicks },
                    CancellationToken.None);
            },
            runtimeSeconds);

        Assert.Equal(TimeSpan.FromSeconds(500), state!.ResumeTime);
    }

    private static async Task<CapabilityPlayback.State?> RunAsync(
        Func<PlaybackSessionService, EntityCapabilityService, Task> act,
        double? runtimeSeconds = null,
        Guid? entityId = null,
        EntityKind kind = EntityKind.Video) {
        var (state, _) = await RunWithEventsAsync(act, runtimeSeconds, entityId, kind);
        return state;
    }

    private static async Task<(CapabilityPlayback.State? State, IReadOnlyList<PlaybackEventAppend> Events)> RunWithEventsAsync(
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
            new EfEntitySourceOwnershipProjection(db),
            playbackEvents: events);
        var sessions = new PlaybackSessionService(capabilities, new NoOpTranscodeSessionService());

        await act(sessions, capabilities);

        var entity = await repository.FindAsync(id, CancellationToken.None);
        return (entity?.GetCapability<CapabilityPlayback>()?.Value, events.Events);
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

    private sealed class RecordingPlaybackEventStore : IPlaybackEventStore {
        private readonly List<PlaybackEventAppend> _events = [];

        public IReadOnlyList<PlaybackEventAppend> Events => _events;

        public Task StageAsync(PlaybackEventAppend entry, CancellationToken cancellationToken) {
            _events.Add(entry);
            return Task.CompletedTask;
        }

        public Task AppendAsync(PlaybackEventAppend entry, CancellationToken cancellationToken) =>
            StageAsync(entry, cancellationToken);
    }
}
