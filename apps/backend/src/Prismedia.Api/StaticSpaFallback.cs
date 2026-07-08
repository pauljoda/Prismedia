using Prismedia.Api.Jellyfin;
using Prismedia.Contracts.Media;
using Prismedia.Contracts.Opds;

namespace Prismedia.Api;

/// <summary>
/// Serves the built Svelte shell for browser navigations before endpoint routing can match
/// case-insensitive API compatibility routes that share names with lowercase SPA pages.
/// </summary>
internal static class StaticSpaFallback {
    internal static IApplicationBuilder UseStaticSpaFallback(this IApplicationBuilder app, string staticIndexPath) =>
        app.Use(async (context, next) => {
            if (!ShouldServeSpaShell(context.Request)) {
                await next();
                return;
            }

            context.Response.ContentType = MediaContentTypes.Html;
            context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
            context.Response.Headers.Pragma = "no-cache";
            context.Response.Headers.Expires = "0";
            if (HttpMethods.IsHead(context.Request.Method)) {
                return;
            }

            await context.Response.SendFileAsync(staticIndexPath, context.RequestAborted);
        });

    internal static bool ShouldServeSpaShell(HttpRequest request) {
        if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method)) {
            return false;
        }

        var path = request.Path.Value ?? string.Empty;
        if (path.StartsWith("/api", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/assets", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/openapi", StringComparison.OrdinalIgnoreCase) ||
            request.Path.StartsWithSegments(OpdsProtocol.Prefix) ||
            JellyfinRoutes.IsJellyfinRequest(path)) {
            return false;
        }

        if (Path.HasExtension(path)) {
            return false;
        }

        return request.Headers.Accept.Count == 0 ||
            request.Headers.Accept.Any(value =>
                value?.Contains(MediaContentTypes.Html, StringComparison.OrdinalIgnoreCase) == true);
    }
}
