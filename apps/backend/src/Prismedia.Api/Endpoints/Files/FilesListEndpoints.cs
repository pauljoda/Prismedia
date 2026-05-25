using Prismedia.Application.Files;
using Prismedia.Contracts.Files;

namespace Prismedia.Api.Endpoints;

internal static class FilesListEndpoints {
    internal static RouteGroupBuilder MapFilesListEndpoints(this RouteGroupBuilder group) {
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
            FilesEndpoints.ToResult(await FilesEndpoints.RunAsync(
                () => files.ListChildrenAsync(
                    new FileChildrenRequest(rootId, path),
                    NsfwVisibility.ShouldHide(hideNsfw, httpContext),
                    cancellationToken))))
            .WithName("ListFileChildren")
            .WithSummary("Lists direct children under one watched-root directory.")
            .Produces<FileChildrenResponse>();

        return group;
    }
}
