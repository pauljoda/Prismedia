using Prismedia.Application.Entities;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.System;
using Prismedia.Domain.Entities;

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
        Type detailResponseType,
        bool manageable = false) {
        var group = routes.MapGroup(prefix)
            .WithTags(tag);

        group.MapGet("/", async (
            string? query,
            string? cursor,
            bool? hideNsfw,
            int? limit,
            string? sort,
            string? sortDir,
            int? seed,
            bool? favorite,
            bool? organized,
            int? ratingMin,
            int? ratingMax,
            bool? unrated,
            string? status,
            bool? orphaned,
            HttpContext httpContext,
            IEntityReadService entities,
            CancellationToken cancellationToken) =>
            Results.Ok(await entities.ListAsync(
                kind,
                query,
                cursor,
                NsfwVisibility.ShouldHide(hideNsfw, httpContext),
                limit,
                cancellationToken,
                referencedBy: null,
                relationshipCode: null,
                sort: sort,
                sortDir: sortDir,
                seed: seed,
                favorite: favorite,
                organized: organized,
                ratingMin: ratingMin,
                ratingMax: ratingMax,
                unrated: unrated,
                status: status,
                orphaned: orphaned)))
            .WithName(listName)
            .WithSummary($"List {tag}.")
            .Produces(StatusCodes.Status200OK, listResponseType);

        if (manageable) {
            group.MapManagementRoutes(kind, tag, detailName, detailResponseType);
        }

        group.MapGet("/{id:guid}", async (
            Guid id,
            bool? hideNsfw,
            HttpContext httpContext,
            IEntityReadService entities,
            CancellationToken cancellationToken) =>
            await GetKindDetailAsync(id, kind, NsfwVisibility.ShouldHide(hideNsfw, httpContext), entities, cancellationToken))
            .WithName(detailName)
            .WithSummary($"Get {tag} detail.")
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
            .WithSummary($"Update {tag} detail.")
            .Produces(StatusCodes.Status200OK, detailResponseType)
            .Produces<ApiProblem>(StatusCodes.Status400BadRequest)
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        return group;
    }

    /// <summary>
    /// Maps the create (POST) and delete (DELETE) routes for a user-manageable taxonomy kind.
    /// Create returns the new entity's detail so the client can navigate straight to it.
    /// </summary>
    private static void MapManagementRoutes(
        this RouteGroupBuilder group,
        string kind,
        string tag,
        string detailName,
        Type detailResponseType) {
        // Derive clean operation names (GetTag -> CreateTag / DeleteTag) so the generated client
        // exposes createTag()/deleteTag() rather than awkward Get-prefixed names.
        var baseName = detailName.StartsWith("Get", StringComparison.Ordinal) ? detailName[3..] : detailName;

        group.MapPost("/", async (
            EntityCreateRequest request,
            HttpContext httpContext,
            IEntityManagementService management,
            IEntityReadService entities,
            CancellationToken cancellationToken) => {
                var result = await management.CreateAsync(kind, request, cancellationToken);
                return result.Status switch {
                    EntityCommandStatus.Created when result.Id is { } id =>
                        await CreatedKindDetailAsync(id, kind, httpContext, entities, cancellationToken),
                    EntityCommandStatus.Invalid =>
                        Results.BadRequest(new ApiProblem(ApiProblemCodes.InvalidEntity, result.Message ?? "Invalid request.")),
                    _ => Results.BadRequest(new ApiProblem(ApiProblemCodes.EntityNotCreatable, $"{tag} cannot be created.")),
                };
            })
            .WithName($"Create{baseName}")
            .WithSummary($"Create {tag}.")
            .Produces(StatusCodes.Status201Created, detailResponseType)
            .Produces<ApiProblem>(StatusCodes.Status400BadRequest);

        group.MapDelete("/{id:guid}", async (
            Guid id,
            IEntityManagementService management,
            CancellationToken cancellationToken) => {
                var result = await management.DeleteAsync(id, kind, cancellationToken);
                return result.Status switch {
                    EntityCommandStatus.Deleted => Results.NoContent(),
                    EntityCommandStatus.NotFound =>
                        Results.NotFound(new ApiProblem(ApiProblemCodes.EntityNotFound, $"Entity '{id}' was not found.")),
                    _ => Results.BadRequest(new ApiProblem(ApiProblemCodes.EntityNotDeletable, $"{tag} cannot be deleted.")),
                };
            })
            .WithName($"Delete{baseName}")
            .WithSummary($"Delete {tag}.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> CreatedKindDetailAsync(
        Guid id,
        string kind,
        HttpContext httpContext,
        IEntityReadService entities,
        CancellationToken cancellationToken) {
        var entity = await entities.GetDetailAsync(
            id, kind, NsfwVisibility.ShouldHide(null, httpContext), cancellationToken);
        return entity is null
            ? Results.NotFound(new ApiProblem(ApiProblemCodes.EntityNotFound, $"Entity '{id}' was not found."))
            : Results.Created($"/api/entities/{id}", (object)entity);
    }

    internal static async Task<IResult> GetKindDetailAsync(
        Guid id,
        string kind,
        bool hideNsfw,
        IEntityReadService entities,
        CancellationToken cancellationToken) {
        var entity = await entities.GetDetailAsync(id, kind, hideNsfw, cancellationToken);
        return entity is null
            ? Results.NotFound(new ApiProblem(ApiProblemCodes.EntityNotFound, $"Entity '{id}' was not found."))
            : Results.Ok<object>(entity);
    }
}
