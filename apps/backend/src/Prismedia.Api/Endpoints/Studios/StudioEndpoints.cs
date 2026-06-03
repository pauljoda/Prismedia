using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Taxonomy;

namespace Prismedia.Api.Endpoints;

public static class StudioEndpoints {
    public static RouteGroupBuilder MapStudioEndpoints(this IEndpointRouteBuilder routes) =>
        routes.MapEntityKindRoutes(
            "/api/studios",
            "studio",
            "Taxonomy",
            "ListStudios",
            "GetStudio",
            typeof(EntityListResponse),
            typeof(StudioDetail),
            manageable: true);
}
