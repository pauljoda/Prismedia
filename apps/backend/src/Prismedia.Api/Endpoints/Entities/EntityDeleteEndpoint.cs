using Prismedia.Api.Security;
using Prismedia.Application.Entities;
using Prismedia.Contracts.System;

namespace Prismedia.Api.Endpoints;

/// <summary>
/// DELETE /api/entities/{id} and POST /api/entities/bulk-delete — permanently removes file-backed media
/// entities (a movie, a series with its seasons and episodes, an album, …) from the library together with
/// their source files and folders on disk. <c>deleteFiles=true</c> is required; library-only removal is
/// refused because a scan would rediscover those files. Monitored content instead remains as
/// wanted placeholders and immediately starts a clean reacquisition; unmonitored content tears down its
/// monitors/acquisitions entirely. Destructive and irreversible; admin only. Taxonomy kinds (tags, people,
/// studios) keep their own detach-only delete route.
/// </summary>
internal static class EntityDeleteEndpoint {
    internal static RouteGroupBuilder MapEntityDeleteEndpoint(this RouteGroupBuilder group) {
        group.MapDelete("/{id:guid}", async (
                Guid id,
                bool? deleteFiles,
                IMediaEntityDeletionService deletion,
                CancellationToken cancellationToken) => {
            var result = await deletion.DeleteAsync(id, deleteFiles ?? false, cancellationToken);
            return result.Deleted
                ? Results.Ok(new EntityDeleteResponse(1, result.FilesDeleted, [], result.Reverted ? 1 : 0))
                : ToFailureResult(result);
        })
            .RequireAdmin()
            .WithName("DeleteEntity")
            .WithSummary("Permanently deletes a media entity, its descendants, and their files on disk (deleteFiles=true is required).")
            .Produces<EntityDeleteResponse>()
            .Produces<ApiProblem>(StatusCodes.Status404NotFound)
            .Produces<ApiProblem>(StatusCodes.Status409Conflict)
            .Produces<ApiProblem>(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/bulk-delete", async (
                EntityBulkDeleteRequest request,
                IMediaEntityDeletionService deletion,
                CancellationToken cancellationToken) => {
            if (!request.DeleteFiles) {
                return Results.UnprocessableEntity(new ApiProblem(
                    ApiProblemCodes.EntityNotDeletable,
                    "Library-only Entity removal is unsupported because the next library scan would rediscover files that remain on disk. Delete files to remove these Entities, or use Remove wanted / Unmonitor instead."));
            }

            var outcome = await deletion.DeleteManyAsync(request.Ids, request.DeleteFiles, cancellationToken);
            return Results.Ok(new EntityDeleteResponse(
                outcome.Deleted,
                outcome.FilesDeleted,
                outcome.Failures
                    .Select(failure => new EntityDeleteFailure(failure.EntityId, failure.Message))
                    .ToArray(),
                outcome.Reverted));
        })
            .RequireAdmin()
            .WithName("BulkDeleteEntities")
            .WithSummary("Permanently deletes the given media entities, their descendants, and their files on disk (DeleteFiles must be true).")
            .Produces<EntityDeleteResponse>()
            .Produces<ApiProblem>(StatusCodes.Status422UnprocessableEntity);

        return group;
    }

    private static IResult ToFailureResult(MediaEntityDeleteResult result) => result.FailureKind switch {
        MediaEntityDeleteFailureKind.NotFound => Results.NotFound(new ApiProblem(
            ApiProblemCodes.EntityNotFound,
            result.Message ?? "The entity no longer exists.")),
        MediaEntityDeleteFailureKind.Conflict => Results.Conflict(new ApiProblem(
            ApiProblemCodes.EntityDeletionConflict,
            result.Message ?? "The entity cannot be deleted safely in its current state.")),
        _ => Results.UnprocessableEntity(new ApiProblem(
            ApiProblemCodes.EntityNotDeletable,
            result.Message ?? "The entity cannot be deleted through managed file deletion."))
    };
}

/// <summary>Bulk delete request: the entities to remove together with their on-disk files.</summary>
/// <param name="Ids">Entity identifiers to delete (each with its full descendant tree).</param>
/// <param name="DeleteFiles">Must be true; permanently deletes their source files/folders from disk.</param>
public sealed record EntityBulkDeleteRequest(IReadOnlyList<Guid> Ids, bool DeleteFiles);

/// <summary>One entity that could not be deleted, and why.</summary>
public sealed record EntityDeleteFailure(Guid Id, string Message);

/// <summary>
/// Outcome of a delete: how many entities were processed, how many on-disk paths went with them, any
/// failures, and how many of the processed entities were REVERTED to wanted placeholders (they were
/// under active monitoring, so their files were deleted but they stay in the library to be re-acquired)
/// rather than removed outright.
/// </summary>
public sealed record EntityDeleteResponse(int Deleted, int FilesDeleted, IReadOnlyList<EntityDeleteFailure> Failures, int Reverted = 0);
