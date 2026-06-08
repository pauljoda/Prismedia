using Microsoft.AspNetCore.Mvc;
using Prismedia.Application.Jobs;
using Prismedia.Contracts.Jobs;
using Prismedia.Contracts.System;

namespace Prismedia.Api.Endpoints;

internal static class JobCancelEndpoints {
    internal static RouteGroupBuilder MapJobCancelEndpoints(this RouteGroupBuilder group) {
        group.MapDelete("/", async (
            [FromQuery] string? type,
            JobService jobs,
            CancellationToken cancellationToken) => {
                if (!JobRouteValues.TryDecodeJobType(type, out var jobType)) {
                    return Results.BadRequest(new ApiProblem(ApiProblemCodes.UnknownJobType, $"Unknown job type '{type}'."));
                }

                return Results.Ok(await jobs.CancelAsync(jobType, cancellationToken));
            })
            .WithName("CancelJobs")
            .WithSummary("Cancels queued or running job runs.")
            .Produces<JobCancelResponse>()
            .Produces<ApiProblem>(StatusCodes.Status400BadRequest);

        group.MapDelete("/{id:guid}", async (
            Guid id,
            JobService jobs,
            CancellationToken cancellationToken) =>
            Results.Ok(await jobs.CancelRunAsync(id, cancellationToken)))
            .WithName("CancelJobRun")
            .WithSummary("Cancels one queued or running job run.")
            .Produces<JobCancelResponse>();

        return group;
    }
}
