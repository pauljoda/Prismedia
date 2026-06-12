using Prismedia.Application.Jobs;
using Prismedia.Application.Jobs.Handlers;
using Prismedia.Contracts.Jobs;
using Prismedia.Contracts.Plugins;
using Prismedia.Contracts.System;
using Prismedia.Domain.Entities;

namespace Prismedia.Api.Endpoints;

internal static class IdentifyBulkEndpoints {
    internal static RouteGroupBuilder MapIdentifyBulkEndpoints(this RouteGroupBuilder group) {
        group.MapPost("/bulk", async (
            IdentifyBulkStartRequest request,
            bool? hideNsfw,
            HttpContext httpContext,
            IJobQueueService queue,
            CancellationToken cancellationToken) => {
                if (request.EntityIds.Count == 0) {
                    return Results.BadRequest(new ApiProblem(ApiProblemCodes.EmptyBulkIdentify, "Bulk identify requires at least one entity."));
                }

                var payload = new BulkIdentifyPayload(
                    request.EntityIds,
                    request.Provider,
                    request.Query,
                    NsfwVisibility.ShouldHide(hideNsfw, httpContext));

                var job = await queue.EnqueueAsync(
                    new EnqueueJobRequest(
                        Type: JobType.BulkIdentify,
                        PayloadJson: payload.ToJson(),
                        TargetLabel: $"Bulk identify {request.EntityIds.Count} entities",
                        Priority: JobPriorities.InteractiveIdentify),
                    cancellationToken);

                return Results.Accepted(
                    $"/api/jobs/{job.Id}",
                    new JobCreateResponse(new JobRun(
                        job.Id,
                        job.Type,
                        job.Status,
                        job.Progress,
                        job.Message,
                        job.TargetEntityKind,
                        job.TargetEntityId,
                        job.TargetLabel,
                        job.CreatedAt,
                        job.StartedAt,
                        job.FinishedAt)));
            })
            .WithName("StartBulkIdentify")
            .WithSummary("Enqueues a bulk identify job that searches a provider for each entity.")
            .Produces<JobCreateResponse>(StatusCodes.Status202Accepted)
            .Produces<ApiProblem>(StatusCodes.Status400BadRequest);

        return group;
    }
}
