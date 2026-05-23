namespace Prismedia.Api.Endpoints;

public static class JellyfinPlaybackEndpoints {
    public static IEndpointRouteBuilder MapJellyfinPlaybackEndpoints(this IEndpointRouteBuilder routes) {
        routes.MapJellyfinItemPlaybackInfoEndpoints();
        routes.MapJellyfinVideoStreamEndpoints();
        routes.MapJellyfinVideoHlsEndpoints();
        routes.MapJellyfinVideoTranscodeEndpoints();
        routes.MapJellyfinVideoTrickplayEndpoints();
        routes.MapJellyfinSessionEndpoints();
        routes.MapJellyfinUserPlayedItemEndpoints();

        return routes;
    }
}
