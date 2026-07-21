using Microsoft.Extensions.Logging;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Requests;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers;

/// <summary>
/// Starts the ordinary monitored acquisition pipeline for children that an interactive reviewed
/// container request already committed. Each child is idempotent: redelivery observes open work or
/// newly imported media and skips it, while provider enrichment remains in the acquisition job chain.
/// </summary>
public sealed class RequestAcquisitionFanoutJobHandler(
    IRequestGraphAcquisitionStarter requests,
    IRequestChildHydrator childHydrator,
    ILogger<RequestAcquisitionFanoutJobHandler> logger) : IJobHandler {
    public JobType Type => JobType.RequestAcquisitionFanout;

    public async Task HandleAsync(JobContext context, CancellationToken cancellationToken) {
        var payload = RequestAcquisitionFanoutPayload.Parse(context.Job.PayloadJson);
        var targeting = new AcquisitionTargeting(payload.TargetLibraryRootId, payload.ProfileId);
        logger.LogInformation(
            "Request acquisition fan-out: starting {Count} committed child request(s).",
            payload.ChildEntityIds.Count);

        for (var index = 0; index < payload.ChildEntityIds.Count; index++) {
            cancellationToken.ThrowIfCancellationRequested();
            var entityId = payload.ChildEntityIds[index];

            // A structural unit's children are search input, not post-search decoration. Hydrate before
            // CreateAndSearch publishes the parent acquisition so a barren album/season search can
            // immediately fall back to its already-materialized tracks/episodes.
            var hydration = await childHydrator.HydrateAsync(
                entityId,
                payload.HideNsfw,
                cancellationToken);
            if (hydration is { Hydrated: false }) {
                logger.LogWarning(
                    "Request acquisition fan-out could not hydrate structural children for Entity {EntityId}; continuing with the whole-unit search.",
                    entityId);
            }

            await requests.RequestEntityFromGraphAsync(
                entityId,
                payload.HideNsfw,
                cancellationToken,
                targeting,
                hydrateChildren: false);
            await context.ReportProgressAsync(
                (index + 1) * 100 / payload.ChildEntityIds.Count,
                $"Started {index + 1} of {payload.ChildEntityIds.Count} requests",
                cancellationToken);
        }
    }
}
