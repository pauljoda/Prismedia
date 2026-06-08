using Prismedia.Application.Entities;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.System;

namespace Prismedia.Api.Endpoints;

internal static class EntityImageAssetEndpoint {
    internal static RouteGroupBuilder MapEntityImageAssetEndpoint(this RouteGroupBuilder group) {
        group.MapPost("/{id:guid}/images/{role}", async (
            Guid id,
            string role,
            HttpRequest request,
            IEntityImageAssetMutationService assets,
            IEntityReadService entities,
            CancellationToken cancellationToken) => {
            if (!request.HasFormContentType) {
                return Results.BadRequest(new ApiProblem(ApiProblemCodes.InvalidEntityImageUpload, "Image upload expects multipart form data."));
            }

            var form = await request.ReadFormAsync(cancellationToken);
            var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
            if (file is null || file.Length == 0) {
                return Results.BadRequest(new ApiProblem(ApiProblemCodes.InvalidEntityImageUpload, "Image upload requires a non-empty file."));
            }

            await using var stream = file.OpenReadStream();
            var result = await assets.UploadAsync(id, role, file.FileName, file.ContentType, stream, cancellationToken);
            return await ToResultAsync(id, result, entities, cancellationToken);
        })
            .WithName("UploadEntityImageAsset")
            .WithSummary("Uploads user-managed artwork for an entity image role.")
            .DisableAntiforgery()
            .Produces<EntityCard>()
            .Produces<ApiProblem>(StatusCodes.Status400BadRequest)
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        group.MapDelete("/{id:guid}/images/{role}", async (
            Guid id,
            string role,
            IEntityImageAssetMutationService assets,
            IEntityReadService entities,
            CancellationToken cancellationToken) => {
            var result = await assets.ClearAsync(id, role, cancellationToken);
            return await ToResultAsync(id, result, entities, cancellationToken);
        })
            .WithName("ClearEntityImageAsset")
            .WithSummary("Clears user-managed artwork for an entity image role.")
            .Produces<EntityCard>()
            .Produces<ApiProblem>(StatusCodes.Status400BadRequest)
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        return group;
    }

    private static async Task<IResult> ToResultAsync(
        Guid id,
        EntityImageAssetMutationResult result,
        IEntityReadService entities,
        CancellationToken cancellationToken) =>
        result switch {
            EntityImageAssetMutationResult.Updated =>
                await EntityEndpointResults.GetEntityAsync(id, false, entities, cancellationToken),
            EntityImageAssetMutationResult.NotFound =>
                Results.NotFound(new ApiProblem(ApiProblemCodes.EntityNotFound, $"Entity '{id}' was not found.")),
            EntityImageAssetMutationResult.InvalidFile =>
                Results.BadRequest(new ApiProblem(ApiProblemCodes.InvalidEntityImageUpload, "Only image uploads are supported.")),
            EntityImageAssetMutationResult.UnsupportedRole =>
                Results.BadRequest(new ApiProblem(ApiProblemCodes.UnsupportedEntityImageRole, "That image role cannot be managed manually.")),
            _ =>
                Results.BadRequest(new ApiProblem(ApiProblemCodes.InvalidEntityImageUpload, "The image asset could not be updated."))
        };
}
