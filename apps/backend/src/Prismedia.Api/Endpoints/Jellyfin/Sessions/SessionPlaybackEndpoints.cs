using Prismedia.Api.Mapping;
using Prismedia.Application.Videos;
using Prismedia.Contracts.Playback;

namespace Prismedia.Api.Endpoints;

internal static class SessionPlaybackEndpoints {
    internal static IEndpointRouteBuilder MapJellyfinSessionEndpoints(this IEndpointRouteBuilder routes) {
        routes.MapPost("/Sessions/Playing", async (
            PlaybackSessionRequest request,
            IPlaybackSessionService sessions,
            CancellationToken cancellationToken) => {
                await sessions.StartAsync(request.ToApplication(), cancellationToken);
                return Results.NoContent();
            })
            .WithName("PostJellyfinSessionPlaying")
            .WithTags("Jellyfin Sessions");

        routes.MapPost("/Sessions/Playing/Progress", async (
            PlaybackSessionRequest request,
            IPlaybackSessionService sessions,
            CancellationToken cancellationToken) => {
                await sessions.ProgressAsync(request.ToApplication(), cancellationToken);
                return Results.NoContent();
            })
            .WithName("PostJellyfinSessionProgress")
            .WithTags("Jellyfin Sessions");

        routes.MapPost("/Sessions/Playing/Ping", async (
            PlaybackSessionRequest request,
            IPlaybackSessionService sessions,
            CancellationToken cancellationToken) => {
                await sessions.PingAsync(request.ToApplication(), cancellationToken);
                return Results.NoContent();
            })
            .WithName("PostJellyfinSessionPing")
            .WithTags("Jellyfin Sessions");

        routes.MapPost("/Sessions/Playing/Stopped", async (
            PlaybackSessionRequest request,
            IPlaybackSessionService sessions,
            CancellationToken cancellationToken) => {
                await sessions.StopAsync(request.ToApplication(), cancellationToken);
                return Results.NoContent();
            })
            .WithName("PostJellyfinSessionStopped")
            .WithTags("Jellyfin Sessions");

        return routes;
    }
}
