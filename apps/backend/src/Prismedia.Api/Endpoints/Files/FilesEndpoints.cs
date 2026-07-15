using Prismedia.Application.Files;
using Prismedia.Contracts.Files;
using Prismedia.Contracts.System;

using Prismedia.Api.Security;

namespace Prismedia.Api.Endpoints;

public static class FilesEndpoints {
    public static RouteGroupBuilder MapFilesEndpoints(this IEndpointRouteBuilder routes) {
        var group = routes.MapGroup("/api/files")
            .RequireAdmin()
            .WithTags("Files");

        group.MapFilesListEndpoints();
        group.MapFilesMutationEndpoints();
        group.MapFilesUploadEndpoint();
        group.MapFilesRescanEndpoint();
        group.MapFilesDownloadEndpoints();

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

        return group;
    }

    internal static async Task<ResultOrError<T>> RunAsync<T>(Func<Task<T>> action) {
        try {
            return new ResultOrError<T>(await action(), null);
        } catch (FileOperationException ex) {
            return new ResultOrError<T>(default, ex);
        }
    }

    internal static IResult ToResult<T>(ResultOrError<T> result) =>
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

    internal readonly record struct ResultOrError<T>(T? Value, FileOperationException? Error);
}
