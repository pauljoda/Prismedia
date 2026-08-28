using Microsoft.AspNetCore.Http;
using Prismedia.Api.Endpoints;
using Prismedia.Api.Security;
using Prismedia.Application.Security;
using Prismedia.Domain.Entities;

namespace Prismedia.Api.Tests;

public sealed class NsfwVisibilityTests {
    [Fact]
    public void BrowserCookieShowsNsfwForAllowedUsers() {
        var context = WebContext(AuthedUser(allowNsfw: true));
        context.Request.Headers.Cookie = "prismedia-nsfw-mode=show";

        Assert.False(NsfwVisibility.ShouldHide(null, context));
    }

    [Fact]
    public void BrowserDefaultsToHidingNsfwWithoutCookie() {
        var context = WebContext(AuthedUser(allowNsfw: true));

        Assert.True(NsfwVisibility.ShouldHide(null, context));
    }

    [Fact]
    public void UserNsfwCapOverridesBrowserCookie() {
        // The show cookie must not reveal NSFW content to a user without the permission.
        var context = WebContext(AuthedUser(allowNsfw: false));
        context.Request.Headers.Cookie = "prismedia-nsfw-mode=show";

        Assert.True(NsfwVisibility.ShouldHide(null, context));
    }

    [Fact]
    public void ProtocolClientsSeeNsfwWheneverTheUserAllowsIt() {
        // Native and OPDS clients have no browser toggle: the permission alone decides.
        var context = WebContext(AuthedUser(allowNsfw: true), viaCookie: false);

        Assert.False(NsfwVisibility.ShouldHide(null, context));
    }

    [Fact]
    public void ProtocolClientsHonorAnExplicitRequestToHideNsfw() {
        var context = WebContext(AuthedUser(allowNsfw: true), viaCookie: false);

        Assert.True(NsfwVisibility.ShouldHide(true, context));
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
            AllowNsfw: allowNsfw,
            CanCreateLibraries: false,
            CanRequestContent: false,
            Enabled: true,
            HasPassword: true,
            LastLoginAt: null,
            CreatedAt: now,
            UpdatedAt: now);
    }
}
