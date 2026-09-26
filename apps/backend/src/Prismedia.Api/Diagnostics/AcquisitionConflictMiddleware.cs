using Prismedia.Application.Acquisition;
using Prismedia.Contracts.System;

namespace Prismedia.Api.Diagnostics;

/// <summary>Exposes retained acquisition lifecycle conflicts as actionable HTTP problems.</summary>
public sealed class AcquisitionConflictMiddleware(RequestDelegate next) {
    #region Actions - Pipeline

    /// <summary>Handles a refused lifecycle transition without discarding its retained recovery evidence.</summary>
    public async Task InvokeAsync(HttpContext context) {
        try {
            await next(context);
        } catch (AcquisitionConfigurationException error)
            when (!context.Response.HasStarted && error.Code == ApiProblemCodes.AcquisitionInvalid) {
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            await context.Response.WriteAsJsonAsync(new ApiProblem(error.Code, error.Message), context.RequestAborted);
        }
    }

    #endregion
}
