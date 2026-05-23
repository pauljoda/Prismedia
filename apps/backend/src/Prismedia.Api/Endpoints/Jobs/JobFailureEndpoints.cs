using Microsoft.AspNetCore.Mvc;
using Prismedia.Application.Jobs;

namespace Prismedia.Api.Endpoints;

internal static class JobFailureEndpoints {
    internal static RouteGroupBuilder MapJobFailureEndpoints(this RouteGroupBuilder group) {
        group.MapPost("/failures/clear", async (
            [FromQuery] string? type,
            JobService jobs,
            CancellationToken cancellationToken) => {
                if (!JobRouteValues.TryDecodeJobType(type, out var jobType)) {
                    return Results.BadRequest(new { message = $"Unknown job type '{type}'." });
                }

                return Results.Ok(await jobs.ClearFailuresAsync(jobType, cancellationToken));
            })
            .WithName("ClearJobFailures")
            .WithSummary("Clears failed job runs from the operations dashboard.");

        return group;
    }
}
