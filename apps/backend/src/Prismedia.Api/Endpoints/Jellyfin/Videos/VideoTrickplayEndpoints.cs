namespace Prismedia.Api.Endpoints;

internal static class VideoTrickplayEndpoints {
    internal static IEndpointRouteBuilder MapJellyfinVideoTrickplayEndpoints(this IEndpointRouteBuilder routes) {
        routes.MapGet("/Videos/{itemId:guid}/Trickplay/{width:int}/tiles.m3u8", JellyfinPlaybackResults.GetTrickplayPlaylistAsync)
            .WithName("GetJellyfinTrickplayPlaylist")
            .WithSummary("Get Jellyfin Trickplay Playlist.")
            .WithTags("Jellyfin Videos");

        routes.MapGet("/Videos/{itemId:guid}/Trickplay/{width:int}/{index:int}.jpg", JellyfinPlaybackResults.GetTrickplayTileAsync)
            .WithName("GetJellyfinTrickplayTile")
            .WithSummary("Get Jellyfin Trickplay Tile.")
            .WithTags("Jellyfin Videos");

        return routes;
    }
}
