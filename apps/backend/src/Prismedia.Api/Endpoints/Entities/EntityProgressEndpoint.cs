using Prismedia.Application.Entities;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.System;

namespace Prismedia.Api.Endpoints;

internal static class EntityProgressEndpoint {
    internal static RouteGroupBuilder MapEntityProgressEndpoint(this RouteGroupBuilder group) {
        group.MapPatch("/{id:guid}/progress", async (
            Guid id,
            EntityProgressUpdateRequest request,
            EntityCapabilityService capabilities,
            CancellationToken cancellationToken) =>
            EntityEndpointResults.ToResult(id, await capabilities.UpdateProgressAsync(
                id,
                request.CurrentEntityId,
                request.Unit,
                request.Index,
                request.Total,
                request.Mode,
                request.Completed,
                request.Reset,
                request.Location,
                cancellationToken)))
            .WithName("UpdateEntityProgress")
            .WithSummary("Update Entity Progress.")
            .Produces<EntityCard>()
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        return group;
    }
}
