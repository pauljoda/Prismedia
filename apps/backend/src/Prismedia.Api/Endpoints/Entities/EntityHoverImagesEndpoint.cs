using Prismedia.Application.Entities;
using Prismedia.Contracts.Entities;

namespace Prismedia.Api.Endpoints;

internal static class EntityHoverImagesEndpoint {
    internal static RouteGroupBuilder MapEntityHoverImagesEndpoint(this RouteGroupBuilder group) {
        group.MapPost("/hover-images", async (
            EntityThumbnailBatchRequest request,
            bool? hideNsfw,
            HttpContext httpContext,
            IEntityReadService entities,
            CancellationToken cancellationToken) =>
            Results.Ok(await entities.GetHoverImagesAsync(
                request.Ids,
                NsfwVisibility.ShouldHide(hideNsfw, httpContext),
                cancellationToken)))
            .WithName("GetEntityHoverImages")
            .WithSummary("Get sampled child-artwork hover previews for entities.")
            .Produces<EntityHoverImagesResponse>();

        return group;
    }
}
