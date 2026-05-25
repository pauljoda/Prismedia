using Prismedia.Application.Files;
using Prismedia.Contracts.Files;
using Prismedia.Contracts.System;

namespace Prismedia.Api.Endpoints;

internal static class FilesUploadEndpoint {
    internal static RouteGroupBuilder MapFilesUploadEndpoint(this RouteGroupBuilder group) {
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

            var result = await FilesEndpoints.RunAsync(() =>
                files.UploadAsync(
                    new FileUploadRequest(rootId, targetPath, uploadItems),
                    NsfwVisibility.ShouldHide(null, request.HttpContext),
                    cancellationToken));
            foreach (var item in uploadItems) {
                await item.Content.DisposeAsync();
            }

            return FilesEndpoints.ToResult(result);
        })
            .WithName("UploadFiles")
            .WithSummary("Uploads files into a watched-root folder.")
            .DisableAntiforgery()
            .Produces<FileOperationResponse>();

        return group;
    }
}
