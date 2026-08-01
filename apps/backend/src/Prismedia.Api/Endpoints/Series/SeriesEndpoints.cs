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
            await GetVideoSeasonDetailAsync(
                id,
                seasonId,
                NsfwVisibility.ShouldHide(hideNsfw, httpContext),
                entities,
                cancellationToken))
            .WithTags("Series")
            .WithName("GetVideoSeason")
            .WithSummary("Get Video Season (deprecated; use GET /api/entities/{id}).")
            .AddOpenApiOperationTransformer((operation, _, _) => {
                operation.Deprecated = true;
                operation.Description = "Deprecated compatibility alias. Use GET /api/entities/{id}.";
                return Task.CompletedTask;
            })
            .Produces<EntityCard>()
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        return routes;
    }

    private static async Task<IResult> GetVideoSeasonDetailAsync(
        Guid seriesId,
        Guid seasonId,
        bool hideNsfw,
        IEntityReadService entities,
        CancellationToken cancellationToken) {
        var season = await entities.GetAsync(
            seasonId,
            EntityKind.VideoSeason.ToCode(),
            hideNsfw,
            cancellationToken);
        return season is null || season.ParentEntityId != seriesId
            ? Results.NotFound(new ApiProblem(ApiProblemCodes.EntityNotFound, $"Entity '{seasonId}' was not found."))
            : Results.Ok<object>(season);
    }
}
