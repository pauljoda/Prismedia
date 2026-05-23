using Prismedia.Application.Jobs;
using Prismedia.Domain.Entities;

namespace Prismedia.Api.Endpoints;

/// <summary>
/// POST /api/entities/{id}/refresh — queues a refresh-entity job that re-runs the
/// processing pipeline (probe, fingerprint, preview, subtitles) for the entity and
/// its structural children.
/// </summary>
internal static class EntityRefreshEndpoint {
    internal static RouteGroupBuilder MapEntityRefreshEndpoint(this RouteGroupBuilder group) {
        group.MapPost("/{id:guid}/refresh", async (
                Guid id,
                IJobQueueService queue,
                CancellationToken cancellationToken) => {
            var pending = await queue.HasPendingAsync(JobType.RefreshEntity, id.ToString(), cancellationToken);
            if (pending)
                return Results.Ok(new EntityRefreshResponse(null, AlreadyPending: true));

            var job = await queue.EnqueueAsync(
                new EnqueueJobRequest(
                    JobType.RefreshEntity,
                    TargetEntityKind: "entity",
                    TargetEntityId: id.ToString(),
                    Priority: 25),
                cancellationToken);

            return Results.Ok(new EntityRefreshResponse(job.Id, AlreadyPending: false));
        })
            .WithName("RefreshEntity")
            .Produces<EntityRefreshResponse>();

        return group;
    }
}

/// <summary>
/// Response from the refresh-entity endpoint.
/// </summary>
/// <param name="JobId">Queued job identifier, or null when a refresh was already pending.</param>
/// <param name="AlreadyPending">True when a refresh job is already queued or running for this entity.</param>
public sealed record EntityRefreshResponse(Guid? JobId, bool AlreadyPending);
