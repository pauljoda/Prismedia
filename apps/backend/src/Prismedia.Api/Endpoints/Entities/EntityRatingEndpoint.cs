using Prismedia.Application.Entities;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.System;

namespace Prismedia.Api.Endpoints;

internal static class EntityRatingEndpoint {
    internal static RouteGroupBuilder MapEntityRatingEndpoint(this RouteGroupBuilder group) {
        group.MapPatch("/{id:guid}/rating", async (
            Guid id,
            RatingUpdateRequest request,
            EntityCapabilityService capabilities,
            CancellationToken cancellationToken) =>
            EntityEndpointResults.ToResult(id, await capabilities.RateAsync(id, request.Value, cancellationToken)))
            .WithName("UpdateEntityRating")
            .WithSummary("Update Entity Rating.")
            .Produces<EntityCard>()
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        return group;
    }
}
