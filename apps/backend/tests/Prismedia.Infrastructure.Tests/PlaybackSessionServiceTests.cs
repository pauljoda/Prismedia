using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Entities;
using Prismedia.Application.Videos;
using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Media;
using Prismedia.Infrastructure.Entities;
using Prismedia.Infrastructure.Entities.Mappers;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Tests;

/// <summary>
/// Verifies that the Jellyfin playback session path and the native entity playback path write
/// identical playback state, now that both route through <see cref="EntityCapabilityService" />.
/// Also guards the two bugs the unification fixed: Jellyfin "mark played" now records completion,
/// and repeated progress reports no longer inflate the play count.
/// </summary>
public sealed class PlaybackSessionServiceTests {
    private static readonly Guid VideoId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

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
    [InlineData(95, true, 1)]   // >= 90% completes and counts
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
        if (percent is >= 5 and < 90) {
            Assert.True(state.ResumeTime > TimeSpan.Zero);
        }
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
        double? runtimeSeconds = null) {
        await using var db = CreateContext();
        db.Entities.Add(new Persistence.Entities.EntityRow {
            Id = VideoId,
            KindCode = EntityKindRegistry.ToCode(EntityKind.Video),
            Title = "Test Video",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        if (runtimeSeconds is { } seconds) {
            db.EntityTechnical.Add(new Persistence.Entities.EntityTechnicalRow {
                EntityId = VideoId,
                DurationSeconds = seconds,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }
        await db.SaveChangesAsync();

        var repository = new EfEntityRepository(db, EntityMappers.Kinds(db), EntityMappers.Capabilities(db));
        var capabilities = new EntityCapabilityService(repository);
        var sessions = new PlaybackSessionService(capabilities, new NoOpTranscodeSessionService());

        await act(sessions, capabilities);

        var video = await repository.FindAsync<Video>(VideoId, CancellationToken.None);
        return video?.PlaybackCapability?.Value;
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
    }
}
