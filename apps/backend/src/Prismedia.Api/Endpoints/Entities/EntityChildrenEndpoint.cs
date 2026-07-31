using Prismedia.Application.Entities;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.System;

namespace Prismedia.Api.Endpoints;

/// <summary>Batch projection for direct children of shared Entity roots.</summary>
internal static class EntityChildrenEndpoint {
    internal static RouteGroupBuilder MapEntityChildrenEndpoint(this RouteGroupBuilder group) {
        group.MapPost("/children", async (
            EntityChildrenBatchRequest request,
            bool? hideNsfw,
            HttpContext httpContext,
            IEntityReadService entities,
            CancellationToken cancellationToken) => {
                if (request.ParentIds is null || request.ParentIds.Count > EntityChildrenBatchRequest.MaximumParentIds) {
                    return Results.BadRequest(new ApiProblem(
                        ApiProblemCodes.RequestInvalid,
                        $"At most {EntityChildrenBatchRequest.MaximumParentIds} parent Entity identifiers may be requested."));
                }

                return Results.Ok(await entities.GetChildrenAsync(
                    request.ParentIds,
                    NsfwVisibility.ShouldHide(hideNsfw, httpContext),
                    cancellationToken));
            })
            .WithName("GetEntityChildren")
            .WithSummary("Get direct child Entity thumbnails for multiple parents.")
            .Produces<EntityChildrenBatchResponse>()
            .Produces<ApiProblem>(StatusCodes.Status400BadRequest);

        return group;
    }
}
