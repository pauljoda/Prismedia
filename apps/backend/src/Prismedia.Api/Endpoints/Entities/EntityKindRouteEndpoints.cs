using Microsoft.AspNetCore.Mvc;
using Prismedia.Application.Entities;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.System;
using Prismedia.Domain.Entities;

using Prismedia.Api.Security;

namespace Prismedia.Api.Endpoints;

internal static class EntityKindRouteEndpoints {
    internal static RouteGroupBuilder MapEntityKindRoutes(
        this IEndpointRouteBuilder routes,
        string prefix,
        string kind,
        string tag,
        string listName,
        string kindOperationName) {
        var group = routes.MapGroup(prefix)
            .WithTags(tag);

        group.MapGet("/", async (
            [AsParameters] EntityListParameters request,
            HttpContext httpContext,
            IEntityReadService entities,
            CancellationToken cancellationToken) =>
            await EntityListEndpoint.ListAsync(
                request,
                httpContext,
                entities,
                cancellationToken,
                requiredKind: kind))
            .WithName(listName)
            .WithSummary($"List {tag}.")
            .Produces<EntityListResponse>()
            .Produces<ApiProblem>(StatusCodes.Status400BadRequest);

        if (EntityKindRegistry.TryDescribe(kind, out var definition) && definition.SupportsManualManagement) {
            group.MapManagementRoutes(kind, tag, kindOperationName);
        }

        group.MapPatch("/{id:guid}", async (
            Guid id,
            EntityMetadataUpdateRequest request,
            IEntityMetadataPatchService metadata,
            IEntityReadService entities,
            CancellationToken cancellationToken) =>
            await EntityDetailEndpoint.PatchEntityAsync(id, kind, request, metadata, entities, cancellationToken))
            .RequireAdmin()
            .WithName($"{kindOperationName}Patch")
            .WithSummary($"Update {tag} detail.")
            .Produces<EntityCard>()
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
        string kindOperationName) {
        // Derive clean operation names (GetTag -> CreateTag / DeleteTag) so the generated client
        // exposes createTag()/deleteTag() rather than awkward Get-prefixed names.
        var baseName = kindOperationName.StartsWith("Get", StringComparison.Ordinal)
            ? kindOperationName[3..]
            : kindOperationName;

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
            .RequireAdmin()
            .WithName($"Create{baseName}")
            .WithSummary($"Create {tag}.")
            .Produces<EntityCard>(StatusCodes.Status201Created)
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
            .RequireAdmin()
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
        var entity = await entities.GetAsync(
            id, kind, NsfwVisibility.ShouldHide(null, httpContext), cancellationToken);
        return entity is null
            ? Results.NotFound(new ApiProblem(ApiProblemCodes.EntityNotFound, $"Entity '{id}' was not found."))
            : Results.Created($"/api/entities/{id}", (object)entity);
    }

}
