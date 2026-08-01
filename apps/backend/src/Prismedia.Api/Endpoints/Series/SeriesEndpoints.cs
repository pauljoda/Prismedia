using Prismedia.Domain.Entities;

namespace Prismedia.Api.Endpoints;

public static class SeriesEndpoints {
    public static IEndpointRouteBuilder MapSeriesEndpoints(this IEndpointRouteBuilder routes) {
        routes.MapEntityKindRoutes(
            "/api/series",
            EntityKind.VideoSeries.ToCode(),
            "Series",
            "ListVideoSeries",
            "GetVideoSeries");

        return routes;
    }
}
