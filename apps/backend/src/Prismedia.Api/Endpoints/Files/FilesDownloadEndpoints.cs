using Prismedia.Api.Security;
using Prismedia.Application.Files;
using Prismedia.Contracts.Files;
using Prismedia.Contracts.Media;
using Prismedia.Contracts.System;

namespace Prismedia.Api.Endpoints;

/// <summary>Download and folder-archive endpoints for the watched-root Files surface.</summary>
public static class FilesDownloadEndpoints
{
    /// <summary>Maps direct file downloads and asynchronous directory archive preparation.</summary>
    public static RouteGroupBuilder MapFilesDownloadEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/download", StreamDownloadAsync)
            .WithName("DownloadFile")
            .WithSummary("Downloads one watched-root file as an attachment.");

        group.MapPost("/archives", PrepareArchiveAsync)
            .WithName("PrepareFileArchive")
            .WithSummary("Starts preparing a watched-root folder as a ZIP archive.")
            .Produces<FileArchivePreparation>(StatusCodes.Status202Accepted);

        group.MapGet("/archives/{id:guid}", GetArchiveStatus)
            .WithName("GetFileArchiveStatus")
            .WithSummary("Gets folder archive preparation progress.")
            .Produces<FileArchivePreparation>();

        group.MapGet("/archives/{id:guid}/content", DownloadArchive)
            .WithName("DownloadFileArchive")
            .WithSummary("Downloads a prepared folder ZIP archive.");

        return group;
    }

    private static async Task<IResult> StreamDownloadAsync(
        Guid rootId,
        string? path,
        bool? hideNsfw,
        HttpContext httpContext,
        FilesService files,
        CancellationToken cancellationToken)
    {
        var result = await FilesEndpoints.RunAsync(
            () => files.GetContentInfoAsync(
                new FileDetailRequest(rootId, path),
                NsfwVisibility.ShouldHide(hideNsfw, httpContext),
                cancellationToken));
        if (result.Error is { } error)
        {
            return FilesEndpoints.ToResult(result);
        }

        var content = result.Value!;
        return Results.File(
            File.OpenRead(content.AbsolutePath),
            content.MimeType,
            fileDownloadName: Path.GetFileName(content.AbsolutePath),
            enableRangeProcessing: true,
            lastModified: content.LastModified);
    }

    private static async Task<IResult> PrepareArchiveAsync(
        FileArchiveRequest request,
        bool? hideNsfw,
        HttpContext httpContext,
        FilesService files,
        CancellationToken cancellationToken)
    {
        var result = await FilesEndpoints.RunAsync(
            () => files.PrepareArchiveAsync(
                request,
                NsfwVisibility.ShouldHide(hideNsfw, httpContext),
                cancellationToken));
        return result.Error is null
            ? Results.Accepted($"/api/files/archives/{result.Value!.Id}", result.Value)
            : FilesEndpoints.ToResult(result);
    }

    private static IResult GetArchiveStatus(Guid id, IFileArchivePreparationService archives) =>
        archives.Get(id) is { } preparation
            ? Results.Ok(preparation)
            : Results.NotFound(new ApiProblem(ApiProblemCodes.NotFound, "Archive preparation was not found or has expired."));

    private static IResult DownloadArchive(
        Guid id,
        HttpContext httpContext,
        IFileArchivePreparationService archives)
    {
        if (archives.Get(id) is not { } preparation)
        {
            return Results.NotFound(new ApiProblem(ApiProblemCodes.NotFound, "Archive preparation was not found or has expired."));
        }

        if (preparation.Error is not null)
        {
            return Results.BadRequest(new ApiProblem(ApiProblemCodes.InvalidPath, preparation.Error));
        }

        if (!preparation.Ready || archives.Claim(id) is not { } archive)
        {
            return Results.Conflict(new ApiProblem(ApiProblemCodes.FileConflict, "Archive preparation is still in progress."));
        }

        httpContext.Response.OnCompleted(() =>
        {
            archives.Release(archive);
            return Task.CompletedTask;
        });
        return Results.File(
            archive.AbsolutePath,
            MediaContentTypes.Zip,
            fileDownloadName: archive.FileName,
            enableRangeProcessing: false);
    }
}
