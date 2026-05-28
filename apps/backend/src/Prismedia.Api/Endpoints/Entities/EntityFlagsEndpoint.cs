using Prismedia.Application.Entities;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.System;

namespace Prismedia.Api.Endpoints;

internal static class EntityFlagsEndpoint {
    internal static RouteGroupBuilder MapEntityFlagsEndpoint(this RouteGroupBuilder group) {
        group.MapPatch("/{id:guid}/flags", async (
            Guid id,
            EntityFlagsUpdateRequest request,
            EntityCapabilityService capabilities,
            CancellationToken cancellationToken) =>
            EntityEndpointResults.ToResult(id, await capabilities.UpdateFlagsAsync(
                id, request.IsFavorite, request.IsNsfw, request.IsOrganized, cancellationToken)))
            .WithName("UpdateEntityFlags")
            .WithSummary("Update Entity Flags.")
            .Produces<EntityCard>()
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        return group;
    }
}
