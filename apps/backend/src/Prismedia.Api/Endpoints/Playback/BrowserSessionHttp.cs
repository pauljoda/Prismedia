using Prismedia.Application.Playback;
using Prismedia.Contracts.Playback;

namespace Prismedia.Api.Endpoints;

internal static class BrowserSessionHttp {
    public static async Task<BrowserSessionResponse> EnsureAsync(
        HttpContext httpContext,
        BrowserSessionService sessions,
        CancellationToken cancellationToken) {
        var response = await sessions.CheckInAsync(ReadSessionCookie(httpContext), cancellationToken);
        WriteSessionCookie(httpContext, response);
        return response;
    }

    private static Guid? ReadSessionCookie(HttpContext httpContext) =>
        httpContext.Request.Cookies.TryGetValue(BrowserSessionConstants.CookieName, out var raw) &&
        Guid.TryParse(raw, out var id) &&
        id != Guid.Empty
            ? id
            : null;

    private static void WriteSessionCookie(HttpContext httpContext, BrowserSessionResponse response) {
        httpContext.Response.Cookies.Append(
            BrowserSessionConstants.CookieName,
            response.SessionId.ToString("D"),
            new CookieOptions {
                Expires = response.ExpiresAt,
                HttpOnly = true,
                IsEssential = true,
                MaxAge = BrowserSessionConstants.Retention,
                SameSite = SameSiteMode.Lax,
                Secure = httpContext.Request.IsHttps,
            });
    }
}
