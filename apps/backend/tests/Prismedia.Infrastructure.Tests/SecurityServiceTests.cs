using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Security;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Security;

namespace Prismedia.Infrastructure.Tests;

public sealed class SecurityServiceTests {
    [Fact]
    public void PasswordHasherRoundTripsAndRejectsWrongPassword() {
        var hasher = new IdentityPasswordHasher();
        var hash = hasher.Hash("correct horse battery staple");

        Assert.Equal(PasswordVerification.Success, hasher.Verify(hash, "correct horse battery staple"));
        Assert.Equal(PasswordVerification.Failed, hasher.Verify(hash, "wrong password"));
        Assert.NotEqual(hash, hasher.Hash("correct horse battery staple"));
    }

    [Fact]
    public async Task CreateSessionTruncatesClientIdentityToDatabaseLimits() {
        await using var db = CreateContext();
        var persistence = new EfSecurityPersistence(db);
        var user = await CreateUserAsync(persistence, "reader");
        var longClient = new string('c', 200);
        var longDeviceName = new string('d', 200);
        var longDeviceId = new string('i', 300);
        var longVersion = new string('v', 100);

        var session = await persistence.CreateSessionAsync(
            user.Id,
            new string('a', 64),
            new JellyfinClientIdentity(longClient, longDeviceName, longDeviceId, longVersion),
            CancellationToken.None);

        Assert.Equal(128, session.Client?.Length);
        Assert.Equal(128, session.DeviceName?.Length);
        Assert.Equal(256, session.DeviceId?.Length);
        Assert.Equal(64, session.ApplicationVersion?.Length);
    }

    [Fact]
    public async Task ResolveSessionEnforcesSlidingWindowAndTouchStaleness() {
        await using var db = CreateContext();
        var persistence = new EfSecurityPersistence(db);
        var user = await CreateUserAsync(persistence, "sliding");
        var tokenHash = new string('b', 64);
        await persistence.CreateSessionAsync(user.Id, tokenHash, new JellyfinClientIdentity(null, null, null, null), CancellationToken.None);

        // Fresh session: resolves without touching (last seen is brand new).
        var resolved = await persistence.ResolveSessionAsync(
            tokenHash, TimeSpan.FromDays(90), TimeSpan.FromMinutes(5), CancellationToken.None);
        Assert.NotNull(resolved);
        Assert.False(resolved!.Touched);

        // Stale last-seen: resolves and touches.
        var row = await db.UserSessions.SingleAsync();
        row.LastSeenAt = DateTimeOffset.UtcNow - TimeSpan.FromHours(1);
        await db.SaveChangesAsync();
        resolved = await persistence.ResolveSessionAsync(
            tokenHash, TimeSpan.FromDays(90), TimeSpan.FromMinutes(5), CancellationToken.None);
        Assert.NotNull(resolved);
        Assert.True(resolved!.Touched);

        // Beyond the sliding window: expired.
        row = await db.UserSessions.SingleAsync();
        row.LastSeenAt = DateTimeOffset.UtcNow - TimeSpan.FromDays(91);
        await db.SaveChangesAsync();
        resolved = await persistence.ResolveSessionAsync(
            tokenHash, TimeSpan.FromDays(90), TimeSpan.FromMinutes(5), CancellationToken.None);
        Assert.Null(resolved);
    }

    [Fact]
    public async Task DisablingUserInvalidatesActiveSessions() {
        await using var db = CreateContext();
        var persistence = new EfSecurityPersistence(db);
        var user = await CreateUserAsync(persistence, "disabled");
        var tokenHash = new string('e', 64);
        await persistence.CreateSessionAsync(user.Id, tokenHash, new JellyfinClientIdentity(null, null, null, null), CancellationToken.None);

        await persistence.UpdateUserAsync(
            user.Id,
            username: null,
            displayName: null,
            role: null,
            allowSfw: null,
            allowNsfw: null,
            canCreateLibraries: null,
            enabled: false,
            CancellationToken.None);

        var resolved = await persistence.ResolveSessionAsync(
            tokenHash, TimeSpan.FromDays(90), TimeSpan.FromMinutes(5), CancellationToken.None);
        Assert.Null(resolved);
    }

    private static Task<User> CreateUserAsync(EfSecurityPersistence persistence, string username) =>
        persistence.CreateUserAsync(
            username,
            username,
            passwordHash: "hash",
            UserRole.Member,
            allowSfw: true,
            allowNsfw: false,
            canCreateLibraries: false,
            enabled: true,
            CancellationToken.None);

    private static PrismediaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase($"security-{Guid.NewGuid():N}")
            .Options);
}
