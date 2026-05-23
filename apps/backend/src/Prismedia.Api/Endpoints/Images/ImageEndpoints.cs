using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Media;

namespace Prismedia.Api.Endpoints;

public static class ImageEndpoints {
    public static RouteGroupBuilder MapImageEndpoints(this IEndpointRouteBuilder routes) =>
        routes.MapEntityKindRoutes(
            "/api/images",
            "image",
            "Images",
            "ListImages",
            "GetImage",
            typeof(EntityListResponse),
            typeof(ImageDetail));
}
