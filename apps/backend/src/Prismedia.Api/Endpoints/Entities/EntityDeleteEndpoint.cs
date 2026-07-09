using Prismedia.Api.Security;
using Prismedia.Application.Entities;
using Prismedia.Contracts.System;

namespace Prismedia.Api.Endpoints;

/// <summary>
/// DELETE /api/entities/{id} and POST /api/entities/bulk-delete — permanently removes file-backed media
/// entities (a movie, a series with its seasons and episodes, an album, …) from the library and — when
/// <c>deleteFiles</c> is set — their source files and folders on disk. Monitored content instead remains as
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
                : Results.NotFound(new ApiProblem(ApiProblemCodes.EntityNotFound, result.Message ?? "The entity could not be deleted."));
        })
            .RequireAdmin()
            .WithName("DeleteEntity")
            .WithSummary("Permanently deletes a media entity (and its descendants), optionally including its files on disk.")
            .Produces<EntityDeleteResponse>()
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        group.MapPost("/bulk-delete", async (
                EntityBulkDeleteRequest request,
                IMediaEntityDeletionService deletion,
                CancellationToken cancellationToken) => {
            var deleted = 0;
            var filesDeleted = 0;
            var reverted = 0;
            var failures = new List<EntityDeleteFailure>();
            foreach (var id in request.Ids.Distinct()) {
                var result = await deletion.DeleteAsync(id, request.DeleteFiles, cancellationToken);
                if (result.Deleted) {
                    deleted++;
                    filesDeleted += result.FilesDeleted;
                    if (result.Reverted) {
                        reverted++;
                    }
                } else {
                    failures.Add(new EntityDeleteFailure(id, result.Message ?? "The entity could not be deleted."));
                }
            }

            return Results.Ok(new EntityDeleteResponse(deleted, filesDeleted, failures, reverted));
        })
            .RequireAdmin()
            .WithName("BulkDeleteEntities")
            .WithSummary("Permanently deletes the given media entities (and their descendants), optionally including their files on disk.")
            .Produces<EntityDeleteResponse>();

        return group;
    }
}

/// <summary>Bulk delete request: the entities to remove and whether their on-disk files go with them.</summary>
/// <param name="Ids">Entity identifiers to delete (each with its full descendant tree).</param>
/// <param name="DeleteFiles">True to also permanently delete their source files/folders from disk.</param>
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
