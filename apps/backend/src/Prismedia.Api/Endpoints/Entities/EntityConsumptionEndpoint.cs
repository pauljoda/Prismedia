using Prismedia.Application.Entities;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.System;

namespace Prismedia.Api.Endpoints;

internal static class EntityConsumptionEndpoint {
    internal static RouteGroupBuilder MapEntityConsumptionEndpoint(this RouteGroupBuilder group) {
        group.MapPatch("/{id:guid}/consumption", async (
            Guid id,
            ConsumptionUpdateRequest request,
            EntityCapabilityService capabilities,
            CancellationToken cancellationToken) =>
            EntityEndpointResults.ToResult(id, await capabilities.UpdateConsumptionAsync(
                id,
                request.PositionSeconds,
                request.ActivitySeconds,
                request.Completed,
                request.UtcOffsetMinutes,
                cancellationToken)))
            .WithName("UpdateEntityConsumption")
            .WithSummary("Update time-based Entity position and active consumption.")
            .Produces<EntityCard>()
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/consumption/events", async (
            Guid id,
            ConsumptionEventCreateRequest request,
            EntityCapabilityService capabilities,
            CancellationToken cancellationToken) =>
            EntityEndpointResults.ToResult(id, await capabilities.RecordConsumptionEventAsync(
                id,
                request.Kind,
                request.OccurredAt,
                request.PositionSeconds,
                request.DurationSeconds,
                request.SessionId,
                cancellationToken)))
            .WithName("CreateEntityConsumptionEvent")
            .WithSummary("Create a timestamped Entity consumption event.")
            .Produces<EntityCard>()
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        return group;
    }
}
