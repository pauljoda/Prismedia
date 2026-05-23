using Prismedia.Application.Settings;
using Prismedia.Contracts.Settings;

namespace Prismedia.Api.Endpoints;

public static class LibraryEndpoints {
    public static RouteGroupBuilder MapLibraryEndpoints(this IEndpointRouteBuilder routes) {
        var group = routes.MapGroup("/api/libraries")
            .WithTags("Settings");

        group.MapGet("", (
            SettingsService settings,
            CancellationToken cancellationToken) =>
            settings.ListLibraryRootsAsync(cancellationToken))
            .WithName("ListLibraryRoots")
            .WithSummary("Lists watched media roots.");

        group.MapGet("/browse", (
            string? path,
            SettingsService settings,
            CancellationToken cancellationToken) =>
            settings.BrowseLibraryPathAsync(path, cancellationToken))
            .WithName("BrowseLibraryPath")
            .WithSummary("Browses local directories for watched-root selection.");

        group.MapPost("", (
            LibraryRootCreateRequest request,
            SettingsService settings,
            CancellationToken cancellationToken) =>
            settings.CreateLibraryRootAsync(request, cancellationToken))
            .WithName("CreateLibraryRoot")
            .WithSummary("Adds a watched media root.");

        group.MapPatch("/{id:guid}", async (
            Guid id,
            LibraryRootUpdateRequest request,
            SettingsService settings,
            CancellationToken cancellationToken) => {
                var root = await settings.UpdateLibraryRootAsync(id, request, cancellationToken);
                return root is null ? Results.NotFound() : Results.Ok(root);
            })
            .WithName("UpdateLibraryRoot")
            .WithSummary("Updates a watched media root.");

        group.MapDelete("/{id:guid}", async (
            Guid id,
            SettingsService settings,
            CancellationToken cancellationToken) => {
                var deleted = await settings.DeleteLibraryRootAsync(id, cancellationToken);
                return deleted ? Results.Ok(new { ok = true }) : Results.NotFound();
            })
            .WithName("DeleteLibraryRoot")
            .WithSummary("Deletes a watched media root.");

        return group;
    }
}
