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

        routes.MapGet("/Videos/{itemId:guid}/stream.{container}", (
            Guid itemId,
            string container,
            IVideoSourceService sourceFiles,
            IEntityReadService entities,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
            JellyfinPlaybackResults.StreamVideoAsync(itemId, sourceFiles, entities, httpContext, cancellationToken))
            .WithName("GetJellyfinVideoStreamByContainer")
            .WithSummary("Get Jellyfin Video Stream by container.")
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
