using Prismedia.Domain.Entities;

using Prismedia.Api.Security;

namespace Prismedia.Api.Endpoints;

public static class JobEndpoints {
    public static RouteGroupBuilder MapJobEndpoints(this IEndpointRouteBuilder routes) {
        var group = routes.MapGroup("/api/jobs")
            .RequireAdmin()
            .WithTags("Jobs");

        group.MapJobListEndpoint();
        group.MapJobCreateEndpoint();
        group.MapJobCancelEndpoints();
        group.MapJobFailureEndpoints();
        group.MapJobMaintenanceEndpoints();

        return group;
    }
}

/// <summary>
/// Route-bound job type value that decodes public job codes at the HTTP edge.
/// </summary>
/// <param name="Value">Typed job operation resolved from the route segment.</param>
public readonly record struct JobTypeRoute(JobType Value) {
    /// <summary>
    /// Attempts to parse a route segment into a known typed job operation.
    /// </summary>
    /// <param name="value">Route segment supplied by the API caller.</param>
    /// <param name="provider">Format provider supplied by the minimal API binder.</param>
    /// <param name="result">Parsed route value when the segment is known.</param>
    /// <returns>True when the route segment maps to a registered job type.</returns>
    public static bool TryParse(string? value, IFormatProvider? provider, out JobTypeRoute result) {
        if (value is not null && value.TryDecodeAs<JobType>(out var type)) {
            result = new JobTypeRoute(type);
            return true;
        }

        result = default;
        return false;
    }
}
