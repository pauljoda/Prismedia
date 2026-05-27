using Prismedia.Application.Collections;
using Prismedia.Contracts.Collections;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.System;

namespace Prismedia.Api.Endpoints;

public static class CollectionEndpoints {
    public static RouteGroupBuilder MapCollectionEndpoints(this IEndpointRouteBuilder routes) {
        var group = routes.MapEntityKindRoutes(
            "/api/collections",
            "collection",
            "Collections",
            "ListCollections",
            "GetCollection",
            typeof(EntityListResponse),
            typeof(CollectionDetail));

        group.MapPost("/", async (
            CollectionWriteRequest request,
            ICollectionCommandService collections,
            CancellationToken cancellationToken) => {
                var result = await collections.CreateAsync(request, cancellationToken);
                return result.Status switch {
                    CollectionCommandStatus.Succeeded when result.Collection is not null =>
                        Results.Created($"/api/collections/{result.Collection.Id}", result.Collection),
                    CollectionCommandStatus.Invalid =>
                        Results.BadRequest(new ApiProblem("invalid_collection", result.Message ?? "Collection request is invalid.")),
                    _ => Results.NotFound(new ApiProblem("collection_not_found", result.Message ?? "Collection was not found."))
                };
            })
            .WithName("CreateCollection")
            .Produces<CollectionDetail>(StatusCodes.Status201Created)
            .Produces<ApiProblem>(StatusCodes.Status400BadRequest);

        group.MapPut("/{id:guid}", async (
            Guid id,
            CollectionWriteRequest request,
            ICollectionCommandService collections,
            CancellationToken cancellationToken) =>
                ToCollectionWriteResult(await collections.UpdateAsync(id, request, cancellationToken)))
            .WithName("UpdateCollection")
            .Produces<CollectionDetail>(StatusCodes.Status200OK)
            .Produces<ApiProblem>(StatusCodes.Status400BadRequest)
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        group.MapDelete("/{id:guid}", async (
            Guid id,
            ICollectionCommandService collections,
            CancellationToken cancellationToken) => {
                var result = await collections.DeleteAsync(id, cancellationToken);
                return result.Status == CollectionCommandStatus.Succeeded
                    ? Results.Ok(new CollectionDeleteResponse(id))
                    : Results.NotFound(new ApiProblem("collection_not_found", result.Message ?? "Collection was not found."));
            })
            .WithName("DeleteCollection")
            .Produces<CollectionDeleteResponse>(StatusCodes.Status200OK)
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        group.MapPost("/preview-rules", async (
            CollectionRulePreviewRequest request,
            bool? hideNsfw,
            HttpContext httpContext,
            ICollectionCommandService collections,
            CancellationToken cancellationToken) => {
                var response = await collections.PreviewRulesAsync(
                    request,
                    NsfwVisibility.ShouldHide(hideNsfw, httpContext),
                    cancellationToken);
                return response is null
                    ? Results.BadRequest(new ApiProblem("invalid_collection_rules", "Collection rule tree is invalid."))
                    : Results.Ok(response);
            })
            .WithName("PreviewCollectionRules")
            .Produces<CollectionRulePreviewResponse>(StatusCodes.Status200OK)
            .Produces<ApiProblem>(StatusCodes.Status400BadRequest);

        group.MapGet("/{id:guid}/items", async (
            Guid id,
            bool? hideNsfw,
            HttpContext httpContext,
            ICollectionItemReadService collections,
            CancellationToken cancellationToken) => {
                var response = await collections.ListItemsAsync(
                    id,
                    NsfwVisibility.ShouldHide(hideNsfw, httpContext),
                    cancellationToken);
                return Results.Ok(response);
            })
            .WithName("ListCollectionItems")
            .Produces<CollectionItemsResponse>(StatusCodes.Status200OK);

        group.MapPost("/{id:guid}/items", async (
            Guid id,
            CollectionAddItemsRequest request,
            ICollectionCommandService collections,
            CancellationToken cancellationToken) =>
                ToCountResult(await collections.AddItemsAsync(id, request, cancellationToken)))
            .WithName("AddCollectionItems")
            .Produces<CollectionItemMutationResponse>(StatusCodes.Status200OK)
            .Produces<ApiProblem>(StatusCodes.Status400BadRequest)
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/items/remove", async (
            Guid id,
            CollectionRemoveItemsRequest request,
            ICollectionCommandService collections,
            CancellationToken cancellationToken) =>
                ToCountResult(await collections.RemoveItemsAsync(id, request, cancellationToken)))
            .WithName("RemoveCollectionItems")
            .Produces<CollectionItemMutationResponse>(StatusCodes.Status200OK)
            .Produces<ApiProblem>(StatusCodes.Status400BadRequest)
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        group.MapPatch("/{id:guid}/items/reorder", async (
            Guid id,
            CollectionReorderItemsRequest request,
            ICollectionCommandService collections,
            CancellationToken cancellationToken) =>
                ToCountResult(await collections.ReorderItemsAsync(id, request, cancellationToken)))
            .WithName("ReorderCollectionItems")
            .Produces<CollectionItemMutationResponse>(StatusCodes.Status200OK)
            .Produces<ApiProblem>(StatusCodes.Status400BadRequest)
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/refresh", async (
            Guid id,
            ICollectionCommandService collections,
            CancellationToken cancellationToken) => {
                var response = await collections.RefreshAsync(id, cancellationToken);
                return response is null
                    ? Results.NotFound(new ApiProblem("collection_not_found", "Collection was not found."))
                    : Results.Ok(response);
            })
            .WithName("RefreshCollection")
            .Produces<CollectionRefreshResponse>(StatusCodes.Status200OK)
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        return group;
    }

    private static IResult ToCollectionWriteResult(CollectionWriteResult result) =>
        result.Status switch {
            CollectionCommandStatus.Succeeded when result.Collection is not null => Results.Ok(result.Collection),
            CollectionCommandStatus.Invalid => Results.BadRequest(new ApiProblem(
                "invalid_collection",
                result.Message ?? "Collection request is invalid.")),
            _ => Results.NotFound(new ApiProblem(
                "collection_not_found",
                result.Message ?? "Collection was not found."))
        };

    private static IResult ToCountResult(CollectionCountResult result) =>
        result.Status switch {
            CollectionCommandStatus.Succeeded => Results.Ok(new CollectionItemMutationResponse(result.Count)),
            CollectionCommandStatus.Invalid => Results.BadRequest(new ApiProblem(
                "invalid_collection_items",
                result.Message ?? "Collection item request is invalid.")),
            _ => Results.NotFound(new ApiProblem(
                "collection_not_found",
                result.Message ?? "Collection was not found."))
        };
}
