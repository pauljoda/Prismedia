using Prismedia.Application.Entities;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.System;

namespace Prismedia.Api.Endpoints;

internal static class EntityDetailEndpoint {
    internal static RouteGroupBuilder MapEntityDetailEndpoint(this RouteGroupBuilder group) {
        group.MapPatch("/{kind}/{id:guid}", async (
            string kind,
            Guid id,
            EntityMetadataUpdateRequest request,
            IEntityMetadataPatchService metadata,
            IEntityReadService entities,
            CancellationToken cancellationToken) =>
            await PatchEntityAsync(id, kind, request, metadata, entities, cancellationToken))
            .WithName("UpdateEntityByKind")
            .Produces<EntityCard>()
            .Produces<ApiProblem>(StatusCodes.Status400BadRequest)
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        group.MapGet("/{id:guid}", async (
            Guid id,
            bool? hideNsfw,
            HttpContext httpContext,
            IEntityReadService entities,
            CancellationToken cancellationToken) =>
            await EntityEndpointResults.GetEntityAsync(
                id,
                NsfwVisibility.ShouldHide(hideNsfw, httpContext),
                entities,
                cancellationToken))
            .WithName("GetEntity")
            .Produces<EntityCard>()
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        group.MapPatch("/{id:guid}", async (
            Guid id,
            EntityMetadataUpdateRequest request,
            IEntityMetadataPatchService metadata,
            IEntityReadService entities,
            CancellationToken cancellationToken) =>
            await PatchEntityAsync(id, expectedKind: null, request, metadata, entities, cancellationToken))
            .WithName("UpdateEntity")
            .Produces<EntityCard>()
            .Produces<ApiProblem>(StatusCodes.Status400BadRequest)
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        return group;
    }

    internal static async Task<IResult> PatchEntityAsync(
        Guid id,
        string? expectedKind,
        EntityMetadataUpdateRequest request,
        IEntityMetadataPatchService metadata,
        IEntityReadService entities,
        CancellationToken cancellationToken) {
        EntityMetadataPatchResult result;
        try {
            result = await metadata.ApplyPatchAsync(id, request, expectedKind, cancellationToken);
        } catch (ArgumentException ex) {
            return Results.BadRequest(new ApiProblem("invalid_entity_metadata_patch", ex.Message));
        }

        if (result is EntityMetadataPatchResult.NotFound or EntityMetadataPatchResult.KindMismatch) {
            return Results.NotFound(new ApiProblem("entity_not_found", $"Entity '{id}' was not found."));
        }

        return expectedKind is null
            ? await EntityEndpointResults.GetEntityAsync(id, false, entities, cancellationToken)
            : await EntityKindRouteEndpoints.GetKindDetailAsync(id, expectedKind, false, entities, cancellationToken);
    }
}
