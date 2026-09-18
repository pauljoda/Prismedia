using Prismedia.Application.Integrations;
using Prismedia.Application.Requests;
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
        group.MapPost("/review", async (Guid id, ReviewManagedRequestInput request, ReviewedManagedRequestService service, CancellationToken token) => {
            try { return Results.Ok(await service.ReviewAsync(id, request, token)); }
            catch (RequestProposalChangedException error) {
                return Results.Conflict(new ApiProblem(ApiProblemCodes.RequestProposalChanged, error.Message));
            }
            catch (RequestCommitValidationException error) {
                return Results.BadRequest(new ApiProblem(ApiProblemCodes.RequestInvalid, error.Message));
            }
        }).WithName("ReviewManagedRequest").Produces<ReviewedManagedRequest>().Produces<ApiProblem>(400).Produces<ApiProblem>(409);
        group.MapPost("/commit-reviewed", async (Guid id, CommitReviewedManagedRequestInput request,
            ReviewedManagedRequestService service, CancellationToken token) => {
            try { return Results.Accepted(value: await service.CommitAsync(id, request, token)); }
            catch (RequestProposalChangedException error) {
                return Results.Conflict(new ApiProblem(ApiProblemCodes.RequestProposalChanged, error.Message));
            }
            catch (RequestCommitValidationException error) {
                return Results.BadRequest(new ApiProblem(ApiProblemCodes.RequestInvalid, error.Message));
            }
            catch (FulfillmentOwnershipConflictException error) {
                return Results.Conflict(new ApiProblem(ApiProblemCodes.FulfillmentOwnershipConflict, error.Message));
            }
        }).WithName("CommitReviewedManagedRequest").Produces<ReviewedManagedRequestCommitResponse>(202)
            .Produces<ApiProblem>(400).Produces<ApiProblem>(409);
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
