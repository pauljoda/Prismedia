using Prismedia.Application.Integrations;
using Prismedia.Contracts.System;

namespace Prismedia.Api.Endpoints;

/// <summary>Maps typed transfer failures into safe, stable HTTP problems.</summary>
public sealed class IntegrationTransferProblemFilter : IEndpointFilter {
    #region Actions - Filtering

    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next) {
        try {
            return await next(context);
        } catch (IntegrationTransferNotFoundException error) {
            return Results.NotFound(new ApiProblem(ApiProblemCodes.IntegrationTransferNotFound, error.Message));
        } catch (IntegrationTransferConflictException error) {
            return Results.Conflict(new ApiProblem(ApiProblemCodes.IntegrationTransferConflict, error.Message));
        } catch (IntegrationTransferPlanUnavailableException error) {
            return Results.Conflict(new ApiProblem(ApiProblemCodes.IntegrationTransferUnavailable, error.Message));
        } catch (ArgumentException error) {
            return Results.BadRequest(new ApiProblem(ApiProblemCodes.IntegrationTransferInvalid, error.Message));
        }
    }

    #endregion
}
