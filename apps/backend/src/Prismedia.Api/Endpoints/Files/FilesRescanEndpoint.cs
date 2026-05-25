using Prismedia.Application.Files;
using Prismedia.Contracts.Files;

namespace Prismedia.Api.Endpoints;

internal static class FilesRescanEndpoint {
    internal static RouteGroupBuilder MapFilesRescanEndpoint(this RouteGroupBuilder group) {
        group.MapPost("/rescan", async (
            FileRescanRequest request,
            bool? hideNsfw,
            HttpContext httpContext,
            FilesService files,
            CancellationToken cancellationToken) =>
            FilesEndpoints.ToResult(await FilesEndpoints.RunAsync(() => files.RescanAsync(
                request,
                NsfwVisibility.ShouldHide(hideNsfw, httpContext),
                cancellationToken))))
            .WithName("RescanFileRoot")
            .WithSummary("Queues scan jobs for a watched root.")
            .Produces<FileOperationResponse>();

        return group;
    }
}
