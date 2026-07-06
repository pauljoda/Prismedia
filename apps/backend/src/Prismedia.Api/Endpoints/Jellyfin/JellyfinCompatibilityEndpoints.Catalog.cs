using Prismedia.Api.Security;
using Prismedia.Application.Jellyfin;
using Prismedia.Application.Security;
using Prismedia.Contracts.Jellyfin;
using Prismedia.Contracts.Security;
using Prismedia.Contracts.System;

namespace Prismedia.Api.Endpoints;

/// <summary>
/// Catalog/browse request handlers for the Jellyfin compatibility endpoints.
/// </summary>
public static partial class JellyfinCompatibilityEndpoints {
    private static async Task<IResult> GetUserViewsAsync(
        HttpContext httpContext,
        PrismediaSecurityService security,
        JellyfinCatalogService catalog,
        CancellationToken cancellationToken) {
        var state = await security.EnsureSecurityAsync(cancellationToken);
        var views = await catalog.GetUserViewsWithArtworkAsync(
            state.ServerId.ToString("N"),
            NsfwVisibility.JellyfinContent(httpContext),
            cancellationToken);
        return Results.Ok(views);
    }

    private static async Task<IResult> GetRootAsync(
        PrismediaSecurityService security,
        JellyfinCatalogService catalog,
        CancellationToken cancellationToken) {
        var state = await security.EnsureSecurityAsync(cancellationToken);
        return Results.Ok(catalog.GetRoot(state.ServerId.ToString("N")));
    }

    private static async Task<IResult> GetGroupingOptionsAsync(
        Guid? userId,
        HttpContext httpContext,
        PrismediaSecurityService security,
        JellyfinCatalogService catalog,
        CancellationToken cancellationToken) {
        var user = await ResolveUserAsync(httpContext, security, userId, cancellationToken);
        if (user is null) {
            return Results.NotFound(new ApiProblem(ApiProblemCodes.JellyfinUserNotFound, "No Jellyfin user was found."));
        }

        var options = catalog.GetUserViews(user.ServerId)
            .Items
            .Select(item => new JellyfinSpecialViewOptionDto(item.Name, item.Id.ToString("N")))
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return Results.Ok(options);
    }

    private static JellyfinVirtualFolderInfoDto ToVirtualFolder(JellyfinBaseItemDto item) =>
        new(
            item.Name,
            [],
            VirtualFolderCollectionType(item),
            new JellyfinLibraryOptionsDto(),
            item.Id.ToString("N"),
            item.Id.ToString("N"),
            RefreshProgress: null,
            RefreshStatus: null);

    private static async Task<IResult> GetItemsAsync(
        HttpContext httpContext,
        PrismediaSecurityService security,
        JellyfinCatalogService catalog,
        CancellationToken cancellationToken) {
        var state = await security.EnsureSecurityAsync(cancellationToken);
        var result = await catalog.GetItemsAsync(
            ItemQueryFrom(httpContext.Request),
            state.ServerId.ToString("N"),
            NsfwVisibility.JellyfinContent(httpContext),
            cancellationToken);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetPlaylistItemsAsync(
        string playlistId,
        HttpContext httpContext,
        PrismediaSecurityService security,
        JellyfinCatalogService catalog,
        CancellationToken cancellationToken) {
        if (TryGuid(playlistId) is not { } parsedPlaylistId) {
            return Results.NotFound(new ApiProblem(ApiProblemCodes.JellyfinItemNotFound, $"Playlist '{playlistId}' was not found."));
        }

        var state = await security.EnsureSecurityAsync(cancellationToken);
        var query = ItemQueryFrom(httpContext.Request) with {
            ParentId = parsedPlaylistId,
            // Real Jellyfin's /Playlists/{id}/Items endpoint does not accept IncludeItemTypes;
            // clients such as Manet send IncludeItemTypes=PlaylistItem, which is not the Type of the
            // returned audio rows. Ignore the query filter here and return the playlist's contents.
            IncludeItemTypes = []
        };
        var result = await catalog.GetItemsAsync(
            query,
            state.ServerId.ToString("N"),
            NsfwVisibility.JellyfinContent(httpContext),
            cancellationToken);
        var items = result.Items
            .Select(item => item with { PlaylistItemId = item.PlaylistItemId ?? item.Id.ToString("N") })
            .ToArray();
        return Results.Ok(new JellyfinQueryResult<JellyfinBaseItemDto>(items, result.TotalRecordCount, result.StartIndex));
    }

    private static async Task<IResult> GetItemAsync(
        Guid itemId,
        HttpContext httpContext,
        PrismediaSecurityService security,
        JellyfinCatalogService catalog,
        CancellationToken cancellationToken) {
        var state = await security.EnsureSecurityAsync(cancellationToken);
        var item = await catalog.GetItemAsync(
            itemId,
            state.ServerId.ToString("N"),
            NsfwVisibility.JellyfinContent(httpContext),
            cancellationToken);
        return item is null
            ? Results.NotFound(new ApiProblem(ApiProblemCodes.JellyfinItemNotFound, $"Item '{itemId}' was not found."))
            : Results.Ok(item);
    }

    private static async Task<IResult> GetLatestAsync(
        HttpContext httpContext,
        PrismediaSecurityService security,
        JellyfinCatalogService catalog,
        CancellationToken cancellationToken) {
        var state = await security.EnsureSecurityAsync(cancellationToken);
        var limit = TryInt(httpContext.Request.Query["Limit"].FirstOrDefault()) ?? 20;
        var parentId = TryGuid(httpContext.Request.Query["ParentId"].FirstOrDefault());
        var result = await catalog.GetLatestAsync(
            parentId,
            Math.Clamp(limit, 1, 100),
            state.ServerId.ToString("N"),
            NsfwVisibility.JellyfinContent(httpContext),
            cancellationToken);
        return Results.Ok(result.Items);
    }

    private static async Task<IResult> GetResumeAsync(
        HttpContext httpContext,
        PrismediaSecurityService security,
        JellyfinCatalogService catalog,
        CancellationToken cancellationToken) {
        var state = await security.EnsureSecurityAsync(cancellationToken);
        var start = TryInt(httpContext.Request.Query["StartIndex"].FirstOrDefault()) ?? 0;
        var limit = TryInt(httpContext.Request.Query["Limit"].FirstOrDefault()) ?? 20;
        var result = await catalog.GetResumeAsync(
            Math.Max(0, start),
            Math.Clamp(limit, 1, 100),
            state.ServerId.ToString("N"),
            NsfwVisibility.JellyfinContent(httpContext),
            cancellationToken);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetNextUpAsync(
        HttpContext httpContext,
        PrismediaSecurityService security,
        JellyfinCatalogService catalog,
        CancellationToken cancellationToken) {
        var state = await security.EnsureSecurityAsync(cancellationToken);
        var start = TryInt(httpContext.Request.Query["StartIndex"].FirstOrDefault()) ?? 0;
        var limit = TryInt(httpContext.Request.Query["Limit"].FirstOrDefault()) ?? 20;
        var result = await catalog.GetNextUpAsync(
            Math.Max(0, start),
            Math.Clamp(limit, 1, 100),
            state.ServerId.ToString("N"),
            NsfwVisibility.JellyfinContent(httpContext),
            cancellationToken);
        return Results.Ok(result);
    }

    // itemId is bound (though unused) so the {itemId} route token is described as a
    // path parameter; without it the generated OpenAPI document is invalid and the
    // frontend client generator (orval) rejects the spec.
    private static IResult EmptyItemListAsync(Guid itemId) =>
        Results.Ok(Array.Empty<JellyfinBaseItemDto>());

    private static IResult EmptyPagedItemListAsync(Guid itemId) =>
        Results.Ok(new JellyfinQueryResult<JellyfinBaseItemDto>([], 0, 0));

    private static IResult GetMediaSegmentsAsync(Guid itemId) =>
        Results.Ok(new JellyfinQueryResult<JellyfinMediaSegmentDto>([], 0, 0));

    /// <summary>
    /// Lists music artists for the <c>/Artists</c> and <c>/Artists/AlbumArtists</c> endpoints music
    /// clients use as their artist index. Prismedia models a single artist concept, so both routes
    /// resolve to the same <c>MusicArtist</c> listing under the Music view (search/sort/paging from
    /// the request still apply via the shared item query).
    /// </summary>
    private static async Task<IResult> GetArtistsAsync(
        HttpContext httpContext,
        PrismediaSecurityService security,
        JellyfinCatalogService catalog,
        CancellationToken cancellationToken) {
        var state = await security.EnsureSecurityAsync(cancellationToken);
        var baseQuery = ItemQueryFrom(httpContext.Request);
        var query = baseQuery with {
            ParentId = baseQuery.ParentId ?? JellyfinCatalogService.MusicViewId,
            IncludeItemTypes = [JellyfinProtocol.ItemTypes.MusicArtist]
        };
        var result = await catalog.GetItemsAsync(
            query,
            state.ServerId.ToString("N"),
            NsfwVisibility.JellyfinContent(httpContext),
            cancellationToken);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetSeriesSeasonsAsync(
        Guid seriesId,
        HttpContext httpContext,
        PrismediaSecurityService security,
        JellyfinCatalogService catalog,
        CancellationToken cancellationToken) {
        var state = await security.EnsureSecurityAsync(cancellationToken);
        var query = ItemQueryFrom(httpContext.Request) with {
            ParentId = seriesId,
            Recursive = false,
            IncludeItemTypes = [JellyfinProtocol.ItemTypes.Season]
        };
        var result = await catalog.GetItemsAsync(
            query,
            state.ServerId.ToString("N"),
            NsfwVisibility.JellyfinContent(httpContext),
            cancellationToken);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetSeriesEpisodesAsync(
        Guid seriesId,
        HttpContext httpContext,
        PrismediaSecurityService security,
        JellyfinCatalogService catalog,
        CancellationToken cancellationToken) {
        var state = await security.EnsureSecurityAsync(cancellationToken);
        var seasonId = TryGuid(httpContext.Request.Query["SeasonId"].FirstOrDefault());
        var query = ItemQueryFrom(httpContext.Request) with {
            ParentId = seasonId ?? seriesId,
            Recursive = seasonId is null,
            IncludeItemTypes = [JellyfinProtocol.ItemTypes.Episode]
        };
        var result = await catalog.GetItemsAsync(
            query,
            state.ServerId.ToString("N"),
            NsfwVisibility.JellyfinContent(httpContext),
            cancellationToken);
        return Results.Ok(result);
    }
}
