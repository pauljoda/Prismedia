using Prismedia.Contracts.Entities;
using Prismedia.Domain.Entities;

namespace Prismedia.Api.Endpoints;

public static class StudioEndpoints {
    public static RouteGroupBuilder MapStudioEndpoints(this IEndpointRouteBuilder routes) =>
        routes.MapEntityKindRoutes(
            "/api/studios",
            EntityKind.Studio.ToCode(),
            "Taxonomy",
            "ListStudios",
            "GetStudio");
}
