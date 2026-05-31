using Prismedia.Contracts.System;

namespace Prismedia.Api.Endpoints;

internal static class VideoStreamEndpoints {
    internal static IEndpointRouteBuilder MapJellyfinVideoStreamEndpoints(this IEndpointRouteBuilder routes) {
        routes.MapGet("/Videos/{itemId:guid}/stream", JellyfinPlaybackResults.StreamVideoAsync)
            .WithName("GetJellyfinVideoStream")
            .WithSummary("Get Jellyfin Video Stream.")
            .WithTags("Jellyfin Videos")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status206PartialContent)
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        routes.MapMethods("/Videos/{itemId:guid}/stream", [HttpMethods.Head], JellyfinPlaybackResults.StreamVideoAsync)
            .ExcludeFromDescription();

        return routes;
    }
}
