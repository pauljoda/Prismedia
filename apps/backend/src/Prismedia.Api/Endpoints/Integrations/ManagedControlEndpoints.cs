using Prismedia.Application.Integrations;
using Prismedia.Contracts.Integrations;
using Prismedia.Contracts.System;

namespace Prismedia.Api.Endpoints;

/// <summary>Typed controls for finite, already linked holdings; inherits the connections administrator boundary.</summary>
public static class ManagedControlEndpoints {
    /// <summary>Maps reviewed settings, search intent, and retained command observations.</summary>
    public static void MapManagedControlEndpoints(this RouteGroupBuilder connections) {
        var group = connections.MapGroup("/{id:guid}/library/tracking/{holdingId:guid}/controls");
        group.MapGet("/preview", async (Guid id, Guid holdingId, ManagedControlService service, CancellationToken token) =>
            Results.Ok(await service.PreviewAsync(id, holdingId, token)))
            .WithName("PreviewManagedControls").Produces<ManagedControlPreview>().Produces<ApiProblem>(400).Produces<ApiProblem>(409);
        group.MapGet("/", async (Guid id, Guid holdingId, ManagedControlService service, CancellationToken token) =>
            Results.Ok(await service.ListAsync(id, holdingId, token)))
            .WithName("ListManagedControls").Produces<IReadOnlyList<ManagedControlActionResponse>>();
        group.MapPost("/", async (Guid id, Guid holdingId, CreateManagedControlRequest request, ManagedControlService service, CancellationToken token) =>
            Results.Accepted(value: await service.CreateAsync(id, holdingId, request, token)))
            .WithName("CreateManagedControl").Produces<ManagedControlActionResponse>(202).Produces<ApiProblem>(400).Produces<ApiProblem>(409);
        group.MapPost("/{actionId:guid}/refresh", async (Guid id, Guid holdingId, Guid actionId, ManagedControlService service, CancellationToken token) => {
            await service.RefreshAsync(id, holdingId, actionId, token); return Results.Accepted();
        }).WithName("RefreshManagedControl").Produces(202).Produces<ApiProblem>(400);
        group.MapPost("/{actionId:guid}/cancel", async (Guid id, Guid holdingId, Guid actionId, ManagedControlRevisionRequest request, ManagedControlService service, CancellationToken token) =>
            Results.Ok(await service.CancelAsync(id, holdingId, actionId, request.ExpectedRevision, token)))
            .WithName("CancelManagedControl").Produces<ManagedControlActionResponse>().Produces<ApiProblem>(409);
        group.MapPost("/{actionId:guid}/close-unverified", async (Guid id, Guid holdingId, Guid actionId, ManagedControlRevisionRequest request, ManagedControlService service, CancellationToken token) =>
            Results.Ok(await service.CloseUnverifiedAsync(id, holdingId, actionId, request.ExpectedRevision, token)))
            .WithName("CloseUnverifiedManagedControl").Produces<ManagedControlActionResponse>().Produces<ApiProblem>(409);
    }
}
