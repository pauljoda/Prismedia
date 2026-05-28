using Prismedia.Application.Entities;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.System;

namespace Prismedia.Api.Endpoints;

internal static class EntityPlaybackEndpoint {
    internal static RouteGroupBuilder MapEntityPlaybackEndpoint(this RouteGroupBuilder group) {
        group.MapPatch("/{id:guid}/playback", async (
            Guid id,
            PlaybackUpdateRequest request,
            EntityCapabilityService capabilities,
            CancellationToken cancellationToken) =>
            EntityEndpointResults.ToResult(id, await capabilities.UpdatePlaybackAsync(
                id, request.ResumeSeconds, request.DurationSeconds, request.Completed, cancellationToken)))
            .WithName("UpdateEntityPlayback")
            .WithSummary("Update Entity Playback.")
            .Produces<EntityCard>()
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        return group;
    }
}
