using Prismedia.Application.Jobs;
using Prismedia.Contracts.Jobs;

namespace Prismedia.Api.Endpoints;

internal static class JobCreateEndpoint {
    internal static RouteGroupBuilder MapJobCreateEndpoint(this RouteGroupBuilder group) {
        group.MapPost("/{type}", async (
            JobTypeRoute type,
            JobService jobs,
            CancellationToken cancellationToken) => {
                var response = await jobs.CreateAsync(type.Value, cancellationToken);
                return Results.Accepted($"/api/jobs/{response.Job.Id}", response);
            })
            .WithName("CreateJob")
            .WithSummary("Queues a background job run.")
            .Produces<JobCreateResponse>(StatusCodes.Status202Accepted);

        return group;
    }
}
