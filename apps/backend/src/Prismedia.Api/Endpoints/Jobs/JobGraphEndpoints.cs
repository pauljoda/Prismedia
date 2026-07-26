using Prismedia.Application.Jobs;
using Prismedia.Contracts.Jobs;

namespace Prismedia.Api.Endpoints;

internal static class JobGraphEndpoints {
    internal static RouteGroupBuilder MapJobGraphEndpoints(this RouteGroupBuilder group) {
        group.MapGet("/graphs", async (
            bool? hideNsfw,
            HttpContext httpContext,
            JobService jobs,
            CancellationToken cancellationToken) =>
            await jobs.ListGraphsAsync(
                NsfwVisibility.ShouldHide(hideNsfw, httpContext),
                cancellationToken))
            .WithName("ListJobGraphs")
            .WithSummary("Lists durable job graphs and logical execution lanes.")
            .Produces<JobGraphListResponse>();

        group.MapGet("/graphs/{graphId:guid}", async (
            Guid graphId,
            bool? hideNsfw,
            HttpContext httpContext,
            JobService jobs,
            CancellationToken cancellationToken) =>
            await jobs.GetGraphAsync(
                graphId,
                NsfwVisibility.ShouldHide(hideNsfw, httpContext),
                cancellationToken) is { } graph
                ? Results.Ok(graph)
                : Results.NotFound())
            .WithName("GetJobGraph")
            .WithSummary("Gets nodes, dependencies, warnings, and waits for one job graph.")
            .Produces<JobGraphDetailResponse>()
            .Produces(StatusCodes.Status404NotFound);

        group.MapDelete("/graphs/{graphId:guid}", async (
            Guid graphId,
            JobService jobs,
            CancellationToken cancellationToken) =>
            Results.Ok(await jobs.CancelGraphAsync(graphId, cancellationToken)))
            .WithName("CancelJobGraph")
            .WithSummary("Cancels a graph, its active nodes, and its open waits.")
            .Produces<JobGraphCancelResponse>();

        return group;
    }
}
