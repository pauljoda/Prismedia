using Microsoft.Extensions.Time.Testing;
using Prismedia.Application.Security;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Tests.Security;

/// <summary>
/// Locks the security-relevant behavior of the auth caches: revocation and user mutation must
/// stop cached identities from resolving immediately, and entries must expire on their own.
/// </summary>
public sealed class AuthCacheTests {
    private static User MakeUser(Guid id) =>
        new(id, "user", "User", UserRole.Member, AllowNsfw: false, CanCreateLibraries: false,
            CanRequestContent: false,
            Enabled: true, HasPassword: true, LastLoginAt: null,
            CreatedAt: DateTimeOffset.UnixEpoch, UpdatedAt: DateTimeOffset.UnixEpoch);

    private static UserSessionResolution MakeResolution(Guid sessionId, Guid userId) =>
        new(
            new UserSession(sessionId, userId, "hash", "Tests", "Device", "device", "1.0",
                DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null),
            MakeUser(userId),
            Touched: false);

    [Fact]
    public void SessionCacheReturnsEntryUntilExpiry() {
        var time = new FakeTimeProvider();
        var cache = new SessionResolutionCache(time);
        cache.Set("token", MakeResolution(Guid.NewGuid(), Guid.NewGuid()));

        Assert.NotNull(cache.TryGet("token"));

        time.Advance(SessionResolutionCache.TimeToLive + TimeSpan.FromSeconds(1));
        Assert.Null(cache.TryGet("token"));
    }

    [Fact]
    public void SessionCacheInvalidatesBySessionAndUser() {
        var cache = new SessionResolutionCache();
        var sessionId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        cache.Set("a", MakeResolution(sessionId, userId));
        cache.Set("b", MakeResolution(Guid.NewGuid(), userId));
        cache.Set("c", MakeResolution(Guid.NewGuid(), Guid.NewGuid()));

        cache.InvalidateSession(sessionId);
        Assert.Null(cache.TryGet("a"));
        Assert.NotNull(cache.TryGet("b"));

        cache.InvalidateUser(userId);
        Assert.Null(cache.TryGet("b"));
        Assert.NotNull(cache.TryGet("c"));
    }

    [Fact]
    public void CredentialCacheKeysOnPasswordAndInvalidatesByUser() {
        var cache = new BasicCredentialCache();
        var user = MakeUser(Guid.NewGuid());
        cache.Set("Reader", "correct-password", user);

        Assert.NotNull(cache.TryGet("reader", "correct-password"));
        Assert.Null(cache.TryGet("reader", "wrong-password"));

        cache.InvalidateUser(user.Id);
        Assert.Null(cache.TryGet("reader", "correct-password"));
    }

    [Fact]
    public void CredentialCacheExpires() {
        var time = new FakeTimeProvider();
        var cache = new BasicCredentialCache(time);
        cache.Set("reader", "pw", MakeUser(Guid.NewGuid()));

        time.Advance(BasicCredentialCache.TimeToLive + TimeSpan.FromSeconds(1));
        Assert.Null(cache.TryGet("reader", "pw"));
    }
}
