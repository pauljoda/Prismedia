using Microsoft.Extensions.Logging;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Requests;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers;

/// <summary>
/// Applies the cached reviewed graph, then starts the ordinary monitored acquisition pipeline for its
/// committed children. Each child is idempotent: redelivery observes open work or newly imported media
/// and skips it. A metadata failure stops the job before any release search can be published.
/// </summary>
[JobDefinition(JobType.RequestAcquisitionFanout)]
public sealed class RequestAcquisitionFanoutJobHandler(
    IRequestGraphAcquisitionStarter requests,
    IRequestChildHydrator childHydrator,
    ILogger<RequestAcquisitionFanoutJobHandler> logger) : IJobHandler {
    public async Task HandleAsync(JobContext context, CancellationToken cancellationToken) {
        var payload = RequestAcquisitionFanoutPayload.Parse(context.Job.PayloadJson);
        if (payload.ReviewedProposal is { } reviewedProposal) {
            await requests.ApplyReviewedMetadataAsync(
                reviewedProposal.TargetEntityId!.Value,
                reviewedProposal,
                cancellationToken);
        }

        var targeting = new AcquisitionTargeting(payload.TargetLibraryRootId, payload.ProfileId);
        logger.LogInformation(
            "Request acquisition fan-out: starting {Count} committed child request(s).",
            payload.ChildEntityIds.Count);

        for (var index = 0; index < payload.ChildEntityIds.Count; index++) {
            cancellationToken.ThrowIfCancellationRequested();
            var entityId = payload.ChildEntityIds[index];

            if (payload.HydrateChildren) {
                // Artist reviews intentionally commit shallow album shells. Hydrate those children before
                // CreateAndSearch so a barren album search can immediately fall back to its tracks. Series
                // reviews carry their cached episode graph in this job and skip this provider refetch.
                var hydration = await childHydrator.HydrateAsync(
                    entityId,
                    payload.HideNsfw,
                    cancellationToken);
                if (hydration is { Hydrated: false }) {
                    logger.LogWarning(
                        "Request acquisition fan-out could not hydrate structural children for Entity {EntityId}; continuing with the whole-unit search.",
                        entityId);
                }
            }

            await requests.RequestEntityFromGraphAsync(
                entityId,
                payload.HideNsfw,
                cancellationToken,
                targeting,
                bookRendition: null,
                hydrateChildren: false,
                context,
                context.Job.GraphOrigin ?? JobGraphOrigin.Interactive);
            await context.ReportProgressAsync(
                (index + 1) * 100 / payload.ChildEntityIds.Count,
                $"Started {index + 1} of {payload.ChildEntityIds.Count} requests",
                cancellationToken);
        }
    }
}
