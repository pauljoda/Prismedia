using Prismedia.Application.Entities;
using Prismedia.Application.Videos;

namespace Prismedia.Api.Endpoints;

internal static class UserPlayedItemEndpoints {
    internal static IEndpointRouteBuilder MapJellyfinUserPlayedItemEndpoints(this IEndpointRouteBuilder routes) {
        async Task<IResult> markPlayed(
            Guid itemId,
            IPlaybackSessionService sessions,
            IEntityReadService entities,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
            await JellyfinPlaybackResults.MarkPlayedAsync(itemId, sessions, entities, httpContext, cancellationToken);

        async Task<IResult> markUnplayed(
            Guid itemId,
            IPlaybackSessionService sessions,
            IEntityReadService entities,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
            await JellyfinPlaybackResults.MarkUnplayedAsync(itemId, sessions, entities, httpContext, cancellationToken);
        async Task<IResult> markUserScopedPlayed(
            Guid userId,
            Guid itemId,
            IPlaybackSessionService sessions,
            IEntityReadService entities,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
            await markPlayed(itemId, sessions, entities, httpContext, cancellationToken);
        async Task<IResult> markUserScopedUnplayed(
            Guid userId,
            Guid itemId,
            IPlaybackSessionService sessions,
            IEntityReadService entities,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
            await markUnplayed(itemId, sessions, entities, httpContext, cancellationToken);

        routes.MapPost("/UserPlayedItems/{itemId:guid}", markPlayed)
            .WithName("PostJellyfinUserPlayedItem")
            .WithSummary("Post Jellyfin User Played Item.")
            .WithTags("Jellyfin Sessions");

        routes.MapDelete("/UserPlayedItems/{itemId:guid}", markUnplayed)
            .WithName("DeleteJellyfinUserPlayedItem")
            .WithSummary("Delete Jellyfin User Played Item.")
            .WithTags("Jellyfin Sessions");

        routes.MapPost("/Users/{userId:guid}/PlayedItems/{itemId:guid}", markUserScopedPlayed)
            .WithName("PostJellyfinUserScopedPlayedItem")
            .WithSummary("Post Jellyfin User-Scoped Played Item.")
            .WithTags("Jellyfin Sessions");

        routes.MapDelete("/Users/{userId:guid}/PlayedItems/{itemId:guid}", markUserScopedUnplayed)
            .WithName("DeleteJellyfinUserScopedPlayedItem")
            .WithSummary("Delete Jellyfin User-Scoped Played Item.")
            .WithTags("Jellyfin Sessions");

        return routes;
    }
}
