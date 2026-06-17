using System.Net;
using Prismedia.Api.Jellyfin;
using Prismedia.Application.Security;
using Prismedia.Contracts.Jellyfin;
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
    private static readonly string[] ForwardedHeaderNames = [
        "Forwarded",
        "X-Forwarded-For"
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
                await WriteUnauthorizedAsync(context, ApiProblemCodes.MissingApiKey);
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
                await WriteUnauthorizedAsync(context, ApiProblemCodes.InvalidApiKey);
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
            values.GetValueOrDefault(JellyfinProtocol.AuthFields.Client),
            values.GetValueOrDefault(JellyfinProtocol.AuthFields.Device),
            values.GetValueOrDefault(JellyfinProtocol.AuthFields.DeviceId),
            values.GetValueOrDefault(JellyfinProtocol.AuthFields.Version));
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

    internal static bool ShouldBootstrapCookie(HttpRequest request) {
        if (!IsTrustedUiBootstrapClient(request.HttpContext)) {
            return false;
        }

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

    private static bool IsTrustedUiBootstrapClient(HttpContext context) {
        var remoteAddress = context.Connection.RemoteIpAddress;
        if (remoteAddress is null || IPAddress.IsLoopback(remoteAddress)) {
            return true;
        }

        return IsPrivateNetworkAddress(remoteAddress) &&
            HasForwardedClient(context.Request) &&
            HasForwardedOrigin(context.Request);
    }

    private static bool HasForwardedClient(HttpRequest request) =>
        ForwardedHeaderNames.Any(name => request.Headers.ContainsKey(name));

    private static bool HasForwardedOrigin(HttpRequest request) =>
        request.Headers["X-Forwarded-Host"].Any(value =>
            string.Equals(value, request.Host.Value, StringComparison.OrdinalIgnoreCase)) &&
        request.Headers["X-Forwarded-Proto"].Any(value =>
            value is not null &&
            (value.Equals("http", StringComparison.OrdinalIgnoreCase) ||
             value.Equals("https", StringComparison.OrdinalIgnoreCase)));

    private static bool IsPrivateNetworkAddress(IPAddress address) {
        if (address.IsIPv4MappedToIPv6) {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) {
            return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6UniqueLocal;
        }

        var bytes = address.GetAddressBytes();
        return bytes[0] == 10 ||
            (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
            (bytes[0] == 192 && bytes[1] == 168) ||
            (bytes[0] == 169 && bytes[1] == 254);
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

        if (path.StartsWithSegments("/api")) {
            return true;
        }

        if (!IsJellyfinRequest(path)) {
            return false;
        }

        return !IsPublicJellyfinRoute(request);
    }

    private static bool IsJellyfinRequest(PathString requestPath) =>
        JellyfinRoutes.IsJellyfinRequest(requestPath.Value ?? string.Empty);

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

    private static string? ExtractToken(HttpRequest request) =>
        TokenFromAuthorizationHeader(request.Headers.Authorization.FirstOrDefault()) ??
        TokenFromAuthorizationHeader(request.Headers[JellyfinProtocol.Headers.EmbyAuthorization].FirstOrDefault()) ??
        request.Headers[JellyfinProtocol.Headers.EmbyToken].FirstOrDefault() ??
        request.Headers[JellyfinProtocol.Headers.MediaBrowserToken].FirstOrDefault() ??
        request.Headers[JellyfinProtocol.Headers.PrismediaApiKey].FirstOrDefault() ??
        request.Cookies[CookieName] ??
        request.Query[JellyfinProtocol.QueryKeys.ApiKey].FirstOrDefault() ??
        request.Query[JellyfinProtocol.QueryKeys.ApiKeySnake].FirstOrDefault();

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

    private static async Task WriteUnauthorizedAsync(HttpContext context, string code) {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new ApiProblem(code, "Authentication is required."), context.RequestAborted);
    }

    private static async Task WriteThrottledAsync(HttpContext context) {
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.Response.WriteAsJsonAsync(new ApiProblem(ApiProblemCodes.AuthRateLimited, "Too many failed authentication attempts."), context.RequestAborted);
    }
}
