using Prismedia.Contracts.Plugins;
using Prismedia.Contracts.System;
using Prismedia.Contracts.Entities;
using Prismedia.Application.Entities;
using Prismedia.Infrastructure.Plugins;

namespace Prismedia.Api.Endpoints;

internal static class IdentifyEntityEndpoints {
    internal static RouteGroupBuilder MapIdentifyEntityEndpoints(this RouteGroupBuilder group) {
        group.MapPost("/entities/{entityId:guid}", async (
            Guid entityId,
            IdentifyEntityRequest request,
            bool? hideNsfw,
            HttpContext httpContext,
            IdentifyPluginService identify,
            CancellationToken cancellationToken) => {
                var response = await identify.IdentifyAsync(
                    entityId,
                    request.Provider,
                    request.Query,
                    NsfwVisibility.ShouldHide(hideNsfw, httpContext),
                    cancellationToken);
                return response.Ok
                    ? Results.Ok(response.Result)
                    : Results.BadRequest(new ApiProblem("identify_failed", response.Error ?? "Identify failed."));
            })
            .WithName("IdentifyEntity")
            .WithSummary("Runs one transient metadata identify lookup for an entity.")
            .Produces<EntityMetadataProposal>()
            .Produces<ApiProblem>(StatusCodes.Status400BadRequest);

        group.MapPost("/entities/{entityId:guid}/apply", async (
            Guid entityId,
            ApplyIdentifyProposalRequest request,
            IEntityMetadataPatchService metadata,
            CancellationToken cancellationToken) => {
                EntityMetadataPatchResult result;
                try {
                    result = await metadata.ApplyPatchAsync(
                        entityId,
                        new EntityMetadataUpdateRequest(
                            request.SelectedFields,
                            request.Proposal.Patch,
                            request.SelectedImages,
                            request.Proposal.Children,
                            request.Proposal.Relationships),
                        expectedKind: null,
                        cancellationToken);
                } catch (ArgumentException ex) {
                    return Results.BadRequest(new ApiProblem("invalid_entity_metadata_patch", ex.Message));
                }

                if (result is EntityMetadataPatchResult.NotFound or EntityMetadataPatchResult.KindMismatch) {
                    return Results.NotFound(new ApiProblem("entity_not_found", $"Entity '{entityId}' was not found."));
                }

                return Results.NoContent();
            })
            .WithName("ApplyIdentifyProposal")
            .WithSummary("Applies selected fields from a transient identify proposal to the entity.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ApiProblem>(StatusCodes.Status400BadRequest)
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        return group;
    }
}
