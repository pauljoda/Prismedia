using Prismedia.Contracts.System;
using Prismedia.Domain.Entities;

namespace Prismedia.Api.Security;

/// <summary>
/// Endpoint-level authorization: <c>RequireAdmin()</c> on a route group or handler
/// rejects non-admin users with 403 <c>admin_required</c>. Authentication itself is
/// enforced earlier by the middleware; this filter only checks the resolved role.
/// </summary>
internal static class AuthorizationEndpointFilters {
    /// <summary>Restricts the endpoint(s) to admin users.</summary>
    internal static TBuilder RequireAdmin<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilterFactory((_, next) => async context => {
            var user = context.HttpContext.GetCurrentUser();
            if (user is null) {
                return Results.Json(
                    new ApiProblem(ApiProblemCodes.AuthenticationRequired, "Authentication is required."),
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            if (user.Role != UserRole.Admin) {
                return Results.Json(
                    new ApiProblem(ApiProblemCodes.AdminRequired, "Administrator access is required."),
                    statusCode: StatusCodes.Status403Forbidden);
            }

            return await next(context);
        });
}
