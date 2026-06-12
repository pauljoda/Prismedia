using Microsoft.AspNetCore.Http;
using Prismedia.Api.Endpoints;
using Prismedia.Api.Security;
using Prismedia.Application.Security;

namespace Prismedia.Api.Tests;

public sealed class NsfwVisibilityTests {
    [Fact]
    public void JellyfinContentHonorsBrowserNsfwCookieForAppKeyRequests() {
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = "prismedia-nsfw-mode=show";

        var visibility = NsfwVisibility.JellyfinContent(context);

        Assert.True(visibility.AllowSfw);
        Assert.True(visibility.AllowNsfw);
    }

    [Fact]
    public void JellyfinContentDefaultsBrowserRequestsToSfwOnlyWithoutNsfwCookie() {
        var context = new DefaultHttpContext();

        var visibility = NsfwVisibility.JellyfinContent(context);

        Assert.True(visibility.AllowSfw);
        Assert.False(visibility.AllowNsfw);
    }

    [Fact]
    public void JellyfinContentKeepsJellyfinProfileVisibilityAheadOfBrowserCookie() {
        var now = DateTimeOffset.UtcNow;
        var profile = new JellyfinProfile(
            Guid.NewGuid(),
            "Prismedia",
            "Prismedia",
            AllowSfw: true,
            AllowNsfw: false,
            Enabled: true,
            LastLoginAt: null,
            CreatedAt: now,
            UpdatedAt: now);
        var session = new JellyfinSession(
            Guid.NewGuid(),
            profile.Id,
            "token-hash",
            "client",
            "device",
            "device-id",
            "1.0",
            now,
            now,
            InvalidatedAt: null);
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = "prismedia-nsfw-mode=show";
        context.Items["PrismediaAuth"] = new PrismediaAuthContext(
            PrismediaAuthKind.JellyfinSession,
            "token",
            new JellyfinSessionResolution(session, profile));

        var visibility = NsfwVisibility.JellyfinContent(context);

        Assert.True(visibility.AllowSfw);
        Assert.False(visibility.AllowNsfw);
    }
}
