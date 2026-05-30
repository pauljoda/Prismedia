using Prismedia.Application.Entities;
using Prismedia.Application.Videos;

namespace Prismedia.Api.Endpoints;

internal static class UserPlayedItemEndpoints {
    internal static IEndpointRouteBuilder MapJellyfinUserPlayedItemEndpoints(this IEndpointRouteBuilder routes) {
        routes.MapPost("/UserPlayedItems/{itemId:guid}", async (
            Guid itemId,
            IPlaybackSessionService sessions,
            IEntityReadService entities,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
            await JellyfinPlaybackResults.MarkPlayedAsync(itemId, sessions, entities, httpContext, cancellationToken))
            .WithName("PostJellyfinUserPlayedItem")
            .WithSummary("Post Jellyfin User Played Item.")
            .WithTags("Jellyfin Sessions");

        routes.MapDelete("/UserPlayedItems/{itemId:guid}", async (
            Guid itemId,
            IPlaybackSessionService sessions,
            IEntityReadService entities,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
            await JellyfinPlaybackResults.MarkUnplayedAsync(itemId, sessions, entities, httpContext, cancellationToken))
            .WithName("DeleteJellyfinUserPlayedItem")
            .WithSummary("Delete Jellyfin User Played Item.")
            .WithTags("Jellyfin Sessions");

        return routes;
    }
}
