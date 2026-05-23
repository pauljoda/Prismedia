namespace Prismedia.Api.Endpoints;

public static class UserStateEndpoints {
    public static IEndpointRouteBuilder MapUserStateEndpoints(this IEndpointRouteBuilder routes) {
        routes.MapUpdateCheckEndpoints();
        routes.MapPlaylistSessionEndpoints();
        return routes;
    }
}
