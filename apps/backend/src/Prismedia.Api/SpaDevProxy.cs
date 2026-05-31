namespace Prismedia.Api;

/// <summary>
/// Development-only middleware that proxies non-API requests to the Vite dev
/// server so the .NET API can serve everything on a single port (8008) while
/// Svelte still gets full HMR. API, asset, and OpenAPI routes pass through
/// to the normal .NET pipeline.
/// </summary>
public static class SpaDevProxy {
    private static readonly string[] ApiPrefixes =
    [
        "/api",
        "/assets",
        "/openapi"
    ];

    private static readonly string[] JellyfinApiPrefixes =
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

    public static void UseSpaDevServer(this WebApplication app, string viteUrl) {
        var client = new HttpClient(new SocketsHttpHandler {
            AllowAutoRedirect = false,
            UseCookies = false,
        }) {
            Timeout = TimeSpan.FromSeconds(60),
        };

        var trimmedViteUrl = viteUrl.TrimEnd('/');

        app.Use(async (context, next) => {
            if (ShouldPassThroughToBackend(context.Request.Path)) {
                await InvokeBackendRequestAsync(context, next);
                return;
            }

            var targetUrl = $"{trimmedViteUrl}{context.Request.Path}{context.Request.QueryString}";

            using var request = new HttpRequestMessage(
                new HttpMethod(context.Request.Method), targetUrl);

            foreach (var header in context.Request.Headers) {
                if (string.Equals(header.Key, "Host", StringComparison.OrdinalIgnoreCase))
                    continue;
                request.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            }

            if (context.Request.ContentLength > 0 ||
                context.Request.Headers.ContainsKey("Transfer-Encoding")) {
                request.Content = new StreamContent(context.Request.Body);
                if (context.Request.ContentType is not null) {
                    request.Content.Headers.TryAddWithoutValidation(
                        "Content-Type", context.Request.ContentType);
                }
            }

            try {
                using var response = await client.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);

                context.Response.StatusCode = (int)response.StatusCode;

                foreach (var header in response.Headers)
                    context.Response.Headers[header.Key] = header.Value.ToArray();
                foreach (var header in response.Content.Headers)
                    context.Response.Headers[header.Key] = header.Value.ToArray();

                context.Response.Headers.Remove("transfer-encoding");

                await response.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
            } catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) {
                // Browser disconnected — nothing to write.
            } catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException) {
                if (context.Response.HasStarted) return;
                context.Response.StatusCode = 503;
                context.Response.ContentType = "text/html; charset=utf-8";
                await context.Response.WriteAsync(
                    """
                    <!DOCTYPE html>
                    <html><head><meta charset="utf-8"><title>Waiting for Vite…</title>
                    <style>body{background:#0a0a0a;color:#888;font-family:system-ui,sans-serif;display:flex;align-items:center;justify-content:center;height:100vh;margin:0}h1{color:#c49a5a}</style>
                    </head><body><div style="text-align:center">
                    <h1>Waiting for Vite dev server…</h1>
                    <p>The Svelte dev server is still starting. This page will auto-refresh.</p>
                    </div><script>setTimeout(()=>location.reload(),2000)</script></body></html>
                    """);
            }
        });
    }

    /// <summary>
    /// Returns whether a request should bypass the development Vite proxy and
    /// stay in the .NET backend route table. Jellyfin-compatible public routes
    /// are intentionally case-sensitive so lowercase SPA routes like
    /// <c>/videos</c> can still be refreshed directly in the browser.
    /// </summary>
    public static bool ShouldPassThroughToBackend(PathString requestPath) {
        var path = requestPath.Value ?? "";

        foreach (var prefix in ApiPrefixes) {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        foreach (var prefix in JellyfinApiPrefixes) {
            if (path.StartsWith(prefix, StringComparison.Ordinal)) {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Invokes the backend route table and ignores cancellation that only means
    /// the browser abandoned the request during navigation or refresh.
    /// </summary>
    public static async Task InvokeBackendRequestAsync(HttpContext context, Func<Task> next) {
        try {
            await next();
        } catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) {
            // Browser disconnected or navigated away. The request is already gone,
            // so there is no response to produce and no backend fault to surface.
        }
    }
}
