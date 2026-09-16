using Prismedia.Contracts.System;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Api.Diagnostics;

/// <summary>Returns the same actionable conflict for tracked writes and bulk persistence paths.</summary>
public sealed class FulfillmentOwnershipMiddleware(RequestDelegate next) {
    /// <summary>Maps only the named ownership invariant; unrelated database errors retain their normal handling.</summary>
    public async Task InvokeAsync(HttpContext context) {
        try { await next(context); }
        catch (Exception error) when (!context.Response.HasStarted && FulfillmentOwnershipViolation.IsConflict(error)) {
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            await context.Response.WriteAsJsonAsync(new ApiProblem(ApiProblemCodes.FulfillmentOwnershipConflict,
                "This work and scope already has an acquisition owner. Use its connection or resolve the existing ownership first."), context.RequestAborted);
        }
    }
}
