using Prismedia.Application.Integrations;
using Prismedia.Contracts.Integrations;
using Prismedia.Contracts.System;

namespace Prismedia.Api.Endpoints;

/// <summary>Wanted-work fulfillment through external managers, inheriting the connections administrator boundary.</summary>
public static class ManagedRequestEndpoints {
    /// <summary>Maps review, durable acceptance, observation, and cancellation without exposing direct mutation gateway calls.</summary>
    public static void MapManagedRequestEndpoints(this RouteGroupBuilder connections) {
        var group = connections.MapGroup("/{id:guid}/manager/requests");
        group.MapPost("/preview", async (Guid id, PreviewManagedRequestInput request, ManagedRequestService service, CancellationToken token) =>
            Results.Ok(await service.PreviewAsync(id, request, token)))
            .WithName("PreviewManagedRequest").Produces<ManagedRequestPreview>().Produces<ApiProblem>(400);
        group.MapGet("/", async (Guid id, ManagedRequestService service, CancellationToken token) =>
            Results.Ok(await service.ListAsync(id, token)))
            .WithName("ListManagedRequests").Produces<IReadOnlyList<ManagedRequestResponse>>();
        group.MapPost("/", async (Guid id, CreateManagedRequestInput request, ManagedRequestService service, CancellationToken token) =>
            Results.Accepted(value: await service.CreateAsync(id, request, token)))
            .WithName("CreateManagedRequest").Produces<ManagedRequestResponse>(202).Produces<ApiProblem>(400).Produces<ApiProblem>(409);
        group.MapPost("/{requestId:guid}/refresh", async (Guid id, Guid requestId, ManagedRequestService service, CancellationToken token) => {
            await service.RefreshAsync(id, requestId, token); return Results.Accepted();
        }).WithName("RefreshManagedRequest").Produces(202).Produces<ApiProblem>(400);
        group.MapPost("/{requestId:guid}/cancel", async (Guid id, Guid requestId, CancelManagedRequestInput request, ManagedRequestService service, CancellationToken token) =>
            Results.Ok(await service.CancelAsync(id, requestId, request.ExpectedRevision, token)))
            .WithName("CancelManagedRequest").Produces<ManagedRequestResponse>().Produces<ApiProblem>(409);
    }
}
