using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Infrastructure.Acquisition;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Tests;

/// <summary>
/// Pins the indexer health ladder: consecutive failures escalate through growing suppression
/// windows (capped at the 24h ceiling), a success steps down exactly one level and clears the
/// window, and an indexer with no recorded failures has no row at all.
/// </summary>
public sealed class EfIndexerStatusStoreTests {
    [Fact]
    public async Task ConsecutiveFailuresClimbTheLadderAndOpenGrowingWindows() {
        await using var db = CreateContext();
        var store = new EfIndexerStatusStore(db);
        var id = Guid.NewGuid();

        await store.RecordFailureAsync(id, "timeout", CancellationToken.None);
        var first = (await store.GetAllAsync(CancellationToken.None))[id];
        Assert.Equal(1, first.EscalationLevel);
        Assert.True(first.IsDisabledAt(DateTimeOffset.UtcNow));
        Assert.False(first.IsDisabledAt(DateTimeOffset.UtcNow + TimeSpan.FromMinutes(2)));

        await store.RecordFailureAsync(id, "timeout", CancellationToken.None);
        var second = (await store.GetAllAsync(CancellationToken.None))[id];
        Assert.Equal(2, second.EscalationLevel);
        Assert.True(second.IsDisabledAt(DateTimeOffset.UtcNow + TimeSpan.FromMinutes(2)));
        Assert.Equal("timeout", second.LastFailureMessage);
    }

    [Fact]
    public async Task EscalationCapsAtTheLadderCeiling() {
        await using var db = CreateContext();
        var store = new EfIndexerStatusStore(db);
        var id = Guid.NewGuid();

        for (var i = 0; i < 20; i++) {
            await store.RecordFailureAsync(id, "down", CancellationToken.None);
        }

        var health = (await store.GetAllAsync(CancellationToken.None))[id];
        Assert.Equal(IndexerBackoffLadder.MaxLevel, health.EscalationLevel);
        // The ceiling window is 24 hours, never more.
        Assert.True(health.IsDisabledAt(DateTimeOffset.UtcNow + TimeSpan.FromHours(23)));
        Assert.False(health.IsDisabledAt(DateTimeOffset.UtcNow + TimeSpan.FromHours(25)));
    }

    [Fact]
    public async Task ASuccessStepsDownOneLevelAndClearsTheWindow() {
        await using var db = CreateContext();
        var store = new EfIndexerStatusStore(db);
        var id = Guid.NewGuid();

        await store.RecordFailureAsync(id, "down", CancellationToken.None);
        await store.RecordFailureAsync(id, "down", CancellationToken.None);
        await store.RecordSuccessAsync(id, CancellationToken.None);

        var health = (await store.GetAllAsync(CancellationToken.None))[id];
        Assert.Equal(1, health.EscalationLevel);
        Assert.False(health.IsDisabledAt(DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task AHealthyIndexerHasNoRowAndSuccessKeepsItThatWay() {
        await using var db = CreateContext();
        var store = new EfIndexerStatusStore(db);
        var id = Guid.NewGuid();

        await store.RecordSuccessAsync(id, CancellationToken.None);

        Assert.Empty(await store.GetAllAsync(CancellationToken.None));
    }

    [Fact]
    public void TheQueryWindowGatesOnlyPastTheConfiguredLimit() {
        var window = new IndexerQueryWindow();
        var id = Guid.NewGuid();

        Assert.True(window.TryRecordQuery(id, 2));
        Assert.True(window.TryRecordQuery(id, 2));
        Assert.False(window.TryRecordQuery(id, 2));
        // No limit configured → never gated.
        Assert.True(window.TryRecordQuery(Guid.NewGuid(), null));
    }

    private static PrismediaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
