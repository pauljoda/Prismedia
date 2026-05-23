namespace Prismedia.Api.Endpoints;

public static class IdentifyEndpoints {
    public static RouteGroupBuilder MapIdentifyEndpoints(this IEndpointRouteBuilder routes) {
        var group = routes.MapGroup("/api/identify")
            .WithTags("Identify");

        group.MapIdentifyProviderEndpoints();
        group.MapIdentifyEntityEndpoints();
        group.MapIdentifyBulkEndpoints();

        return group;
    }
}
