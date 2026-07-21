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
    RequestCommitService requests,
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
            var response = await requests.RequestEntityFromGraphAsync(
                entityId,
                payload.HideNsfw,
                cancellationToken,
                targeting,
                hydrateChildren: false);
            if (response?.Items.Any(item => item.Outcome == RequestCommitOutcome.AlreadyRequested) == true) {
                // A repeat artist request is also a repair pass: older album acquisitions may predate the
                // persisted plugin route or their enrichment job may have completed before tracks existed.
                // Hydrate in this durable job, never on the interactive commit boundary.
                await childHydrator.HydrateAsync(entityId, payload.HideNsfw, cancellationToken);
            }
            await context.ReportProgressAsync(
                (index + 1) * 100 / payload.ChildEntityIds.Count,
                $"Started {index + 1} of {payload.ChildEntityIds.Count} requests",
                cancellationToken);
        }
    }
}
