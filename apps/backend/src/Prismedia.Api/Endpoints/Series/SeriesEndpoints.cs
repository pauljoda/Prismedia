using Prismedia.Application.Entities;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.System;
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

        routes.MapGet("/api/series/{id:guid}/seasons/{seasonId:guid}", async (
            Guid id,
            Guid seasonId,
            bool? hideNsfw,
            HttpContext httpContext,
            IEntityReadService entities,
            CancellationToken cancellationToken) =>
            await EntityKindRouteEndpoints.GetKindDetailAsync(
                seasonId,
                EntityKind.VideoSeason.ToCode(),
                NsfwVisibility.ShouldHide(hideNsfw, httpContext),
                entities,
                cancellationToken))
            .WithTags("Series")
            .WithName("GetVideoSeason")
            .WithSummary("Get Video Season.")
            .Produces<EntityCard>()
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        return routes;
    }
}
