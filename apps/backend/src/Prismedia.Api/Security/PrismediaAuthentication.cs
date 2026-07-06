using System.Text;
using Prismedia.Api.Jellyfin;
using Prismedia.Application.Security;
using Prismedia.Contracts.Jellyfin;
using Prismedia.Contracts.Opds;
using Prismedia.Contracts.System;

namespace Prismedia.Api.Security;

/// <summary>
/// Authenticated identity attached to the request: the presented token, the resolved
/// user, and the session when the token maps to one (OPDS Basic auth verifies
/// credentials per request without a persisted session). <paramref name="ViaCookie"/>
/// distinguishes web-browser sessions (session cookie) from protocol clients
/// (header/query token or Basic auth), which changes NSFW toggle semantics.
/// </summary>
internal sealed record PrismediaAuthContext(
    string Token,
    User User,
    UserSession? Session,
    bool ViaCookie);

internal static class PrismediaAuthentication {
    /// <summary>HttpOnly cookie carrying the web portal's session token.</summary>
    internal const string SessionCookieName = "prismedia-session";

    /// <summary>Pre-multi-user api-key cookie; expired once at login for cleanup.</summary>
    internal const string LegacyApiKeyCookieName = "prismedia-api-key";

    private const string AuthContextKey = "PrismediaAuth";
    private const string SecFetchSiteHeader = "Sec-Fetch-Site";
    private const string CrossSiteFetch = "cross-site";

    internal static IApplicationBuilder UsePrismediaApiAuthentication(this IApplicationBuilder app) =>
        app.Use(async (context, next) => {
            if (!RequiresAuthentication(context.Request)) {
                await next();
                return;
            }

            var auth = context.RequestServices.GetRequiredService<UserAuthService>();
            var isOpds = IsOpdsRequest(context.Request.Path);

            if (isOpds && TryExtractBasicCredentials(context.Request, out var username, out var password)) {
                var basicResult = await auth.VerifyCredentialsAsync(
                    username,
                    password,
                    BucketFor(context, username),
                    context.RequestAborted);
                if (basicResult.IsThrottled) {
                    await WriteThrottledAsync(context);
                    return;
                }

                if (basicResult.User is null) {
                    await WriteUnauthorizedAsync(context);
                    return;
                }

                SetAuthContext(context, new PrismediaAuthContext(string.Empty, basicResult.User, null, ViaCookie: false));
                await next();
                return;
            }

            var (token, fromCookie) = ExtractToken(context.Request);
            if (string.IsNullOrWhiteSpace(token)) {
                await WriteUnauthorizedAsync(context);
                return;
            }

            var resolution = await auth.ResolveSessionAsync(token, context.RequestAborted);
            if (resolution is null) {
                await WriteUnauthorizedAsync(context);
                return;
            }

            SetAuthContext(context, new PrismediaAuthContext(token, resolution.User, resolution.Session, fromCookie));
            if (resolution.Touched && fromCookie) {
                context.AppendSessionCookie(token);
            }

            await next();
        });

    internal static PrismediaAuthContext? GetPrismediaAuth(this HttpContext context) =>
        context.Items.TryGetValue(AuthContextKey, out var value) ? value as PrismediaAuthContext : null;

    /// <summary>The authenticated user for this request, or null on public routes.</summary>
    internal static User? GetCurrentUser(this HttpContext context) =>
        context.GetPrismediaAuth()?.User;

    /// <summary>Issues the sliding web session cookie.</summary>
    internal static void AppendSessionCookie(this HttpContext context, string token) =>
        context.Response.Cookies.Append(SessionCookieName, token, new CookieOptions {
            HttpOnly = true,
            IsEssential = true,
            Path = "/",
            SameSite = SameSiteMode.Lax,
            Secure = context.Request.IsHttps,
            MaxAge = UserAuthService.SessionSlidingWindow
        });

    /// <summary>Expires the web session cookie (sign out).</summary>
    internal static void ExpireSessionCookie(this HttpContext context) =>
        context.Response.Cookies.Delete(SessionCookieName, new CookieOptions { Path = "/" });

    /// <summary>Expires the obsolete pre-multi-user api-key cookie.</summary>
    internal static void ExpireLegacyApiKeyCookie(this HttpContext context) {
        if (context.Request.Cookies.ContainsKey(LegacyApiKeyCookieName)) {
            context.Response.Cookies.Delete(LegacyApiKeyCookieName, new CookieOptions { Path = "/" });
        }
    }

    internal static JellyfinClientIdentity GetJellyfinClientIdentity(this HttpRequest request) {
        var values = ParseJellyfinHeaderValues(request);
        return new JellyfinClientIdentity(
            values.GetValueOrDefault(JellyfinProtocol.AuthFields.Client),
            values.GetValueOrDefault(JellyfinProtocol.AuthFields.Device),
            values.GetValueOrDefault(JellyfinProtocol.AuthFields.DeviceId),
            values.GetValueOrDefault(JellyfinProtocol.AuthFields.Version));
    }

    internal static string BucketFor(HttpContext context, string? extra = null) {
        var remote = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return string.IsNullOrWhiteSpace(extra) ? remote : $"{remote}:{extra.Trim().ToLowerInvariant()}";
    }

    private static void SetAuthContext(HttpContext context, PrismediaAuthContext auth) {
        context.Items[AuthContextKey] = auth;
        context.RequestServices.GetRequiredService<CurrentUserContextHolder>()
            .Set(auth.User, auth.Session?.Id ?? Guid.Empty);
    }

    private static bool RequiresAuthentication(HttpRequest request) {
        var path = request.Path;
        if (path.StartsWithSegments("/api/health")) {
            return false;
        }

        // Development-only codegen manifest endpoint; mapped only in Development and never
        // exposed in production, so treating it as public here is safe.
        if (path.StartsWithSegments(Codegen.CodegenEndpoints.CodegenPrefix)) {
            return false;
        }

        if (IsPublicAuthRoute(request)) {
            return false;
        }

        if (path.StartsWithSegments("/api")) {
            return true;
        }

        if (IsOpdsRequest(path)) {
            return true;
        }

        if (!IsJellyfinRequest(path)) {
            return false;
        }

        return !IsPublicJellyfinRoute(request);
    }

    private static bool IsPublicAuthRoute(HttpRequest request) {
        var path = request.Path.Value ?? string.Empty;
        return (HttpMethods.IsGet(request.Method) &&
                path.Equals("/api/auth/setup-status", StringComparison.OrdinalIgnoreCase)) ||
            (HttpMethods.IsPost(request.Method) &&
                (path.Equals("/api/auth/setup", StringComparison.OrdinalIgnoreCase) ||
                 path.Equals("/api/auth/login", StringComparison.OrdinalIgnoreCase)));
    }

    private static bool IsJellyfinRequest(PathString requestPath) =>
        JellyfinRoutes.IsJellyfinRequest(requestPath.Value ?? string.Empty);

    private static bool IsOpdsRequest(PathString requestPath) =>
        requestPath.StartsWithSegments(OpdsProtocol.Prefix);

    private static bool IsPublicJellyfinRoute(HttpRequest request) {
        var path = request.Path.Value ?? string.Empty;
        // Image endpoints are anonymous in real Jellyfin — clients (e.g. Manet) request artwork
        // without a token, so requiring auth here returns 401 and the client shows no covers.
        if (HttpMethods.IsGet(request.Method) &&
            path.StartsWith("/Items/", StringComparison.OrdinalIgnoreCase) &&
            path.Contains("/Images", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        return (HttpMethods.IsGet(request.Method) && path.Equals(JellyfinRoutes.SystemInfoPublic, StringComparison.OrdinalIgnoreCase)) ||
            ((HttpMethods.IsGet(request.Method) || HttpMethods.IsPost(request.Method)) &&
                path.Equals(JellyfinRoutes.SystemPing, StringComparison.OrdinalIgnoreCase)) ||
            (HttpMethods.IsGet(request.Method) && path.Equals(JellyfinRoutes.BrandingConfiguration, StringComparison.OrdinalIgnoreCase)) ||
            (HttpMethods.IsGet(request.Method) && (path.Equals(JellyfinRoutes.BrandingCss, StringComparison.OrdinalIgnoreCase) ||
                path.Equals(JellyfinRoutes.BrandingCssFile, StringComparison.OrdinalIgnoreCase))) ||
            (HttpMethods.IsGet(request.Method) && path.Equals(JellyfinRoutes.BrandingSplashscreen, StringComparison.OrdinalIgnoreCase)) ||
            (HttpMethods.IsGet(request.Method) && path.Equals(JellyfinRoutes.QuickConnectEnabled, StringComparison.OrdinalIgnoreCase)) ||
            (HttpMethods.IsPost(request.Method) && path.Equals(JellyfinRoutes.QuickConnectInitiate, StringComparison.OrdinalIgnoreCase)) ||
            (HttpMethods.IsGet(request.Method) && path.Equals(JellyfinRoutes.QuickConnectConnect, StringComparison.OrdinalIgnoreCase)) ||
            (HttpMethods.IsGet(request.Method) && path.Equals(JellyfinRoutes.UsersPublic, StringComparison.OrdinalIgnoreCase)) ||
            (HttpMethods.IsPost(request.Method) && path.Equals(JellyfinRoutes.UsersAuthenticateByName, StringComparison.OrdinalIgnoreCase)) ||
            (HttpMethods.IsPost(request.Method) && path.Equals(JellyfinRoutes.UsersAuthenticateWithQuickConnect, StringComparison.OrdinalIgnoreCase)) ||
            (HttpMethods.IsPost(request.Method) &&
                path.StartsWith(JellyfinRoutes.UsersPrefix, StringComparison.OrdinalIgnoreCase) &&
                path.EndsWith(JellyfinRoutes.AuthenticateSuffix, StringComparison.OrdinalIgnoreCase));
    }

    private static (string? Token, bool FromCookie) ExtractToken(HttpRequest request) {
        var headerToken =
            TokenFromAuthorizationHeader(request.Headers.Authorization.FirstOrDefault()) ??
            TokenFromAuthorizationHeader(request.Headers[JellyfinProtocol.Headers.EmbyAuthorization].FirstOrDefault()) ??
            request.Headers[JellyfinProtocol.Headers.EmbyToken].FirstOrDefault() ??
            request.Headers[JellyfinProtocol.Headers.MediaBrowserToken].FirstOrDefault() ??
            request.Headers[JellyfinProtocol.Headers.PrismediaApiKey].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(headerToken)) {
            return (headerToken, false);
        }

        // Ignore the session cookie on cross-site sends as CSRF belt-and-braces on top of
        // SameSite=Lax; explicit header/query tokens above are unaffected.
        var crossSite = string.Equals(
            request.Headers[SecFetchSiteHeader].FirstOrDefault(),
            CrossSiteFetch,
            StringComparison.OrdinalIgnoreCase);
        if (!crossSite && request.Cookies[SessionCookieName] is { Length: > 0 } cookieToken) {
            return (cookieToken, true);
        }

        var queryToken =
            request.Query[JellyfinProtocol.QueryKeys.ApiKey].FirstOrDefault() ??
            request.Query[JellyfinProtocol.QueryKeys.ApiKeySnake].FirstOrDefault();
        return (queryToken, false);
    }

    private static string? TokenFromAuthorizationHeader(string? header) {
        if (string.IsNullOrWhiteSpace(header)) {
            return null;
        }

        if (header.StartsWith(JellyfinProtocol.Schemes.Bearer, StringComparison.OrdinalIgnoreCase)) {
            return header[JellyfinProtocol.Schemes.Bearer.Length..].Trim();
        }

        var values = ParseJellyfinHeaderValues(header);
        return values.GetValueOrDefault(JellyfinProtocol.AuthFields.Token);
    }

    private static bool TryExtractBasicCredentials(
        HttpRequest request,
        out string? username,
        out string? password) {
        username = null;
        password = null;
        var header = request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(header)) {
            return false;
        }

        var trimmed = header.Trim();
        if (!trimmed.StartsWith(OpdsProtocol.BasicScheme + " ", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        var encoded = trimmed[(OpdsProtocol.BasicScheme.Length + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(encoded)) {
            return false;
        }

        try {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            var separator = decoded.IndexOf(':');
            if (separator <= 0) {
                return false;
            }

            username = decoded[..separator];
            password = decoded[(separator + 1)..];
            return true;
        } catch (FormatException) {
            return false;
        }
    }

    private static IReadOnlyDictionary<string, string> ParseJellyfinHeaderValues(HttpRequest request) {
        var header = request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(header)) {
            header = request.Headers[JellyfinProtocol.Headers.EmbyAuthorization].FirstOrDefault();
        }

        return ParseJellyfinHeaderValues(header);
    }

    private static IReadOnlyDictionary<string, string> ParseJellyfinHeaderValues(string? header) {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(header)) {
            return values;
        }

        var text = header.Trim();
        if (text.StartsWith(JellyfinProtocol.Schemes.MediaBrowser, StringComparison.OrdinalIgnoreCase)) {
            text = text[JellyfinProtocol.Schemes.MediaBrowser.Length..];
        }

        foreach (var part in text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)) {
            var index = part.IndexOf('=');
            if (index <= 0) {
                continue;
            }

            var key = part[..index].Trim();
            var value = part[(index + 1)..].Trim().Trim('"');
            if (key.Length > 0) {
                values[key] = value;
            }
        }

        return values;
    }

    private static async Task WriteUnauthorizedAsync(HttpContext context) {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        if (IsOpdsRequest(context.Request.Path)) {
            context.Response.Headers.WWWAuthenticate = OpdsProtocol.BasicChallenge;
        }

        await context.Response.WriteAsJsonAsync(
            new ApiProblem(ApiProblemCodes.AuthenticationRequired, "Authentication is required."),
            context.RequestAborted);
    }

    private static async Task WriteThrottledAsync(HttpContext context) {
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.Response.WriteAsJsonAsync(new ApiProblem(ApiProblemCodes.AuthRateLimited, "Too many failed authentication attempts."), context.RequestAborted);
    }
}
