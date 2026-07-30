using Prismedia.Contracts.Entities;
using Prismedia.Domain.Entities;

namespace Prismedia.Api.Endpoints;

public static class GalleryEndpoints {
    public static RouteGroupBuilder MapGalleryEndpoints(this IEndpointRouteBuilder routes) =>
        routes.MapEntityKindRoutes(
            "/api/galleries",
            EntityKindRegistry.Gallery.Code,
            "Galleries",
            "ListGalleries",
            "GetGallery");
}
