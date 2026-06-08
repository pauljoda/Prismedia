using Prismedia.Api.Mapping;
using Prismedia.Application.Entities;
using Prismedia.Application.Videos;
using Prismedia.Contracts.System;
using Prismedia.Contracts.Playback;

namespace Prismedia.Api.Endpoints;

internal static class SessionPlaybackEndpoints {
    internal static IEndpointRouteBuilder MapJellyfinSessionEndpoints(this IEndpointRouteBuilder routes) {
        routes.MapPost("/Sessions/Playing", async (
            PlaybackSessionRequest request,
            HttpContext httpContext,
            IPlaybackSessionService sessions,
            IEntityReadService entities,
            CancellationToken cancellationToken) => {
                if (!await JellyfinPlaybackResults.IsVisibleAsync(request.ItemId, entities, httpContext, cancellationToken)) {
                    return Results.NotFound(new ApiProblem(ApiProblemCodes.PlaybackItemNotFound, $"Item '{request.ItemId}' was not found."));
                }

                await sessions.StartAsync(request.ToApplication(), cancellationToken);
                return Results.NoContent();
            })
            .WithName("PostJellyfinSessionPlaying")
            .WithSummary("Post Jellyfin Session Playing.")
            .WithTags("Jellyfin Sessions");

        routes.MapPost("/Sessions/Playing/Progress", async (
            PlaybackSessionRequest request,
            HttpContext httpContext,
            IPlaybackSessionService sessions,
            IEntityReadService entities,
            CancellationToken cancellationToken) => {
                if (!await JellyfinPlaybackResults.IsVisibleAsync(request.ItemId, entities, httpContext, cancellationToken)) {
                    return Results.NotFound(new ApiProblem(ApiProblemCodes.PlaybackItemNotFound, $"Item '{request.ItemId}' was not found."));
                }

                await sessions.ProgressAsync(request.ToApplication(), cancellationToken);
                return Results.NoContent();
            })
            .WithName("PostJellyfinSessionProgress")
            .WithSummary("Post Jellyfin Session Progress.")
            .WithTags("Jellyfin Sessions");

        routes.MapPost("/Sessions/Playing/Ping", async (
            PlaybackSessionRequest request,
            HttpContext httpContext,
            IPlaybackSessionService sessions,
            IEntityReadService entities,
            CancellationToken cancellationToken) => {
                if (!await JellyfinPlaybackResults.IsVisibleAsync(request.ItemId, entities, httpContext, cancellationToken)) {
                    return Results.NotFound(new ApiProblem(ApiProblemCodes.PlaybackItemNotFound, $"Item '{request.ItemId}' was not found."));
                }

                await sessions.PingAsync(request.ToApplication(), cancellationToken);
                return Results.NoContent();
            })
            .WithName("PostJellyfinSessionPing")
            .WithSummary("Post Jellyfin Session Ping.")
            .WithTags("Jellyfin Sessions");

        routes.MapPost("/Sessions/Playing/Stopped", async (
            PlaybackSessionRequest request,
            HttpContext httpContext,
            IPlaybackSessionService sessions,
            IEntityReadService entities,
            CancellationToken cancellationToken) => {
                if (!await JellyfinPlaybackResults.IsVisibleAsync(request.ItemId, entities, httpContext, cancellationToken)) {
                    return Results.NotFound(new ApiProblem(ApiProblemCodes.PlaybackItemNotFound, $"Item '{request.ItemId}' was not found."));
                }

                await sessions.StopAsync(request.ToApplication(), cancellationToken);
                return Results.NoContent();
            })
            .WithName("PostJellyfinSessionStopped")
            .WithSummary("Post Jellyfin Session Stopped.")
            .WithTags("Jellyfin Sessions");

        return routes;
    }
}
