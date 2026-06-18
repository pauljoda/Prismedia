using Prismedia.Application.Playback;
using Prismedia.Contracts.Playback;

namespace Prismedia.Api.Endpoints;

public static class BrowserSessionEndpoints {
    public static RouteGroupBuilder MapBrowserSessionEndpoints(this IEndpointRouteBuilder routes) {
        var group = routes.MapGroup("/api/browser-session")
            .WithTags("Browser Session");

        group.MapPost("/check-in", async (
            HttpContext httpContext,
            BrowserSessionService sessions,
            CancellationToken cancellationToken) =>
            await BrowserSessionHttp.EnsureAsync(httpContext, sessions, cancellationToken))
            .WithName("CheckInBrowserSession")
            .WithSummary("Ensures a browser-scoped persistence session.")
            .Produces<BrowserSessionResponse>();

        return group;
    }
}
