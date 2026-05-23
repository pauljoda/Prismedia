using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Videos;

namespace Prismedia.Api.Endpoints;

internal static class VideoCatalogEndpoints {
    internal static RouteGroupBuilder MapVideoCatalogEndpoints(this IEndpointRouteBuilder routes) =>
        routes.MapEntityKindRoutes(
            "/api/videos",
            "video",
            "Videos",
            "ListVideos",
            "GetVideo",
            typeof(EntityListResponse),
            typeof(VideoDetail));
}
