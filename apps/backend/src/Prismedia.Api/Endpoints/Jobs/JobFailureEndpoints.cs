using Microsoft.AspNetCore.Mvc;
using Prismedia.Application.Jobs;
using Prismedia.Contracts.Jobs;
using Prismedia.Contracts.System;

namespace Prismedia.Api.Endpoints;

internal static class JobFailureEndpoints {
    internal static RouteGroupBuilder MapJobFailureEndpoints(this RouteGroupBuilder group) {
        group.MapPost("/failures/clear", async (
            [FromQuery] string? type,
            JobService jobs,
            CancellationToken cancellationToken) => {
                if (!JobRouteValues.TryDecodeJobType(type, out var jobType)) {
                    return Results.BadRequest(new ApiProblem("unknown_job_type", $"Unknown job type '{type}'."));
                }

                return Results.Ok(await jobs.ClearFailuresAsync(jobType, cancellationToken));
            })
            .WithName("ClearJobFailures")
            .WithSummary("Clears failed job runs from the operations dashboard.")
            .Produces<JobFailureClearResponse>()
            .Produces<ApiProblem>(StatusCodes.Status400BadRequest);

        return group;
    }
}
