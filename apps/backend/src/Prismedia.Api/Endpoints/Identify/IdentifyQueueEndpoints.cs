using Prismedia.Contracts.Plugins;
using Prismedia.Contracts.System;
using Prismedia.Infrastructure.Plugins;

namespace Prismedia.Api.Endpoints;

internal static class IdentifyQueueEndpoints {
    internal static RouteGroupBuilder MapIdentifyQueueEndpoints(this RouteGroupBuilder group) {
        group.MapGet("/queue", async (
            bool? includeCompleted,
            IdentifyQueueService queue,
            CancellationToken cancellationToken) =>
            Results.Ok(await queue.ListAsync(includeCompleted ?? false, cancellationToken)))
            .WithName("ListIdentifyQueue")
            .WithSummary("Lists durable identify queue items.")
            .Produces<IReadOnlyList<IdentifyQueueItem>>();

        group.MapPost("/queue/entities/{entityId:guid}", async (
            Guid entityId,
            IdentifyQueueService queue,
            CancellationToken cancellationToken) => {
                try {
                    return Results.Ok(await queue.AddAsync(entityId, cancellationToken));
                } catch (KeyNotFoundException ex) {
                    return Results.NotFound(new ApiProblem("entity_not_found", ex.Message));
                }
            })
            .WithName("AddIdentifyQueueItem")
            .WithSummary("Adds an entity to the durable identify queue.")
            .Produces<IdentifyQueueItem>()
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        group.MapGet("/queue/entities/{entityId:guid}", async (
            Guid entityId,
            IdentifyQueueService queue,
            CancellationToken cancellationToken) => {
                var item = await queue.GetAsync(entityId, cancellationToken);
                return item is null
                    ? Results.NotFound(new ApiProblem("identify_queue_item_not_found", $"Entity '{entityId}' is not in the identify queue."))
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
            IdentifyQueueService queue,
            CancellationToken cancellationToken) => {
                try {
                    return Results.Ok(await queue.SearchAsync(
                        entityId,
                        request,
                        NsfwVisibility.ShouldHide(hideNsfw, httpContext),
                        cancellationToken));
                } catch (KeyNotFoundException ex) {
                    return Results.NotFound(new ApiProblem("entity_not_found", ex.Message));
                }
            })
            .WithName("SearchIdentifyQueueItem")
            .WithSummary("Runs a provider search and stores candidates or a proposal on the queue item.")
            .Produces<IdentifyQueueItem>()
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        group.MapPost("/queue/entities/{entityId:guid}/apply", async (
            Guid entityId,
            ApplyIdentifyQueueItemRequest request,
            IdentifyQueueService queue,
            CancellationToken cancellationToken) => {
                try {
                    return Results.Ok(await queue.ApplyAsync(entityId, request, cancellationToken));
                } catch (InvalidOperationException ex) {
                    return Results.BadRequest(new ApiProblem("identify_queue_apply_invalid", ex.Message));
                } catch (ArgumentException ex) {
                    return Results.BadRequest(new ApiProblem("identify_queue_apply_invalid", ex.Message));
                } catch (KeyNotFoundException ex) {
                    return Results.NotFound(new ApiProblem("entity_not_found", ex.Message));
                }
            })
            .WithName("ApplyIdentifyQueueItem")
            .WithSummary("Applies a reviewed identify queue proposal and marks the item done.")
            .Produces<IdentifyQueueItem>()
            .Produces<ApiProblem>(StatusCodes.Status400BadRequest)
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        group.MapDelete("/queue/entities/{entityId:guid}", async (
            Guid entityId,
            IdentifyQueueService queue,
            CancellationToken cancellationToken) => {
                try {
                    var item = await queue.DeleteAsync(entityId, cancellationToken);
                    return item is null
                        ? Results.NotFound(new ApiProblem("identify_queue_item_not_found", $"Entity '{entityId}' is not in the identify queue."))
                        : Results.Ok(item);
                } catch (KeyNotFoundException ex) {
                    return Results.NotFound(new ApiProblem("entity_not_found", ex.Message));
                }
            })
            .WithName("DeleteIdentifyQueueItem")
            .WithSummary("Removes an entity from the active identify queue.")
            .Produces<IdentifyQueueItem>()
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        return group;
    }
}
