using Prismedia.Application.Collections;
using Prismedia.Contracts.Collections;
using Prismedia.Contracts.Entities;

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

        return group;
    }
}
