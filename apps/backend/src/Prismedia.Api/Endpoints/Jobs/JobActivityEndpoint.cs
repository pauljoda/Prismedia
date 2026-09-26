using Prismedia.Application.Jobs;
using Prismedia.Contracts.Jobs;

namespace Prismedia.Api.Endpoints;

internal static class JobActivityEndpoint {
    #region Static Variables

    private const int DefaultActivityHours = 24;

    #endregion

    #region Actions - Routes

    internal static RouteGroupBuilder MapJobActivityEndpoint(this RouteGroupBuilder group) {
        group.MapGet("/activity", (
            int? hours,
            bool? hideNsfw,
            HttpContext httpContext,
            IJobActivityReader activity,
            CancellationToken cancellationToken) =>
            activity.ListActivityAsync(hours ?? DefaultActivityHours, NsfwVisibility.ShouldHide(hideNsfw, httpContext), cancellationToken))
            .WithName("ListJobActivity")
            .WithSummary("Buckets recent background work per job type and hour for the operations dashboard.")
            .Produces<JobActivityResponse>();

        return group;
    }

    #endregion
}
