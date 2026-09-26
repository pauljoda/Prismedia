using Prismedia.Application.Plugins;
using Prismedia.Contracts.System;

namespace Prismedia.Api.Diagnostics;

/// <summary>Preserves metadata provider failures as upstream errors instead of missing library identities.</summary>
public sealed class PluginProviderFailureMiddleware(RequestDelegate next) {
    #region Actions - Pipeline

    /// <summary>Maps the explicit provider exception; unrelated server failures retain their normal handling.</summary>
    public async Task InvokeAsync(HttpContext context) {
        try {
            await next(context);
        } catch (PluginProviderUnavailableException error) when (!context.Response.HasStarted) {
            context.Response.StatusCode = StatusCodes.Status502BadGateway;
            await context.Response.WriteAsJsonAsync(new ApiProblem(ApiProblemCodes.PluginProviderUnavailable, error.Message),
                context.RequestAborted);
        } catch (PluginInUseException error) when (!context.Response.HasStarted) {
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            await context.Response.WriteAsJsonAsync(new ApiProblem(ApiProblemCodes.PluginInUse, error.Message), context.RequestAborted);
        }
    }

    #endregion
}
