using Microsoft.EntityFrameworkCore;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;
using Prismedia.Infrastructure.Playback;

namespace Prismedia.Infrastructure.Tests;

public sealed class EfBrowserSessionPersistenceTests {
    [Fact]
    public async Task EnsureCreatesSessionWhenCookieIsMissing() {
        await using var db = CreateContext();
        var store = new EfBrowserSessionPersistence(db);
        var now = DateTimeOffset.Parse("2026-06-18T12:00:00Z");

        var session = await store.EnsureAsync(null, now, now.AddDays(-7), CancellationToken.None);

        Assert.NotEqual(Guid.Empty, session.Id);
        Assert.Equal(now, session.CreatedAt);
        Assert.Equal(now, session.LastSeenAt);
        Assert.Equal(session.Id, Assert.Single(db.BrowserSessions).Id);
    }

    [Fact]
    public async Task EnsureRefreshesExistingSessionAndPrunesStaleSessions() {
        await using var db = CreateContext();
        var now = DateTimeOffset.Parse("2026-06-18T12:00:00Z");
        var freshId = Guid.NewGuid();
        var staleId = Guid.NewGuid();
        db.BrowserSessions.AddRange(
            new BrowserSessionRow {
                Id = freshId,
                CreatedAt = now.AddDays(-1),
                LastSeenAt = now.AddHours(-1),
                UpdatedAt = now.AddHours(-1),
            },
            new BrowserSessionRow {
                Id = staleId,
                CreatedAt = now.AddDays(-10),
                LastSeenAt = now.AddDays(-8),
                UpdatedAt = now.AddDays(-8),
            });
        await db.SaveChangesAsync();

        var store = new EfBrowserSessionPersistence(db);
        var session = await store.EnsureAsync(freshId, now, now.AddDays(-7), CancellationToken.None);

        Assert.Equal(freshId, session.Id);
        Assert.Equal(now, session.LastSeenAt);
        Assert.Null(await db.BrowserSessions.FindAsync(staleId));
    }

    [Fact]
    public async Task SettingsAreScopedPerBrowserSession() {
        await using var db = CreateContext();
        var store = new EfBrowserSessionPersistence(db);
        var session1 = Guid.NewGuid();
        var session2 = Guid.NewGuid();
        var now = DateTimeOffset.Parse("2026-06-18T12:00:00Z");
        db.BrowserSessions.AddRange(
            new BrowserSessionRow { Id = session1, CreatedAt = now, LastSeenAt = now, UpdatedAt = now },
            new BrowserSessionRow { Id = session2, CreatedAt = now, LastSeenAt = now, UpdatedAt = now });
        await db.SaveChangesAsync();

        await store.ReplaceSettingsAsync(session1, new Dictionary<string, string> { ["audio.output"] = """{"volume":0.2}""" }, [], now, CancellationToken.None);
        await store.ReplaceSettingsAsync(session2, new Dictionary<string, string> { ["audio.output"] = """{"volume":0.8}""" }, [], now, CancellationToken.None);

        var loaded1 = await store.LoadSettingsAsync(session1, ["audio.output"], CancellationToken.None);
        var loaded2 = await store.LoadSettingsAsync(session2, ["audio.output"], CancellationToken.None);

        Assert.Contains("0.2", loaded1["audio.output"], StringComparison.Ordinal);
        Assert.Contains("0.8", loaded2["audio.output"], StringComparison.Ordinal);
    }

    private static PrismediaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase($"browser-sessions-{Guid.NewGuid():N}")
            .Options);
}
