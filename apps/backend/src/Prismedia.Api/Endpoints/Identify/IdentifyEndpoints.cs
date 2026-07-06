using Prismedia.Api.Security;

namespace Prismedia.Api.Endpoints;

public static class IdentifyEndpoints {
    public static RouteGroupBuilder MapIdentifyEndpoints(this IEndpointRouteBuilder routes) {
        var group = routes.MapGroup("/api/identify")
            .RequireAdmin()
            .WithTags("Identify");

        group.MapIdentifyProviderEndpoints();
        group.MapIdentifyEntityEndpoints();
        group.MapIdentifyQueueEndpoints();
        group.MapIdentifyBulkEndpoints();

        return group;
    }
}
