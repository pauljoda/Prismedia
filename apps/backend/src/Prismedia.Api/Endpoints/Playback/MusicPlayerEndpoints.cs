using Prismedia.Application.Playback;
using Prismedia.Contracts.Playback;

namespace Prismedia.Api.Endpoints;

public static class MusicPlayerEndpoints {
    public static RouteGroupBuilder MapMusicPlayerEndpoints(this IEndpointRouteBuilder routes) {
        var group = routes.MapGroup("/api/music-player")
            .WithTags("Playback");

        group.MapGet("/state", async (
            HttpContext httpContext,
            BrowserSessionService sessions,
            MusicPlayerStateService playerState,
            CancellationToken cancellationToken) => {
            var browserSession = await BrowserSessionHttp.EnsureAsync(httpContext, sessions, cancellationToken);
            return await playerState.GetAsync(browserSession.SessionId, cancellationToken);
        })
            .WithName("GetMusicPlayerState")
            .WithSummary("Gets the browser-scoped music player state.")
            .Produces<MusicPlayerStateResponse>();

        group.MapPut("/state", async (
            HttpContext httpContext,
            UpdateMusicPlayerStateRequest request,
            BrowserSessionService sessions,
            MusicPlayerStateService playerState,
            CancellationToken cancellationToken) => {
            var browserSession = await BrowserSessionHttp.EnsureAsync(httpContext, sessions, cancellationToken);
            return await playerState.SaveAsync(browserSession.SessionId, request, cancellationToken);
        })
            .WithName("UpdateMusicPlayerState")
            .WithSummary("Saves the browser-scoped music player state.")
            .Produces<MusicPlayerStateResponse>();

        group.MapDelete("/state", async (
            HttpContext httpContext,
            BrowserSessionService sessions,
            MusicPlayerStateService playerState,
            CancellationToken cancellationToken) => {
            var browserSession = await BrowserSessionHttp.EnsureAsync(httpContext, sessions, cancellationToken);
            await playerState.ClearAsync(browserSession.SessionId, cancellationToken);
            return Results.NoContent();
        })
            .WithName("ClearMusicPlayerState")
            .WithSummary("Clears the browser-scoped music player queue state.")
            .Produces(StatusCodes.Status204NoContent);

        return group;
    }
}
