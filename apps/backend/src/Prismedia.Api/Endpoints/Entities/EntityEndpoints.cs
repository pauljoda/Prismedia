namespace Prismedia.Api.Endpoints;

public static class EntityEndpoints {
    public static IEndpointRouteBuilder MapEntityEndpoints(this IEndpointRouteBuilder routes) {
        var group = routes.MapGroup("/api/entities")
            .WithTags("Entities");

        group.MapEntityListEndpoint();
        group.MapEntityDetailEndpoint();
        group.MapEntityThumbnailEndpoint();
        group.MapEntityImageAssetEndpoint();
        group.MapEntityRatingEndpoint();
        group.MapEntityFlagsEndpoint();
        group.MapEntityFileEndpoint();
        group.MapEntityPlaybackEndpoint();
        group.MapEntityProgressEndpoint();
        group.MapEntityMarkerEndpoints();
        group.MapEntityRefreshEndpoint();

        return routes;
    }
}
