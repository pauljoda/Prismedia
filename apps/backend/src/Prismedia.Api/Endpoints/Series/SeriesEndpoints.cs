using Prismedia.Application.Entities;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Series;
using Prismedia.Contracts.System;

namespace Prismedia.Api.Endpoints;

public static class SeriesEndpoints {
    public static IEndpointRouteBuilder MapSeriesEndpoints(this IEndpointRouteBuilder routes) {
        routes.MapEntityKindRoutes(
            "/api/series",
            "video-series",
            "Series",
            "ListVideoSeries",
            "GetVideoSeries",
            typeof(EntityListResponse),
            typeof(VideoSeriesDetail));

        routes.MapGet("/api/series/{id:guid}/seasons/{seasonId:guid}", async (
            Guid id,
            Guid seasonId,
            bool? hideNsfw,
            HttpContext httpContext,
            IEntityReadService entities,
            CancellationToken cancellationToken) =>
            await EntityKindRouteEndpoints.GetKindDetailAsync(
                seasonId,
                "video-season",
                NsfwVisibility.ShouldHide(hideNsfw, httpContext),
                entities,
                cancellationToken))
            .WithTags("Series")
            .WithName("GetVideoSeason")
            .WithSummary("Get Video Season.")
            .Produces<VideoSeasonDetail>()
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        return routes;
    }
}
