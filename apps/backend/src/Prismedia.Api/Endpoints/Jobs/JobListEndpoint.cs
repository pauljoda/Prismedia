using Prismedia.Application.Jobs;

namespace Prismedia.Api.Endpoints;

internal static class JobListEndpoint {
    internal static RouteGroupBuilder MapJobListEndpoint(this RouteGroupBuilder group) {
        group.MapGet("/", (
            bool? hideNsfw,
            HttpContext httpContext,
            JobService jobs,
            CancellationToken cancellationToken) =>
            jobs.ListAsync(NsfwVisibility.ShouldHide(hideNsfw, httpContext), cancellationToken))
            .WithName("ListJobs")
            .WithSummary("Lists Prismedia background job runs for the operations dashboard.");

        return group;
    }
}
