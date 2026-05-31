using Prismedia.Application.Security;
using Prismedia.Contracts.System;

namespace Prismedia.Api.Security;

internal enum PrismediaAuthKind {
    AppKey,
    JellyfinSession
}

internal sealed record PrismediaAuthContext(
    PrismediaAuthKind Kind,
    string Token,
    JellyfinSessionResolution? JellyfinSession);

internal static class PrismediaAuthentication {
    internal const string CookieName = "prismedia-api-key";
    private const string AuthContextKey = "PrismediaAuth";

    private static readonly string[] JellyfinPrefixes =
    [
        "/System",
        "/Users",
        "/UserViews",
        "/Items",
        "/Shows",
        "/Videos",
        "/Sessions",
        "/UserPlayedItems",
        "/UserItems",
        "/MediaSegments",
        "/Library",
        "/Branding",
        "/DisplayPreferences"
    ];

    internal static IApplicationBuilder UsePrismediaUiApiKeyCookie(this IApplicationBuilder app) =>
        app.Use(async (context, next) => {
            if (ShouldBootstrapCookie(context.Request)) {
                var security = context.RequestServices.GetRequiredService<PrismediaSecurityService>();
                var state = await security.EnsureSecurityAsync(context.RequestAborted);
                if (!context.Request.Cookies.TryGetValue(CookieName, out var existing) ||
                    !string.Equals(
                        PrismediaSecurityService.NormalizeApiKey(existing),
                        state.ApiKey,
                        StringComparison.Ordinal)) {
                    context.Response.Cookies.Append(CookieName, state.ApiKey, CookieOptions(context));
                }
            }

            await next();
        });

    internal static IApplicationBuilder UsePrismediaApiAuthentication(this IApplicationBuilder app) =>
        app.Use(async (context, next) => {
            if (!RequiresAuthentication(context.Request)) {
                await next();
                return;
            }

            var token = ExtractToken(context.Request);
            if (string.IsNullOrWhiteSpace(token)) {
                await WriteUnauthorizedAsync(context, "missing_api_key");
                return;
            }

            var security = context.RequestServices.GetRequiredService<PrismediaSecurityService>();
            var bucket = BucketFor(context);
            var isJellyfin = IsJellyfinRequest(context.Request.Path);
            if (isJellyfin) {
                var session = await security.ResolveJellyfinSessionAsync(token, context.RequestAborted);
                if (session is not null) {
                    context.Items[AuthContextKey] = new PrismediaAuthContext(
                        PrismediaAuthKind.JellyfinSession,
                        token,
                        session);
                    await next();
                    return;
                }
            }

            var validation = await security.ValidateApiKeyAsync(token, bucket, context.RequestAborted);
            if (validation.IsThrottled) {
                await WriteThrottledAsync(context);
                return;
            }

            if (!validation.IsValid) {
                await WriteUnauthorizedAsync(context, "invalid_api_key");
                return;
            }

            context.Items[AuthContextKey] = new PrismediaAuthContext(
                PrismediaAuthKind.AppKey,
                PrismediaSecurityService.NormalizeApiKey(token),
                null);
            await next();
        });

    internal static PrismediaAuthContext? GetPrismediaAuth(this HttpContext context) =>
        context.Items.TryGetValue(AuthContextKey, out var value) ? value as PrismediaAuthContext : null;

    internal static bool IsJellyfinSession(this HttpContext context) =>
        context.GetPrismediaAuth()?.Kind == PrismediaAuthKind.JellyfinSession;

    internal static JellyfinProfile? GetJellyfinProfile(this HttpContext context) =>
        context.GetPrismediaAuth()?.JellyfinSession?.Profile;

    internal static void AppendUiApiKeyCookie(this HttpContext context, string apiKey) =>
        context.Response.Cookies.Append(CookieName, apiKey, CookieOptions(context));

    internal static JellyfinClientIdentity GetJellyfinClientIdentity(this HttpRequest request) {
        var values = ParseJellyfinHeaderValues(request);
        return new JellyfinClientIdentity(
            values.GetValueOrDefault("Client"),
            values.GetValueOrDefault("Device"),
            values.GetValueOrDefault("DeviceId"),
            values.GetValueOrDefault("Version"));
    }

    internal static string BucketFor(HttpContext context, string? extra = null) {
        var remote = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return string.IsNullOrWhiteSpace(extra) ? remote : $"{remote}:{extra.Trim().ToLowerInvariant()}";
    }

    private static CookieOptions CookieOptions(HttpContext context) =>
        new() {
            HttpOnly = true,
            IsEssential = true,
            Path = "/",
            SameSite = SameSiteMode.Lax,
            Secure = context.Request.IsHttps
        };

    private static bool ShouldBootstrapCookie(HttpRequest request) {
        if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method)) {
            return false;
        }

        var path = request.Path.Value ?? string.Empty;
        if (path.StartsWith("/api", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/assets", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/openapi", StringComparison.OrdinalIgnoreCase) ||
            IsJellyfinRequest(request.Path)) {
            return false;
        }

        if (Path.HasExtension(path)) {
            return false;
        }

        return request.Headers.Accept.Count == 0 ||
            request.Headers.Accept.Any(value => value?.Contains("text/html", StringComparison.OrdinalIgnoreCase) == true);
    }

    private static bool RequiresAuthentication(HttpRequest request) {
        var path = request.Path;
        if (path.StartsWithSegments("/api/health")) {
            return false;
        }

        if (path.StartsWithSegments("/api")) {
            return true;
        }

        if (!IsJellyfinRequest(path)) {
            return false;
        }

        return !IsPublicJellyfinRoute(request);
    }

    private static bool IsJellyfinRequest(PathString requestPath) {
        var path = requestPath.Value ?? string.Empty;
        return path.StartsWith("/Library", StringComparison.Ordinal) ||
            JellyfinPrefixes
                .Where(prefix => !string.Equals(prefix, "/Library", StringComparison.Ordinal))
                .Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsPublicJellyfinRoute(HttpRequest request) {
        var path = request.Path.Value ?? string.Empty;
        return (HttpMethods.IsGet(request.Method) && path.Equals("/System/Info/Public", StringComparison.OrdinalIgnoreCase)) ||
            ((HttpMethods.IsGet(request.Method) || HttpMethods.IsPost(request.Method)) &&
                path.Equals("/System/Ping", StringComparison.OrdinalIgnoreCase)) ||
            (HttpMethods.IsGet(request.Method) && path.Equals("/Branding/Configuration", StringComparison.OrdinalIgnoreCase)) ||
            (HttpMethods.IsGet(request.Method) && (path.Equals("/Branding/Css", StringComparison.OrdinalIgnoreCase) ||
                path.Equals("/Branding/Css.css", StringComparison.OrdinalIgnoreCase))) ||
            (HttpMethods.IsGet(request.Method) && path.Equals("/Users/Public", StringComparison.OrdinalIgnoreCase)) ||
            (HttpMethods.IsPost(request.Method) && path.Equals("/Users/AuthenticateByName", StringComparison.OrdinalIgnoreCase)) ||
            (HttpMethods.IsPost(request.Method) &&
                path.StartsWith("/Users/", StringComparison.OrdinalIgnoreCase) &&
                path.EndsWith("/Authenticate", StringComparison.OrdinalIgnoreCase));
    }

    private static string? ExtractToken(HttpRequest request) =>
        TokenFromAuthorizationHeader(request.Headers.Authorization.FirstOrDefault()) ??
        TokenFromAuthorizationHeader(request.Headers["X-Emby-Authorization"].FirstOrDefault()) ??
        request.Headers["X-Emby-Token"].FirstOrDefault() ??
        request.Headers["X-MediaBrowser-Token"].FirstOrDefault() ??
        request.Headers["X-Prismedia-Api-Key"].FirstOrDefault() ??
        request.Cookies[CookieName] ??
        request.Query["ApiKey"].FirstOrDefault() ??
        request.Query["api_key"].FirstOrDefault();

    private static string? TokenFromAuthorizationHeader(string? header) {
        if (string.IsNullOrWhiteSpace(header)) {
            return null;
        }

        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) {
            return header["Bearer ".Length..].Trim();
        }

        var values = ParseJellyfinHeaderValues(header);
        return values.GetValueOrDefault("Token");
    }

    private static IReadOnlyDictionary<string, string> ParseJellyfinHeaderValues(HttpRequest request) {
        var header = request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(header)) {
            header = request.Headers["X-Emby-Authorization"].FirstOrDefault();
        }

        return ParseJellyfinHeaderValues(header);
    }

    private static IReadOnlyDictionary<string, string> ParseJellyfinHeaderValues(string? header) {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(header)) {
            return values;
        }

        var text = header.Trim();
        if (text.StartsWith("MediaBrowser ", StringComparison.OrdinalIgnoreCase)) {
            text = text["MediaBrowser ".Length..];
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

    private static async Task WriteUnauthorizedAsync(HttpContext context, string code) {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new ApiProblem(code, "Authentication is required."), context.RequestAborted);
    }

    private static async Task WriteThrottledAsync(HttpContext context) {
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.Response.WriteAsJsonAsync(new ApiProblem("auth_rate_limited", "Too many failed authentication attempts."), context.RequestAborted);
    }
}
