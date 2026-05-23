using Prismedia.Application.Entities;
using Prismedia.Contracts.Entities;

namespace Prismedia.Api.Endpoints;

internal static class EntityThumbnailEndpoint {
    internal static RouteGroupBuilder MapEntityThumbnailEndpoint(this RouteGroupBuilder group) {
        group.MapPost("/thumbnails", async (
            EntityThumbnailBatchRequest request,
            bool? hideNsfw,
            HttpContext httpContext,
            IEntityReadService entities,
            CancellationToken cancellationToken) =>
            Results.Ok(await entities.GetThumbnailsAsync(
                request.Ids,
                NsfwVisibility.ShouldHide(hideNsfw, httpContext),
                cancellationToken)))
            .WithName("GetEntityThumbnails")
            .Produces<EntityThumbnailBatchResponse>();

        return group;
    }
}
