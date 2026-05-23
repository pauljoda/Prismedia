using Prismedia.Application.Entities;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.System;

namespace Prismedia.Api.Endpoints;

internal static class EntityKindRouteEndpoints {
    internal static RouteGroupBuilder MapEntityKindRoutes(
        this IEndpointRouteBuilder routes,
        string prefix,
        string kind,
        string tag,
        string listName,
        string detailName,
        Type listResponseType,
        Type detailResponseType) {
        var group = routes.MapGroup(prefix)
            .WithTags(tag);

        group.MapGet("/", async (
            string? query,
            string? cursor,
            bool? hideNsfw,
            int? limit,
            HttpContext httpContext,
            IEntityReadService entities,
            CancellationToken cancellationToken) =>
            Results.Ok(await entities.ListAsync(kind, query, cursor, NsfwVisibility.ShouldHide(hideNsfw, httpContext), limit, cancellationToken)))
            .WithName(listName)
            .Produces(StatusCodes.Status200OK, listResponseType);

        group.MapGet("/{id:guid}", async (
            Guid id,
            bool? hideNsfw,
            HttpContext httpContext,
            IEntityReadService entities,
            CancellationToken cancellationToken) =>
            await GetKindDetailAsync(id, kind, NsfwVisibility.ShouldHide(hideNsfw, httpContext), entities, cancellationToken))
            .WithName(detailName)
            .Produces(StatusCodes.Status200OK, detailResponseType)
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        group.MapPatch("/{id:guid}", async (
            Guid id,
            EntityMetadataUpdateRequest request,
            IEntityMetadataPatchService metadata,
            IEntityReadService entities,
            CancellationToken cancellationToken) =>
            await EntityDetailEndpoint.PatchEntityAsync(id, kind, request, metadata, entities, cancellationToken))
            .WithName($"{detailName}Patch")
            .Produces(StatusCodes.Status200OK, detailResponseType)
            .Produces<ApiProblem>(StatusCodes.Status400BadRequest)
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        return group;
    }

    internal static async Task<IResult> GetKindDetailAsync(
        Guid id,
        string kind,
        bool hideNsfw,
        IEntityReadService entities,
        CancellationToken cancellationToken) {
        var entity = await entities.GetDetailAsync(id, kind, hideNsfw, cancellationToken);
        return entity is null
            ? Results.NotFound(new ApiProblem("entity_not_found", $"Entity '{id}' was not found."))
            : Results.Ok<object>(entity);
    }
}
