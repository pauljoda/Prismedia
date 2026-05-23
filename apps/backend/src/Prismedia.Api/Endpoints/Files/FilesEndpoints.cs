using Prismedia.Application.Files;
using Prismedia.Contracts.Files;
using Prismedia.Contracts.System;

namespace Prismedia.Api.Endpoints;

public static class FilesEndpoints {
    public static RouteGroupBuilder MapFilesEndpoints(this IEndpointRouteBuilder routes) {
        var group = routes.MapGroup("/api/files")
            .WithTags("Files");

        group.MapGet("/roots", (
            bool? hideNsfw,
            HttpContext httpContext,
            FilesService files,
            CancellationToken cancellationToken) =>
            files.ListRootsAsync(NsfwVisibility.ShouldHide(hideNsfw, httpContext), cancellationToken))
            .WithName("ListFileRoots")
            .WithSummary("Lists watched roots for the Files page.")
            .Produces<FileRootsResponse>();

        group.MapGet("/children", async (
            Guid rootId,
            string? path,
            bool? hideNsfw,
            HttpContext httpContext,
            FilesService files,
            CancellationToken cancellationToken) =>
            ToResult(await RunAsync(
                () => files.ListChildrenAsync(
                    new FileChildrenRequest(rootId, path),
                    NsfwVisibility.ShouldHide(hideNsfw, httpContext),
                    cancellationToken))))
            .WithName("ListFileChildren")
            .WithSummary("Lists direct children under one watched-root directory.")
            .Produces<FileChildrenResponse>();

        group.MapGet("/detail", async (
            Guid rootId,
            string? path,
            bool? hideNsfw,
            HttpContext httpContext,
            FilesService files,
            CancellationToken cancellationToken) =>
            ToResult(await RunAsync(
                () => files.GetDetailAsync(
                    new FileDetailRequest(rootId, path),
                    NsfwVisibility.ShouldHide(hideNsfw, httpContext),
                    cancellationToken))))
            .WithName("GetFileDetail")
            .WithSummary("Gets file or directory details for the Files page.")
            .Produces<FileDetail>();

        group.MapGet("/content", StreamContent)
            .WithName("GetFileContent")
            .WithSummary("Streams a watched-root file with range support.");

        group.MapMethods("/content", ["HEAD"], StreamContent)
            .WithName("HeadFileContent")
            .WithSummary("Probes a watched-root file with range metadata.");

        group.MapPost("/folders", async (
            FileCreateFolderRequest request,
            bool? hideNsfw,
            HttpContext httpContext,
            FilesService files,
            CancellationToken cancellationToken) =>
            ToResult(await RunAsync(() => files.CreateFolderAsync(
                request,
                NsfwVisibility.ShouldHide(hideNsfw, httpContext),
                cancellationToken))))
            .WithName("CreateFileFolder")
            .WithSummary("Creates a folder under a watched root.")
            .Produces<FileOperationResponse>();

        group.MapPost("/upload", async (
            HttpRequest request,
            FilesService files,
            CancellationToken cancellationToken) => {
            if (!request.HasFormContentType) {
                return Results.BadRequest(new ApiProblem("invalid_upload", "Files upload expects multipart form data."));
            }

            var form = await request.ReadFormAsync(cancellationToken);
            if (!Guid.TryParse(form["rootId"], out var rootId)) {
                return Results.BadRequest(new ApiProblem("invalid_upload", "Files upload requires a rootId."));
            }

            var targetPath = form["targetPath"].FirstOrDefault();
            var relativePaths = form["relativePaths"].ToArray();
            var uploadItems = form.Files.Select((file, index) =>
                new FileUploadItem(
                    index < relativePaths.Length && !string.IsNullOrWhiteSpace(relativePaths[index])
                        ? relativePaths[index]!
                        : file.FileName,
                    file.OpenReadStream())).ToArray();

            var result = await RunAsync(() =>
                files.UploadAsync(
                    new FileUploadRequest(rootId, targetPath, uploadItems),
                    NsfwVisibility.ShouldHide(null, request.HttpContext),
                    cancellationToken));
            foreach (var item in uploadItems) {
                await item.Content.DisposeAsync();
            }

            return ToResult(result);
        })
            .WithName("UploadFiles")
            .WithSummary("Uploads files into a watched-root folder.")
            .DisableAntiforgery()
            .Produces<FileOperationResponse>();

        group.MapPatch("/rename", async (
            FileRenameRequest request,
            bool? hideNsfw,
            HttpContext httpContext,
            FilesService files,
            CancellationToken cancellationToken) =>
            ToResult(await RunAsync(() => files.RenameAsync(
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
            ToResult(await RunAsync(() => files.MoveAsync(
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
            ToResult(await RunAsync(() => files.DeleteAsync(
                new FileDeleteRequest(rootId, path),
                NsfwVisibility.ShouldHide(hideNsfw, httpContext),
                cancellationToken))))
            .WithName("DeleteFile")
            .WithSummary("Permanently deletes a watched-root file or folder.")
            .Produces<FileOperationResponse>();

        group.MapPost("/rescan", async (
            FileRescanRequest request,
            bool? hideNsfw,
            HttpContext httpContext,
            FilesService files,
            CancellationToken cancellationToken) =>
            ToResult(await RunAsync(() => files.RescanAsync(
                request,
                NsfwVisibility.ShouldHide(hideNsfw, httpContext),
                cancellationToken))))
            .WithName("RescanFileRoot")
            .WithSummary("Queues scan jobs for a watched root.")
            .Produces<FileOperationResponse>();

        return group;
    }

    private static async Task<ResultOrError<T>> RunAsync<T>(Func<Task<T>> action) {
        try {
            return new ResultOrError<T>(await action(), null);
        } catch (FileOperationException ex) {
            return new ResultOrError<T>(default, ex);
        }
    }

    private static IResult ToResult<T>(ResultOrError<T> result) =>
        result.Error is null ? Results.Ok(result.Value) : ToProblem(result.Error);

    private static async Task<IResult> StreamContent(
        Guid rootId,
        string? path,
        bool? hideNsfw,
        HttpContext httpContext,
        FilesService files,
        CancellationToken cancellationToken) {
        var result = await RunAsync(
            () => files.GetContentInfoAsync(
                new FileDetailRequest(rootId, path),
                NsfwVisibility.ShouldHide(hideNsfw, httpContext),
                cancellationToken));
        if (result.Error is { } error) {
            return ToProblem(error);
        }

        var content = result.Value!;
        return Results.File(
            File.OpenRead(content.AbsolutePath),
            content.MimeType,
            enableRangeProcessing: true,
            lastModified: content.LastModified);
    }

    private static IResult ToProblem(FileOperationException error) {
        var problem = new ApiProblem(error.Code, error.Message);
        return error switch {
            FileConflictException => Results.Conflict(problem),
            _ when error.Code == "root_not_found" || error.Code == "not_found" => Results.NotFound(problem),
            _ => Results.BadRequest(problem),
        };
    }

    private readonly record struct ResultOrError<T>(T? Value, FileOperationException? Error);
}
