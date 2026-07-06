using Prismedia.Api.Security;
using Prismedia.Application.Security;
using Prismedia.Application.Settings;
using Prismedia.Contracts.Settings;
using Prismedia.Contracts.System;
using Prismedia.Domain.Entities;

namespace Prismedia.Api.Endpoints;

/// <summary>
/// Watched library-root management. Admins manage every root and its per-user access;
/// members with the create-libraries permission manage roots they created (creator-only
/// visibility by default) and read only summaries of roots they were granted.
/// </summary>
public static class LibraryEndpoints {
    public static RouteGroupBuilder MapLibraryEndpoints(this IEndpointRouteBuilder routes) {
        var group = routes.MapGroup("/api/libraries")
            .WithTags("Settings");

        group.MapGet("", async (
            SettingsService settings,
            ILibraryAccessStore access,
            CancellationToken cancellationToken) => {
            var roots = await settings.ListLibraryRootsAsync(cancellationToken);
            var accessByRoot = await access.GetAccessByRootAsync(cancellationToken);
            return Results.Ok(roots
                .Select(root => root with {
                    AccessUserIds = accessByRoot.GetValueOrDefault(root.Id) ?? []
                })
                .ToArray());
        })
            .RequireAdmin()
            .WithName("ListLibraryRoots")
            .WithSummary("Lists watched media roots with per-user access (admin).")
            .Produces<LibraryRoot[]>();

        group.MapGet("/accessible", async (
            HttpContext httpContext,
            SettingsService settings,
            ICurrentUserContext currentUser,
            CancellationToken cancellationToken) => {
            var roots = await settings.ListLibraryRootsAsync(cancellationToken);
            var allowed = await currentUser.GetAllowedLibraryRootIdsAsync(cancellationToken);
            return Results.Ok(roots
                .Where(root => root.Enabled && (allowed is null || allowed.Contains(root.Id)))
                .Select(root => new LibraryRootSummary(
                    root.Id,
                    root.Label,
                    root.ScanVideos,
                    root.ScanImages,
                    root.ScanAudio,
                    root.ScanBooks,
                    root.IsNsfw))
                .ToArray());
        })
            .WithName("ListAccessibleLibraryRoots")
            .WithSummary("Lists the library roots the signed-in user can access.")
            .Produces<LibraryRootSummary[]>();

        group.MapGet("/browse", async (
            string? path,
            HttpContext httpContext,
            SettingsService settings,
            CancellationToken cancellationToken) =>
            CanCreateLibraries(httpContext)
                ? Results.Ok(await settings.BrowseLibraryPathAsync(path, cancellationToken))
                : LibraryManagementForbidden())
            .WithName("BrowseLibraryPath")
            .WithSummary("Browses local directories for watched-root selection.")
            .Produces<LibraryBrowseResponse>()
            .Produces<ApiProblem>(StatusCodes.Status403Forbidden);

        group.MapPost("", async (
            LibraryRootCreateRequest request,
            HttpContext httpContext,
            SettingsService settings,
            ILibraryAccessStore access,
            CancellationToken cancellationToken) => {
            var user = httpContext.GetCurrentUser()!;
            if (!CanCreateLibraries(httpContext)) {
                return LibraryManagementForbidden();
            }

            var created = await settings.CreateLibraryRootAsync(request, cancellationToken, user.Id);

            // Admins see every library implicitly; explicit grants only exist for members.
            // A member-created root is always creator-only; admins may grant an initial set.
            var grants = user.Role == UserRole.Admin
                ? request.GrantUserIds ?? []
                : [user.Id];
            if (grants.Count > 0) {
                await access.GrantRootAccessAsync(created.Id, grants.ToArray(), cancellationToken);
            }

            return Results.Ok(created with { AccessUserIds = grants });
        })
            .WithName("CreateLibraryRoot")
            .WithSummary("Adds a watched media root.")
            .Produces<LibraryRoot>()
            .Produces<ApiProblem>(StatusCodes.Status403Forbidden);

        group.MapPatch("/{id:guid}", async (
            Guid id,
            LibraryRootUpdateRequest request,
            HttpContext httpContext,
            SettingsService settings,
            CancellationToken cancellationToken) => {
                if (!await CanManageRootAsync(httpContext, settings, id, cancellationToken)) {
                    return LibraryManagementForbidden();
                }

                var root = await settings.UpdateLibraryRootAsync(id, request, cancellationToken);
                return root is null ? Results.NotFound() : Results.Ok(root);
            })
            .WithName("UpdateLibraryRoot")
            .WithSummary("Updates a watched media root.")
            .Produces<LibraryRoot>()
            .Produces<ApiProblem>(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        group.MapDelete("/{id:guid}", async (
            Guid id,
            HttpContext httpContext,
            SettingsService settings,
            CancellationToken cancellationToken) => {
                if (!await CanManageRootAsync(httpContext, settings, id, cancellationToken)) {
                    return LibraryManagementForbidden();
                }

                var deleted = await settings.DeleteLibraryRootAsync(id, cancellationToken);
                return deleted ? Results.Ok(new { ok = true }) : Results.NotFound();
            })
            .WithName("DeleteLibraryRoot")
            .WithSummary("Deletes a watched media root.")
            .Produces<ApiProblem>(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPut("/{id:guid}/access", async (
            Guid id,
            LibraryAccessUpdateRequest request,
            SettingsService settings,
            ILibraryAccessStore access,
            CancellationToken cancellationToken) => {
                if (await settings.GetLibraryRootAsync(id, cancellationToken) is null) {
                    return Results.NotFound(new ApiProblem(ApiProblemCodes.RootNotFound, $"Library root '{id}' was not found."));
                }

                await access.ReplaceRootAccessAsync(id, request.UserIds, cancellationToken);
                return Results.NoContent();
            })
            .RequireAdmin()
            .WithName("ReplaceLibraryAccess")
            .WithSummary("Replaces the member users granted access to a library root (admin).")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        return group;
    }

    private static bool CanCreateLibraries(HttpContext httpContext) =>
        httpContext.GetCurrentUser() is { } user &&
        (user.Role == UserRole.Admin || user.CanCreateLibraries);

    /// <summary>Admins manage every root; members with the permission manage roots they created.</summary>
    private static async Task<bool> CanManageRootAsync(
        HttpContext httpContext,
        SettingsService settings,
        Guid rootId,
        CancellationToken cancellationToken) {
        var user = httpContext.GetCurrentUser();
        if (user is null) {
            return false;
        }

        if (user.Role == UserRole.Admin) {
            return true;
        }

        if (!user.CanCreateLibraries) {
            return false;
        }

        var root = await settings.GetLibraryRootAsync(rootId, cancellationToken);
        return root?.CreatedByUserId == user.Id;
    }

    private static IResult LibraryManagementForbidden() =>
        Results.Json(
            new ApiProblem(ApiProblemCodes.AdminRequired, "You do not have permission to manage this library."),
            statusCode: StatusCodes.Status403Forbidden);
}
