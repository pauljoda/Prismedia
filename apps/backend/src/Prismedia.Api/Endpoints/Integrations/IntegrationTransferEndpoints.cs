using Prismedia.Api.Security;
using Prismedia.Application.Integrations;
using Prismedia.Contracts.Integrations;
using Prismedia.Contracts.System;

namespace Prismedia.Api.Endpoints;

/// <summary>Administrative status and recovery for durable plugin transfers.</summary>
public static class IntegrationTransferEndpoints {
    /// <summary>Maps read and explicit retry operations without exposing retrieval credentials or filesystem paths.</summary>
    public static RouteGroupBuilder MapIntegrationTransferEndpoints(this IEndpointRouteBuilder routes) {
        var group = routes.MapGroup("/api/integration-transfers").RequireAdmin().WithTags("Integration transfers");
        group.AddEndpointFilter<IntegrationTransferProblemFilter>();
        group.MapGet("/", async (CatalogAcquisitionService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListAsync(cancellationToken)))
            .WithName("ListIntegrationTransfers").Produces<IReadOnlyList<IntegrationTransferResponse>>();
        group.MapGet("/{id:guid}", async (Guid id, CatalogAcquisitionService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.GetAsync(id, cancellationToken)))
            .WithName("GetIntegrationTransfer").Produces<IntegrationTransferResponse>().Produces<ApiProblem>(404);
        group.MapPost("/{id:guid}/retry", async (Guid id, CatalogAcquisitionService service, CancellationToken cancellationToken) => {
            await service.RetryAsync(id, cancellationToken);
            return Results.Accepted($"/api/integration-transfers/{id}");
        }).WithName("RetryIntegrationTransfer").Produces(202).Produces<ApiProblem>(400).Produces<ApiProblem>(404);
        group.MapPost("/{id:guid}/cancel", async (Guid id, CatalogAcquisitionService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.CancelAsync(id, cancellationToken)))
            .WithName("CancelIntegrationTransfer").Produces<IntegrationTransferResponse>().Produces<ApiProblem>(400).Produces<ApiProblem>(404).Produces<ApiProblem>(409);
        return group;
    }
}

/// <summary>Maps typed transfer failures into safe, stable HTTP problems.</summary>
public sealed class IntegrationTransferProblemFilter : IEndpointFilter {
    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next) {
        try { return await next(context); }
        catch (IntegrationTransferNotFoundException error) { return Results.NotFound(new ApiProblem(ApiProblemCodes.IntegrationTransferNotFound, error.Message)); }
        catch (IntegrationTransferConflictException error) { return Results.Conflict(new ApiProblem(ApiProblemCodes.IntegrationTransferConflict, error.Message)); }
        catch (IntegrationTransferPlanUnavailableException error) { return Results.Conflict(new ApiProblem(ApiProblemCodes.IntegrationTransferUnavailable, error.Message)); }
        catch (ArgumentException error) { return Results.BadRequest(new ApiProblem(ApiProblemCodes.IntegrationTransferInvalid, error.Message)); }
    }
}
