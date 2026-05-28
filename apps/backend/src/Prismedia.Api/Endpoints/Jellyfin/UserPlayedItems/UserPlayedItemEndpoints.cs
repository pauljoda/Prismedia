using Prismedia.Application.Videos;

namespace Prismedia.Api.Endpoints;

internal static class UserPlayedItemEndpoints {
    internal static IEndpointRouteBuilder MapJellyfinUserPlayedItemEndpoints(this IEndpointRouteBuilder routes) {
        routes.MapPost("/UserPlayedItems/{itemId:guid}", async (
            Guid itemId,
            IPlaybackSessionService sessions,
            CancellationToken cancellationToken) =>
            await JellyfinPlaybackResults.MarkPlayedAsync(itemId, sessions, cancellationToken))
            .WithName("PostJellyfinUserPlayedItem")
            .WithSummary("Post Jellyfin User Played Item.")
            .WithTags("Jellyfin Sessions");

        routes.MapDelete("/UserPlayedItems/{itemId:guid}", async (
            Guid itemId,
            IPlaybackSessionService sessions,
            CancellationToken cancellationToken) =>
            await JellyfinPlaybackResults.MarkUnplayedAsync(itemId, sessions, cancellationToken))
            .WithName("DeleteJellyfinUserPlayedItem")
            .WithSummary("Delete Jellyfin User Played Item.")
            .WithTags("Jellyfin Sessions");

        return routes;
    }
}
