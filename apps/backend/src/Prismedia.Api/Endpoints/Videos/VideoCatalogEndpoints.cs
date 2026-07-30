using Prismedia.Contracts.Entities;
using Prismedia.Domain.Entities;

namespace Prismedia.Api.Endpoints;

internal static class VideoCatalogEndpoints {
    internal static RouteGroupBuilder MapVideoCatalogEndpoints(this IEndpointRouteBuilder routes) =>
        routes.MapEntityKindRoutes(
            "/api/videos",
            EntityKindRegistry.Video.Code,
            "Videos",
            "ListVideos",
            "GetVideo");
}
