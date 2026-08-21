using Prismedia.Application.Entities;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.System;

namespace Prismedia.Api.Endpoints;

/// <summary>Entity-agnostic ordered image-page reader routes.</summary>
internal static class EntityReaderEndpoints {
    internal static RouteGroupBuilder MapEntityReaderEndpoints(this RouteGroupBuilder group) {
        group.MapGet("/{id:guid}/reader-manifest", GetManifestAsync)
            .WithName("GetEntityReaderManifest")
            .WithSummary("Gets an Entity's complete ordered image-page manifest.")
            .Produces<EntityReaderManifestResponse>()
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        group.MapGet("/{id:guid}/reader-pages/{ordinal:int:min(0)}", StreamPageAsync)
            .WithName("GetEntityReaderPage")
            .WithSummary("Streams one page from an Entity's ordered reader manifest.")
            .Produces(StatusCodes.Status200OK)
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        return group;
    }

    private static async Task<IResult> GetManifestAsync(
        Guid id,
        IEntityReaderService reader,
        CancellationToken cancellationToken) {
        var manifest = await reader.GetManifestAsync(id, cancellationToken);
        return manifest is null
            ? Results.NotFound(new ApiProblem(
                ApiProblemCodes.EntityReaderManifestNotFound,
                $"Reader manifest for Entity '{id}' was not found."))
            : Results.Ok(manifest);
    }

    private static async Task<IResult> StreamPageAsync(
        Guid id,
        int ordinal,
        IEntityReaderService reader,
        CancellationToken cancellationToken) {
        var page = await reader.GetPageAsync(id, ordinal, cancellationToken);
        if (page is null) {
            return Results.NotFound(new ApiProblem(
                ApiProblemCodes.EntityReaderPageNotFound,
                $"Reader page '{ordinal}' for Entity '{id}' was not found."));
        }

        return await EntityFileResults.StreamAsync(
            page.Path,
            page.MimeType,
            () => Results.NotFound(new ApiProblem(
                ApiProblemCodes.EntityReaderPageNotFound,
                $"Reader page '{ordinal}' for Entity '{id}' was not found.")),
            cancellationToken);
    }
}
