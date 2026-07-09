using Prismedia.Application.Plugins;
using Prismedia.Contracts.Plugins;
using Prismedia.Contracts.System;

namespace Prismedia.Api.Endpoints;

internal static class IdentifyQueueEndpoints {
    internal static RouteGroupBuilder MapIdentifyQueueEndpoints(this RouteGroupBuilder group) {
        group.MapGet("/queue", async (
            bool? includeCompleted,
            bool? hideNsfw,
            HttpContext httpContext,
            IIdentifyQueueService queue,
            CancellationToken cancellationToken) =>
            Results.Ok(await queue.ListAsync(
                includeCompleted ?? false,
                NsfwVisibility.ShouldHide(hideNsfw, httpContext),
                cancellationToken)))
            .WithName("ListIdentifyQueue")
            .WithSummary("Lists durable identify queue items.")
            .Produces<IReadOnlyList<IdentifyQueueItem>>();

        group.MapPost("/queue/entities/{entityId:guid}", async (
            Guid entityId,
            IIdentifyQueueService queue,
            CancellationToken cancellationToken) => {
                try {
                    return Results.Ok(await queue.AddAsync(entityId, cancellationToken));
                } catch (IdentifyTargetNotEligibleException ex) {
                    return IdentifyTargetConflict(ex);
                } catch (KeyNotFoundException ex) {
                    return Results.NotFound(new ApiProblem(ApiProblemCodes.EntityNotFound, ex.Message));
                }
            })
            .WithName("AddIdentifyQueueItem")
            .WithSummary("Adds an entity to the durable identify queue.")
            .Produces<IdentifyQueueItem>()
            .Produces<ApiProblem>(StatusCodes.Status409Conflict)
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        group.MapGet("/queue/entities/{entityId:guid}", async (
            Guid entityId,
            IIdentifyQueueService queue,
            CancellationToken cancellationToken) => {
                var item = await queue.GetAsync(entityId, cancellationToken);
                return item is null
                    ? Results.NotFound(new ApiProblem(ApiProblemCodes.IdentifyQueueItemNotFound, $"Entity '{entityId}' is not in the identify queue."))
                    : Results.Ok(item);
            })
            .WithName("GetIdentifyQueueItem")
            .WithSummary("Gets one durable identify queue item by entity id.")
            .Produces<IdentifyQueueItem>()
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        group.MapPost("/queue/entities/{entityId:guid}/search", async (
            Guid entityId,
            IdentifyQueueSearchRequest request,
            bool? hideNsfw,
            HttpContext httpContext,
            IIdentifyQueueService queue,
            CancellationToken cancellationToken) => {
                try {
                    return Results.Ok(await queue.RequestSearchAsync(
                        entityId,
                        request,
                        NsfwVisibility.ShouldHide(hideNsfw, httpContext),
                        cancellationToken));
                } catch (IdentifyTargetNotEligibleException ex) {
                    return IdentifyTargetConflict(ex);
                } catch (KeyNotFoundException ex) {
                    return Results.NotFound(new ApiProblem(ApiProblemCodes.EntityNotFound, ex.Message));
                }
            })
            .WithName("SearchIdentifyQueueItem")
            .WithSummary("Requests a provider search; a background identify-search job runs it and the item reports progress through its state.")
            .Produces<IdentifyQueueItem>()
            .Produces<ApiProblem>(StatusCodes.Status409Conflict)
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        group.MapPost("/queue/entities/{entityId:guid}/candidate", async (
            Guid entityId,
            IdentifyQueueCandidateRequest request,
            bool? hideNsfw,
            HttpContext httpContext,
            IIdentifyQueueService queue,
            CancellationToken cancellationToken) => {
                try {
                    return Results.Ok(await queue.ResolveCandidateAsync(
                        entityId,
                        request,
                        NsfwVisibility.ShouldHide(hideNsfw, httpContext),
                        cancellationToken));
                } catch (IdentifyTargetNotEligibleException ex) {
                    return IdentifyTargetConflict(ex);
                } catch (ArgumentException ex) {
                    return Results.BadRequest(new ApiProblem(ApiProblemCodes.IdentifyFailed, ex.Message));
                } catch (InvalidOperationException ex) {
                    return Results.BadRequest(new ApiProblem(ApiProblemCodes.IdentifyFailed, ex.Message));
                } catch (KeyNotFoundException ex) {
                    return Results.NotFound(new ApiProblem(ApiProblemCodes.EntityNotFound, ex.Message));
                }
            })
            .WithName("ResolveIdentifyQueueCandidate")
            .WithSummary("Resolves one selected search candidate into the queue item's proposal without enqueueing a new search.")
            .Produces<IdentifyQueueItem>()
            .Produces<ApiProblem>(StatusCodes.Status400BadRequest)
            .Produces<ApiProblem>(StatusCodes.Status409Conflict)
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        group.MapPost("/queue/entities/{entityId:guid}/apply", async (
            Guid entityId,
            ApplyIdentifyQueueItemRequest request,
            IIdentifyQueueService queue,
            CancellationToken cancellationToken) => {
                try {
                    return Results.Ok(await queue.ApplyAsync(entityId, request, cancellationToken));
                } catch (IdentifyTargetNotEligibleException ex) {
                    return IdentifyTargetConflict(ex);
                } catch (InvalidOperationException ex) {
                    return Results.BadRequest(new ApiProblem(ApiProblemCodes.IdentifyQueueApplyInvalid, ex.Message));
                } catch (ArgumentException ex) {
                    return Results.BadRequest(new ApiProblem(ApiProblemCodes.IdentifyQueueApplyInvalid, ex.Message));
                } catch (KeyNotFoundException ex) {
                    return Results.NotFound(new ApiProblem(ApiProblemCodes.EntityNotFound, ex.Message));
                }
            })
            .WithName("ApplyIdentifyQueueItem")
            .WithSummary("Applies a reviewed identify queue proposal and marks the item done.")
            .Produces<IdentifyQueueItem>()
            .Produces<ApiProblem>(StatusCodes.Status400BadRequest)
            .Produces<ApiProblem>(StatusCodes.Status409Conflict)
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        group.MapPut("/queue/entities/{entityId:guid}/proposal", async (
            Guid entityId,
            SaveIdentifyQueueProposalRequest request,
            IIdentifyQueueService queue,
            CancellationToken cancellationToken) => {
                try {
                    return Results.Ok(await queue.SaveProposalAsync(entityId, request.Proposal, cancellationToken));
                } catch (IdentifyTargetNotEligibleException ex) {
                    return IdentifyTargetConflict(ex);
                } catch (InvalidOperationException ex) {
                    return Results.BadRequest(new ApiProblem(ApiProblemCodes.IdentifyQueueProposalInvalid, ex.Message));
                } catch (KeyNotFoundException ex) {
                    return Results.NotFound(new ApiProblem(ApiProblemCodes.EntityNotFound, ex.Message));
                }
            })
            .WithName("SaveIdentifyQueueProposal")
            .WithSummary("Persists an in-progress identify proposal (e.g. resolved children) without applying it.")
            .Produces<IdentifyQueueItem>()
            .Produces<ApiProblem>(StatusCodes.Status400BadRequest)
            .Produces<ApiProblem>(StatusCodes.Status409Conflict)
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        group.MapGet("/queue/entities/{entityId:guid}/apply-progress/{progressId:guid}", (
            Guid entityId,
            Guid progressId,
            IIdentifyApplyProgressStore progress) => {
                var snapshot = progress.Get(progressId);
                if (snapshot is null || snapshot.EntityId != entityId) {
                    return Results.NotFound(new ApiProblem(ApiProblemCodes.IdentifyApplyProgressNotFound, $"Apply progress '{progressId}' was not found."));
                }

                return Results.Ok(snapshot);
            })
            .WithName("GetIdentifyApplyProgress")
            .WithSummary("Gets live progress for an Identify proposal apply operation.")
            .Produces<IdentifyApplyProgress>()
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        group.MapDelete("/queue/entities/{entityId:guid}", async (
            Guid entityId,
            IIdentifyQueueService queue,
            CancellationToken cancellationToken) => {
                try {
                    var item = await queue.DeleteAsync(entityId, cancellationToken);
                    return item is null
                        ? Results.NotFound(new ApiProblem(ApiProblemCodes.IdentifyQueueItemNotFound, $"Entity '{entityId}' is not in the identify queue."))
                        : Results.Ok(item);
                } catch (KeyNotFoundException ex) {
                    return Results.NotFound(new ApiProblem(ApiProblemCodes.EntityNotFound, ex.Message));
                }
            })
            .WithName("DeleteIdentifyQueueItem")
            .WithSummary("Removes an entity from the active identify queue.")
            .Produces<IdentifyQueueItem>()
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        return group;
    }

    private static IResult IdentifyTargetConflict(IdentifyTargetNotEligibleException exception) =>
        Results.Conflict(new ApiProblem(ApiProblemCodes.IdentifyTargetNotEligible, exception.Message));
}
