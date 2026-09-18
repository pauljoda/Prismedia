using Prismedia.Application.Integrations;
using Prismedia.Application.Requests;
using Prismedia.Contracts.Integrations;
using Prismedia.Contracts.Requests;
using Prismedia.Contracts.System;

namespace Prismedia.Api.Endpoints;

/// <summary>Read-only external-manager discovery followed by explicit local wanted preparation.</summary>
public static class ManagedDiscoveryEndpoints {
    /// <summary>Maps connection-scoped catalog search, exact review, and revision-fenced preparation.</summary>
    public static void MapManagedDiscoveryEndpoints(this RouteGroupBuilder connections) {
        var group = connections.MapGroup("/{id:guid}/manager/discovery");
        group.MapPost("/search", async (Guid id, ManagedDiscoveryQuery request, ManagedDiscoveryService service, CancellationToken token) =>
            Results.Ok(await service.SearchAsync(id, request, token)))
            .WithName("SearchManagedDiscovery").Produces<ManagedDiscoverySearchResponse>().Produces<ApiProblem>(400);
        group.MapPost("/review", async (Guid id, ManagedDiscoveryReviewRequest request, ManagedDiscoveryService service, CancellationToken token) =>
            Results.Ok(await service.ReviewAsync(id, request, token)))
            .WithName("ReviewManagedDiscovery").Produces<ManagedDiscoveryReviewResponse>().Produces<ApiProblem>(400);
        group.MapPost("/prepare", async (Guid id, PrepareManagedDiscoveryRequest request, ManagedDiscoveryService service, CancellationToken token) => {
            try { return Results.Ok(await service.PrepareAsync(id, request, token)); }
            catch (RequestProposalChangedException error) { return Results.Conflict(new ApiProblem(ApiProblemCodes.RequestProposalChanged, error.Message)); }
            catch (RequestCommitValidationException error) { return Results.BadRequest(new ApiProblem(ApiProblemCodes.RequestInvalid, error.Message)); }
        }).WithName("PrepareManagedDiscovery").Produces<PreparedWantedMovieResponse>().Produces<ApiProblem>(400).Produces<ApiProblem>(409);
    }
}
