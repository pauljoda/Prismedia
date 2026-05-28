using Prismedia.Application.Videos;
using Prismedia.Contracts.Playback;
using Prismedia.Contracts.System;

namespace Prismedia.Api.Endpoints;

internal static class PlaybackInfoEndpoints {
    internal static IEndpointRouteBuilder MapJellyfinItemPlaybackInfoEndpoints(this IEndpointRouteBuilder routes) {
        routes.MapGet("/Items/{itemId:guid}/PlaybackInfo", (
            Guid itemId,
            IPlaybackInfoService playback,
            CancellationToken cancellationToken) =>
            JellyfinPlaybackResults.GetPlaybackInfoAsync(itemId, playback, null, cancellationToken))
            .WithName("GetJellyfinPlaybackInfo")
            .WithSummary("Get Jellyfin Playback Info.")
            .WithTags("Jellyfin Playback")
            .Produces<PlaybackInfoResponse>()
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        routes.MapPost("/Items/{itemId:guid}/PlaybackInfo", (
            Guid itemId,
            PlaybackInfoRequest request,
            IPlaybackInfoService playback,
            CancellationToken cancellationToken) =>
            JellyfinPlaybackResults.GetPlaybackInfoAsync(itemId, playback, request, cancellationToken))
            .WithName("PostJellyfinPlaybackInfo")
            .WithSummary("Post Jellyfin Playback Info.")
            .WithTags("Jellyfin Playback")
            .Produces<PlaybackInfoResponse>()
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        return routes;
    }
}
