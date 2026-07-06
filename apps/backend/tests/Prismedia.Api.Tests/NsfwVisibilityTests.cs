using Microsoft.AspNetCore.Http;
using Prismedia.Api.Endpoints;
using Prismedia.Api.Security;
using Prismedia.Application.Security;
using Prismedia.Domain.Entities;

namespace Prismedia.Api.Tests;

public sealed class NsfwVisibilityTests {
    [Fact]
    public void JellyfinContentHonorsBrowserNsfwCookieForAllowedUsers() {
        var context = WebContext(AuthedUser(allowNsfw: true));
        context.Request.Headers.Cookie = "prismedia-nsfw-mode=show";

        var visibility = NsfwVisibility.JellyfinContent(context);

        Assert.True(visibility.AllowSfw);
        Assert.True(visibility.AllowNsfw);
    }

    [Fact]
    public void JellyfinContentDefaultsBrowserRequestsToSfwOnlyWithoutNsfwCookie() {
        var context = WebContext(AuthedUser(allowNsfw: true));

        var visibility = NsfwVisibility.JellyfinContent(context);

        Assert.True(visibility.AllowSfw);
        Assert.False(visibility.AllowNsfw);
    }

    [Fact]
    public void UserNsfwCapOverridesBrowserCookie() {
        // The show cookie must not reveal NSFW content to a user without the permission.
        var context = WebContext(AuthedUser(allowNsfw: false));
        context.Request.Headers.Cookie = "prismedia-nsfw-mode=show";

        Assert.True(NsfwVisibility.ShouldHide(null, context));
        Assert.False(NsfwVisibility.JellyfinContent(context).AllowNsfw);
    }

    [Fact]
    public void ProtocolClientsSeeNsfwWheneverTheUserAllowsIt() {
        // Jellyfin/OPDS clients have no toggle: the permission alone decides.
        var context = WebContext(AuthedUser(allowNsfw: true), viaCookie: false);

        Assert.False(NsfwVisibility.ShouldHide(null, context));
        Assert.True(NsfwVisibility.JellyfinContent(context).AllowNsfw);
    }

    private static DefaultHttpContext WebContext(User user, bool viaCookie = true) {
        var context = new DefaultHttpContext();
        context.Items["PrismediaAuth"] = new PrismediaAuthContext("token", user, null, viaCookie);
        return context;
    }

    private static User AuthedUser(bool allowNsfw) {
        var now = DateTimeOffset.UtcNow;
        return new User(
            Guid.NewGuid(),
            "Prismedia",
            "Prismedia",
            UserRole.Member,
            AllowSfw: true,
            AllowNsfw: allowNsfw,
            CanCreateLibraries: false,
            Enabled: true,
            HasPassword: true,
            LastLoginAt: null,
            CreatedAt: now,
            UpdatedAt: now);
    }
}
