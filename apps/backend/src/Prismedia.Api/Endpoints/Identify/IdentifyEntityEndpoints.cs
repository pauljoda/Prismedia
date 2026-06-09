using Prismedia.Contracts.Plugins;
using Prismedia.Contracts.System;
using Prismedia.Application.Plugins;

namespace Prismedia.Api.Endpoints;

internal static class IdentifyEntityEndpoints {
    internal static RouteGroupBuilder MapIdentifyEntityEndpoints(this RouteGroupBuilder group) {
        group.MapPost("/entities/{entityId:guid}", async (
            Guid entityId,
            IdentifyEntityRequest request,
            bool? hideNsfw,
            HttpContext httpContext,
            IIdentifyProviderService identify,
            CancellationToken cancellationToken) => {
                var response = await identify.IdentifyAsync(
                    entityId,
                    request.Provider,
                    request.Query,
                    request.ParentExternalIds,
                    NsfwVisibility.ShouldHide(hideNsfw, httpContext),
                    cancellationToken);
                return response.Ok
                    ? Results.Ok(response.Result)
                    : Results.BadRequest(new ApiProblem(ApiProblemCodes.IdentifyFailed, response.Error ?? "Identify failed."));
            })
            .WithName("IdentifyEntity")
            .WithSummary("Runs one transient metadata identify lookup for an entity.")
            .Produces<EntityMetadataProposal>()
            .Produces<ApiProblem>(StatusCodes.Status400BadRequest);

        group.MapPost("/entities/{entityId:guid}/apply", async (
            Guid entityId,
            ApplyIdentifyProposalRequest request,
            IIdentifyProviderService identify,
            CancellationToken cancellationToken) => {
                // Proposal applies share the queue-accept pipeline (upsert semantics, plugin
                // provenance, NSFW propagation) rather than the manual-edit replace pipeline.
                var applied = await identify.ApplyAsync(
                    entityId,
                    request.Proposal,
                    request.SelectedFields,
                    request.SelectedImages,
                    cancellationToken);
                return applied
                    ? Results.NoContent()
                    : Results.NotFound(new ApiProblem(ApiProblemCodes.EntityNotFound, $"Entity '{entityId}' was not found."));
            })
            .WithName("ApplyIdentifyProposal")
            .WithSummary("Applies selected fields from a transient identify proposal to the entity.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        return group;
    }
}
