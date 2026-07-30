using Prismedia.Contracts.Entities;
using Prismedia.Domain.Entities;

namespace Prismedia.Api.Endpoints;

public static class ImageEndpoints {
    public static RouteGroupBuilder MapImageEndpoints(this IEndpointRouteBuilder routes) =>
        routes.MapEntityKindRoutes(
            "/api/images",
            EntityKind.Image.ToCode(),
            "Images",
            "ListImages",
            "GetImage");
}
