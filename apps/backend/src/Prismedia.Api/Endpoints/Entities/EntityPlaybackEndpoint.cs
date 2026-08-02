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
                id,
                request.ResumeSeconds,
                request.DurationSeconds,
                request.Completed,
                request.UtcOffsetMinutes,
                cancellationToken)))
            .WithName("UpdateEntityPlayback")
            .WithSummary("Update timed Entity resume and active consumption.")
            .Produces<EntityCard>()
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/playback/events", async (
            Guid id,
            PlaybackEventCreateRequest request,
            EntityCapabilityService capabilities,
            CancellationToken cancellationToken) =>
            EntityEndpointResults.ToResult(id, await capabilities.RecordPlaybackEventAsync(
                id,
                request.Kind,
                request.OccurredAt,
                request.PositionSeconds,
                request.DurationSeconds,
                request.SessionId,
                cancellationToken)))
            .WithName("CreateEntityPlaybackEvent")
            .WithSummary("Create a timestamped Entity consumption event.")
            .Produces<EntityCard>()
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        return group;
    }
}
