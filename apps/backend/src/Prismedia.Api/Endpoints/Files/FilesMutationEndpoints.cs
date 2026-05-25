using Prismedia.Application.Files;
using Prismedia.Contracts.Files;
using Prismedia.Contracts.System;

namespace Prismedia.Api.Endpoints;

internal static class FilesMutationEndpoints {
    internal static RouteGroupBuilder MapFilesMutationEndpoints(this RouteGroupBuilder group) {
        group.MapPost("/folders", async (
            FileCreateFolderRequest request,
            bool? hideNsfw,
            HttpContext httpContext,
            FilesService files,
            CancellationToken cancellationToken) =>
            FilesEndpoints.ToResult(await FilesEndpoints.RunAsync(() => files.CreateFolderAsync(
                request,
                NsfwVisibility.ShouldHide(hideNsfw, httpContext),
                cancellationToken))))
            .WithName("CreateFileFolder")
            .WithSummary("Creates a folder under a watched root.")
            .Produces<FileOperationResponse>();

        group.MapPatch("/rename", async (
            FileRenameRequest request,
            bool? hideNsfw,
            HttpContext httpContext,
            FilesService files,
            CancellationToken cancellationToken) =>
            FilesEndpoints.ToResult(await FilesEndpoints.RunAsync(() => files.RenameAsync(
                request,
                NsfwVisibility.ShouldHide(hideNsfw, httpContext),
                cancellationToken))))
            .WithName("RenameFile")
            .WithSummary("Renames a watched-root file or folder.")
            .Produces<FileOperationResponse>();

        group.MapPost("/move", async (
            FileMoveRequest request,
            bool? hideNsfw,
            HttpContext httpContext,
            FilesService files,
            CancellationToken cancellationToken) =>
            FilesEndpoints.ToResult(await FilesEndpoints.RunAsync(() => files.MoveAsync(
                request,
                NsfwVisibility.ShouldHide(hideNsfw, httpContext),
                cancellationToken))))
            .WithName("MoveFile")
            .WithSummary("Moves a watched-root file or folder.")
            .Produces<FileOperationResponse>();

        group.MapDelete("", async (
            Guid rootId,
            string path,
            bool? hideNsfw,
            HttpContext httpContext,
            FilesService files,
            CancellationToken cancellationToken) =>
            FilesEndpoints.ToResult(await FilesEndpoints.RunAsync(() => files.DeleteAsync(
                new FileDeleteRequest(rootId, path),
                NsfwVisibility.ShouldHide(hideNsfw, httpContext),
                cancellationToken))))
            .WithName("DeleteFile")
            .WithSummary("Permanently deletes a watched-root file or folder.")
            .Produces<FileOperationResponse>();

        return group;
    }
}
