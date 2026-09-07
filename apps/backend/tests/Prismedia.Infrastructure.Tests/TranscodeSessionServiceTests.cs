using Prismedia.Infrastructure.Videos;

namespace Prismedia.Infrastructure.Tests;

/// <summary>
/// Covers the liveness window and stale-session reaping that the transcode reaper relies on to
/// cancel abandoned ffmpeg jobs.
/// </summary>
public sealed class TranscodeSessionServiceTests {
    [Fact]
    public void LiveItemIdsIncludesARecentlyRegisteredSession() {
        var clock = new TestClock();
        var service = new TranscodeSessionService(clock);
        var item = Guid.NewGuid();
        service.Register("s1", item);

        Assert.Contains(item, service.LiveItemIds(TimeSpan.FromHours(1)));
    }

    [Fact]
    public void LiveItemIdsExcludesSessionsOlderThanTheWindow() {
        var clock = new TestClock();
        var service = new TranscodeSessionService(clock);
        var item = Guid.NewGuid();
        service.Register("s1", item);
        clock.Advance(TimeSpan.FromMinutes(2));

        Assert.DoesNotContain(item, service.LiveItemIds(TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void LiveItemIdsIgnoresHeartbeatsWithNoAssociatedItem() {
        var clock = new TestClock();
        var service = new TranscodeSessionService(clock);
        service.Ping("anonymous"); // a ping before any item is registered

        Assert.Empty(service.LiveItemIds(TimeSpan.FromHours(1)));
    }

    [Fact]
    public void ReapStaleSessionsRemovesOnlyAbandonedSessions() {
        var clock = new TestClock();
        var service = new TranscodeSessionService(clock);
        service.Register("stale", Guid.NewGuid());
        clock.Advance(TimeSpan.FromMinutes(2));
        var freshItem = Guid.NewGuid();
        service.Register("fresh", freshItem);

        var removed = service.ReapStaleSessions(TimeSpan.FromMinutes(1));

        Assert.Equal(1, removed);
        Assert.Contains(freshItem, service.LiveItemIds(TimeSpan.FromHours(1)));
    }
    private sealed class TestClock : TimeProvider {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan elapsed) => _now += elapsed;
    }
}
