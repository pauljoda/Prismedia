namespace Prismedia.Api.Endpoints;

public static class EntityEndpoints {
    public static IEndpointRouteBuilder MapEntityEndpoints(this IEndpointRouteBuilder routes) {
        var group = routes.MapGroup("/api/entities")
            .WithTags("Entities");

        group.MapEntityListEndpoint();
        group.MapEntityDetailEndpoint();
        group.MapEntityThumbnailEndpoint();
        group.MapEntityChildrenEndpoint();
        group.MapEntityImageAssetEndpoint();
        group.MapEntityRatingEndpoint();
        group.MapEntityFlagsEndpoint();
        group.MapEntityFileEndpoint();
        group.MapEntityConsumptionEndpoint();
        group.MapEntityProgressEndpoint();
        group.MapEntityMarkerEndpoints();
        group.MapEntityRefreshEndpoint();
        group.MapEntityDeleteEndpoint();

        return routes;
    }
}
