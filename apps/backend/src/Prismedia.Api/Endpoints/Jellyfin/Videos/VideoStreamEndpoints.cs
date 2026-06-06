using Prismedia.Application.Entities;
using Prismedia.Application.Videos;
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

        // Jellyfin clients commonly append the chosen container to the stream route, for example
        // /Videos/{id}/stream.mp4. The container is advisory; Prismedia serves the resolved source.
        routes.MapGet("/Videos/{itemId:guid}/stream.{container}", (
            Guid itemId,
            string container,
            IVideoSourceService sourceFiles,
            IEntityReadService entities,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
            JellyfinPlaybackResults.StreamVideoAsync(itemId, sourceFiles, entities, httpContext, cancellationToken))
            .WithName("GetJellyfinVideoStreamContainer")
            .WithSummary("Get Jellyfin Video Stream with container suffix.")
            .WithTags("Jellyfin Videos")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status206PartialContent)
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        routes.MapMethods("/Videos/{itemId:guid}/stream.{container}", [HttpMethods.Head], (
            Guid itemId,
            string container,
            IVideoSourceService sourceFiles,
            IEntityReadService entities,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
            JellyfinPlaybackResults.StreamVideoAsync(itemId, sourceFiles, entities, httpContext, cancellationToken))
            .ExcludeFromDescription();

        return routes;
    }
}
