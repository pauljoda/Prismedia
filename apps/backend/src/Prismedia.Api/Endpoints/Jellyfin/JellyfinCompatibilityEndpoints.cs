using Prismedia.Api.Security;
using Prismedia.Application.Jellyfin;
using Prismedia.Application.Security;
using Prismedia.Contracts.Jellyfin;
using Prismedia.Contracts.Security;
using Prismedia.Contracts.System;

namespace Prismedia.Api.Endpoints;

public static class JellyfinCompatibilityEndpoints {
    public static IEndpointRouteBuilder MapJellyfinCompatibilityEndpoints(this IEndpointRouteBuilder routes) {
        routes.MapJellyfinSystemEndpoints();
        routes.MapJellyfinUserEndpoints();
        routes.MapJellyfinCatalogEndpoints();
        routes.MapJellyfinImageEndpoints();
        routes.MapJellyfinLibraryEndpoints();
        routes.MapJellyfinCompatibilityNoOps();
        return routes;
    }

    private static void MapJellyfinSystemEndpoints(this IEndpointRouteBuilder routes) {
        routes.MapGet("/System/Ping", () => Results.Text("Prismedia"))
            .WithTags("Jellyfin System")
            .WithName("GetJellyfinPing")
            .WithSummary("Gets a Jellyfin-compatible ping response.");

        routes.MapPost("/System/Ping", () => Results.Text("Prismedia"))
            .WithTags("Jellyfin System")
            .WithName("PostJellyfinPing")
            .ExcludeFromDescription();

        routes.MapGet("/System/Info/Public", async (
            HttpContext httpContext,
            PrismediaSecurityService security,
            CancellationToken cancellationToken) =>
            Results.Ok(ToPublicSystemInfo(httpContext, await security.EnsureSecurityAsync(cancellationToken))))
            .WithTags("Jellyfin System")
            .WithName("GetJellyfinPublicSystemInfo")
            .Produces<JellyfinPublicSystemInfo>();

        routes.MapGet("/System/Info", async (
            HttpContext httpContext,
            PrismediaSecurityService security,
            CancellationToken cancellationToken) => {
            var publicInfo = ToPublicSystemInfo(httpContext, await security.EnsureSecurityAsync(cancellationToken));
            return Results.Ok(new JellyfinSystemInfo(
                publicInfo.LocalAddress,
                publicInfo.ServerName,
                publicInfo.Version,
                publicInfo.ProductName,
                publicInfo.Id,
                publicInfo.StartupWizardCompleted,
                Environment.OSVersion.Platform.ToString(),
                "Prismedia",
                publicInfo.ServerName));
        })
            .WithTags("Jellyfin System")
            .WithName("GetJellyfinSystemInfo")
            .Produces<JellyfinSystemInfo>();

        routes.MapGet("/Branding/Configuration", () =>
            Results.Ok(new JellyfinBrandingConfiguration("", "", SplashscreenEnabled: false)))
            .WithTags("Jellyfin Branding")
            .WithName("GetJellyfinBrandingConfiguration")
            .Produces<JellyfinBrandingConfiguration>();

        routes.MapGet("/Branding/Css", () => Results.Text("", "text/css"))
            .WithTags("Jellyfin Branding")
            .WithName("GetJellyfinBrandingCss");

        routes.MapGet("/Branding/Css.css", () => Results.Text("", "text/css"))
            .WithTags("Jellyfin Branding")
            .WithName("GetJellyfinBrandingCssFile");
    }

    private static void MapJellyfinUserEndpoints(this IEndpointRouteBuilder routes) {
        routes.MapGet("/Users/Public", async (
            PrismediaSecurityService security,
            CancellationToken cancellationToken) =>
            Results.Ok(await JellyfinUsersAsync(security, enabledOnly: true, cancellationToken)))
            .WithTags("Jellyfin Users")
            .WithName("GetJellyfinPublicUsers")
            .Produces<IReadOnlyList<JellyfinUserDto>>();

        routes.MapGet("/Users", async (
            PrismediaSecurityService security,
            CancellationToken cancellationToken) =>
            Results.Ok(await JellyfinUsersAsync(security, enabledOnly: true, cancellationToken)))
            .WithTags("Jellyfin Users")
            .WithName("GetJellyfinUsers")
            .Produces<IReadOnlyList<JellyfinUserDto>>();

        routes.MapGet("/Users/Me", async (
            HttpContext httpContext,
            PrismediaSecurityService security,
            CancellationToken cancellationToken) => {
            var user = await ResolveUserAsync(httpContext, security, null, cancellationToken);
            return user is null
                ? Results.NotFound(new ApiProblem("jellyfin_user_not_found", "No Jellyfin user was found."))
                : Results.Ok(user);
        })
            .WithTags("Jellyfin Users")
            .WithName("GetJellyfinCurrentUser")
            .Produces<JellyfinUserDto>()
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        routes.MapGet("/Users/{userId:guid}", async (
            Guid userId,
            HttpContext httpContext,
            PrismediaSecurityService security,
            CancellationToken cancellationToken) => {
            var user = await ResolveUserAsync(httpContext, security, userId, cancellationToken);
            return user is null
                ? Results.NotFound(new ApiProblem("jellyfin_user_not_found", $"User '{userId}' was not found."))
                : Results.Ok(user);
        })
            .WithTags("Jellyfin Users")
            .WithName("GetJellyfinUser")
            .Produces<JellyfinUserDto>()
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        routes.MapPost("/Users/AuthenticateByName", async (
            JellyfinAuthenticateByNameRequest request,
            HttpContext httpContext,
            PrismediaSecurityService security,
            CancellationToken cancellationToken) => {
            return await AuthenticateAsync(
                request.Username,
                request.EffectivePassword,
                httpContext,
                security,
                cancellationToken);
        })
            .WithTags("Jellyfin Users")
            .WithName("AuthenticateJellyfinUserByName")
            .Produces<JellyfinAuthenticationResult>()
            .Produces<ApiProblem>(StatusCodes.Status401Unauthorized)
            .Produces<ApiProblem>(StatusCodes.Status429TooManyRequests);

        routes.MapPost("/Users/{userId:guid}/Authenticate", async (
            Guid userId,
            string? pw,
            HttpContext httpContext,
            PrismediaSecurityService security,
            CancellationToken cancellationToken) => {
            var profile = (await security.ListProfilesAsync(cancellationToken)).Items
                .FirstOrDefault(item => item.Id == userId && item.Enabled);
            return profile is null
                ? Results.NotFound(new ApiProblem("jellyfin_user_not_found", $"User '{userId}' was not found."))
                : await AuthenticateAsync(profile.Username, pw, httpContext, security, cancellationToken);
        })
            .WithTags("Jellyfin Users")
            .WithName("AuthenticateJellyfinUserLegacy")
            .ExcludeFromDescription();
    }

    private static void MapJellyfinCatalogEndpoints(this IEndpointRouteBuilder routes) {
        routes.MapGet("/UserViews", GetUserViewsAsync)
            .WithTags("Jellyfin Catalog")
            .WithName("GetJellyfinUserViews")
            .Produces<JellyfinQueryResult<JellyfinBaseItemDto>>();

        routes.MapGet("/Users/{userId:guid}/Views", GetUserViewsAsync)
            .WithTags("Jellyfin Catalog")
            .WithName("GetJellyfinUserViewsLegacy")
            .ExcludeFromDescription()
            .Produces<JellyfinQueryResult<JellyfinBaseItemDto>>();

        routes.MapGet("/UserViews/GroupingOptions", async (
            Guid? userId,
            HttpContext httpContext,
            PrismediaSecurityService security,
            JellyfinCatalogService catalog,
            CancellationToken cancellationToken) =>
            await GetGroupingOptionsAsync(userId, httpContext, security, catalog, cancellationToken))
            .WithTags("Jellyfin Catalog")
            .WithName("GetJellyfinUserViewGroupingOptions")
            .Produces<IReadOnlyList<JellyfinSpecialViewOptionDto>>()
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        routes.MapGet("/Users/{userId:guid}/GroupingOptions", async (
            Guid userId,
            HttpContext httpContext,
            PrismediaSecurityService security,
            JellyfinCatalogService catalog,
            CancellationToken cancellationToken) =>
            await GetGroupingOptionsAsync(userId, httpContext, security, catalog, cancellationToken))
            .WithTags("Jellyfin Catalog")
            .WithName("GetJellyfinUserViewGroupingOptionsLegacy")
            .Produces<IReadOnlyList<JellyfinSpecialViewOptionDto>>()
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        routes.MapGet("/Items/Root", GetRootAsync)
            .WithTags("Jellyfin Catalog")
            .WithName("GetJellyfinItemsRoot")
            .Produces<JellyfinBaseItemDto>();

        routes.MapGet("/Users/{userId:guid}/Items/Root", GetRootAsync)
            .WithTags("Jellyfin Catalog")
            .WithName("GetJellyfinItemsRootLegacy")
            .ExcludeFromDescription()
            .Produces<JellyfinBaseItemDto>();

        routes.MapGet("/Items", GetItemsAsync)
            .WithTags("Jellyfin Catalog")
            .WithName("GetJellyfinItems")
            .Produces<JellyfinQueryResult<JellyfinBaseItemDto>>();

        routes.MapGet("/Users/{userId:guid}/Items", GetItemsAsync)
            .WithTags("Jellyfin Catalog")
            .WithName("GetJellyfinItemsLegacy")
            .ExcludeFromDescription()
            .Produces<JellyfinQueryResult<JellyfinBaseItemDto>>();

        routes.MapGet("/Items/{itemId:guid}", GetItemAsync)
            .WithTags("Jellyfin Catalog")
            .WithName("GetJellyfinItem")
            .Produces<JellyfinBaseItemDto>()
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        routes.MapGet("/Users/{userId:guid}/Items/{itemId:guid}", GetItemAsync)
            .WithTags("Jellyfin Catalog")
            .WithName("GetJellyfinItemLegacy")
            .ExcludeFromDescription()
            .Produces<JellyfinBaseItemDto>()
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        routes.MapGet("/Items/Latest", GetLatestAsync)
            .WithTags("Jellyfin Catalog")
            .WithName("GetJellyfinLatestItems")
            .Produces<IReadOnlyList<JellyfinBaseItemDto>>();

        routes.MapGet("/Users/{userId:guid}/Items/Latest", GetLatestAsync)
            .WithTags("Jellyfin Catalog")
            .WithName("GetJellyfinLatestItemsLegacy")
            .ExcludeFromDescription()
            .Produces<IReadOnlyList<JellyfinBaseItemDto>>();

        routes.MapGet("/UserItems/Resume", GetResumeAsync)
            .WithTags("Jellyfin Catalog")
            .WithName("GetJellyfinResumeItems")
            .Produces<JellyfinQueryResult<JellyfinBaseItemDto>>();

        routes.MapGet("/Users/{userId:guid}/Items/Resume", GetResumeAsync)
            .WithTags("Jellyfin Catalog")
            .WithName("GetJellyfinResumeItemsLegacy")
            .ExcludeFromDescription()
            .Produces<JellyfinQueryResult<JellyfinBaseItemDto>>();

        routes.MapGet("/Shows/{seriesId:guid}/Seasons", GetSeriesSeasonsAsync)
            .WithTags("Jellyfin Shows")
            .WithName("GetJellyfinSeriesSeasons")
            .Produces<JellyfinQueryResult<JellyfinBaseItemDto>>();

        routes.MapGet("/Users/{userId:guid}/Shows/{seriesId:guid}/Seasons", GetSeriesSeasonsAsync)
            .WithTags("Jellyfin Shows")
            .WithName("GetJellyfinSeriesSeasonsLegacy")
            .ExcludeFromDescription()
            .Produces<JellyfinQueryResult<JellyfinBaseItemDto>>();

        routes.MapGet("/Shows/{seriesId:guid}/Episodes", GetSeriesEpisodesAsync)
            .WithTags("Jellyfin Shows")
            .WithName("GetJellyfinSeriesEpisodes")
            .Produces<JellyfinQueryResult<JellyfinBaseItemDto>>();

        routes.MapGet("/Users/{userId:guid}/Shows/{seriesId:guid}/Episodes", GetSeriesEpisodesAsync)
            .WithTags("Jellyfin Shows")
            .WithName("GetJellyfinSeriesEpisodesLegacy")
            .ExcludeFromDescription()
            .Produces<JellyfinQueryResult<JellyfinBaseItemDto>>();

        routes.MapGet("/Shows/NextUp", GetNextUpAsync)
            .WithTags("Jellyfin Shows")
            .WithName("GetJellyfinNextUp")
            .Produces<JellyfinQueryResult<JellyfinBaseItemDto>>();

        routes.MapGet("/Users/{userId:guid}/Shows/NextUp", GetNextUpAsync)
            .WithTags("Jellyfin Shows")
            .WithName("GetJellyfinNextUpLegacy")
            .ExcludeFromDescription()
            .Produces<JellyfinQueryResult<JellyfinBaseItemDto>>();

        // Extras endpoints Infuse probes on every detail page. Prismedia has no local trailers or
        // bonus features yet, so these return the empty Jellyfin envelope to avoid client 404s.
        routes.MapGet("/Items/{itemId:guid}/LocalTrailers", EmptyItemListAsync)
            .WithTags("Jellyfin Catalog")
            .WithName("GetJellyfinLocalTrailers")
            .Produces<IReadOnlyList<JellyfinBaseItemDto>>();

        routes.MapGet("/Items/{itemId:guid}/SpecialFeatures", EmptyItemListAsync)
            .WithTags("Jellyfin Catalog")
            .WithName("GetJellyfinSpecialFeatures")
            .Produces<IReadOnlyList<JellyfinBaseItemDto>>();

        routes.MapGet("/Users/{userId:guid}/Items/{itemId:guid}/LocalTrailers", EmptyItemListAsync)
            .WithTags("Jellyfin Catalog")
            .WithName("GetJellyfinLocalTrailersLegacy")
            .ExcludeFromDescription()
            .Produces<IReadOnlyList<JellyfinBaseItemDto>>();

        routes.MapGet("/Users/{userId:guid}/Items/{itemId:guid}/SpecialFeatures", EmptyItemListAsync)
            .WithTags("Jellyfin Catalog")
            .WithName("GetJellyfinSpecialFeaturesLegacy")
            .ExcludeFromDescription()
            .Produces<IReadOnlyList<JellyfinBaseItemDto>>();

        // Media segments (intro/credit skip markers). Real Jellyfin returns an empty paged result
        // when an item has none; match that shape rather than 404ing.
        routes.MapGet("/MediaSegments/{itemId:guid}", GetMediaSegmentsAsync)
            .WithTags("Jellyfin Catalog")
            .WithName("GetJellyfinMediaSegments")
            .Produces<JellyfinQueryResult<JellyfinMediaSegmentDto>>();
    }

    private static void MapJellyfinImageEndpoints(this IEndpointRouteBuilder routes) {
        routes.MapGet("/Items/{itemId:guid}/Images", async (
            Guid itemId,
            HttpContext httpContext,
            JellyfinCatalogService catalog,
            CancellationToken cancellationToken) =>
            Results.Ok(await catalog.GetImageInfosAsync(itemId, NsfwVisibility.ShouldHide(null, httpContext), cancellationToken)))
            .WithTags("Jellyfin Images")
            .WithName("GetJellyfinItemImages")
            .Produces<IReadOnlyList<JellyfinImageInfo>>();

        routes.MapGet("/Items/{itemId:guid}/Images/{imageType}", StreamImageAsync)
            .WithTags("Jellyfin Images")
            .WithName("GetJellyfinItemImage")
            .Produces(StatusCodes.Status200OK)
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        routes.MapMethods("/Items/{itemId:guid}/Images/{imageType}", [HttpMethods.Head], StreamImageAsync)
            .ExcludeFromDescription();

        routes.MapGet("/Items/{itemId:guid}/Images/{imageType}/{imageIndex:int}", StreamImageAsync)
            .WithTags("Jellyfin Images")
            .WithName("GetJellyfinItemImageByIndex")
            .Produces(StatusCodes.Status200OK)
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        routes.MapMethods("/Items/{itemId:guid}/Images/{imageType}/{imageIndex:int}", [HttpMethods.Head], StreamImageAsync)
            .ExcludeFromDescription();
    }

    private static void MapJellyfinLibraryEndpoints(this IEndpointRouteBuilder routes) {
        routes.MapGet("/Library/VirtualFolders", async (
            PrismediaSecurityService security,
            JellyfinCatalogService catalog,
            CancellationToken cancellationToken) => {
            var state = await security.EnsureSecurityAsync(cancellationToken);
            var folders = catalog.GetUserViews(state.ServerId.ToString("N"))
                .Items
                .Select(ToVirtualFolder)
                .ToArray();
            return Results.Ok(folders);
        })
            .WithTags("Jellyfin Library")
            .WithName("GetJellyfinVirtualFolders")
            .WithSummary("Gets Jellyfin-compatible virtual library folders.")
            .Produces<IReadOnlyList<JellyfinVirtualFolderInfoDto>>();
    }

    private static void MapJellyfinCompatibilityNoOps(this IEndpointRouteBuilder routes) {
        routes.MapPost("/Sessions/Capabilities", () => Results.NoContent())
            .WithTags("Jellyfin Sessions")
            .WithName("PostJellyfinCapabilities")
            .Produces(StatusCodes.Status204NoContent);

        routes.MapPost("/Sessions/Capabilities/Full", () => Results.NoContent())
            .WithTags("Jellyfin Sessions")
            .WithName("PostJellyfinFullCapabilities")
            .Produces(StatusCodes.Status204NoContent);

        routes.MapGet("/DisplayPreferences/{displayPreferencesId}", (
            string displayPreferencesId,
            string? client) =>
            Results.Ok(new JellyfinDisplayPreferencesDto {
                Id = displayPreferencesId,
                Client = client,
                SortBy = "SortName",
                SortOrder = "Ascending",
                CustomPrefs = new Dictionary<string, string> {
                    ["chromecastVersion"] = "stable",
                    ["skipForwardLength"] = "30000",
                    ["skipBackLength"] = "10000"
                }
            }))
            .WithTags("Jellyfin Display Preferences")
            .WithName("GetJellyfinDisplayPreferences")
            .Produces<JellyfinDisplayPreferencesDto>();

        // The route template includes {displayPreferencesId}, so the handler must bind it for the
        // generated OpenAPI document to declare it as a path parameter. Without the parameter the
        // spec is invalid (templated segment with no matching parameter) and breaks client codegen.
        routes.MapPost("/DisplayPreferences/{displayPreferencesId}", (string displayPreferencesId) =>
                Results.NoContent())
            .WithTags("Jellyfin Display Preferences")
            .WithName("UpdateJellyfinDisplayPreferences")
            .Produces(StatusCodes.Status204NoContent);
    }

    private static async Task<IResult> GetUserViewsAsync(
        PrismediaSecurityService security,
        JellyfinCatalogService catalog,
        CancellationToken cancellationToken) {
        var state = await security.EnsureSecurityAsync(cancellationToken);
        return Results.Ok(catalog.GetUserViews(state.ServerId.ToString("N")));
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
            return Results.NotFound(new ApiProblem("jellyfin_user_not_found", "No Jellyfin user was found."));
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
            NsfwVisibility.ShouldHide(null, httpContext),
            cancellationToken);
        return Results.Ok(result);
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
            NsfwVisibility.ShouldHide(null, httpContext),
            cancellationToken);
        return item is null
            ? Results.NotFound(new ApiProblem("jellyfin_item_not_found", $"Item '{itemId}' was not found."))
            : Results.Ok(item);
    }

    private static async Task<IResult> GetLatestAsync(
        HttpContext httpContext,
        PrismediaSecurityService security,
        JellyfinCatalogService catalog,
        CancellationToken cancellationToken) {
        var state = await security.EnsureSecurityAsync(cancellationToken);
        var limit = TryInt(httpContext.Request.Query["Limit"].FirstOrDefault()) ?? 20;
        var result = await catalog.GetLatestAsync(
            Math.Clamp(limit, 1, 100),
            state.ServerId.ToString("N"),
            NsfwVisibility.ShouldHide(null, httpContext),
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
            NsfwVisibility.ShouldHide(null, httpContext),
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
            NsfwVisibility.ShouldHide(null, httpContext),
            cancellationToken);
        return Results.Ok(result);
    }

    private static IResult EmptyItemListAsync() =>
        Results.Ok(Array.Empty<JellyfinBaseItemDto>());

    private static IResult GetMediaSegmentsAsync() =>
        Results.Ok(new JellyfinQueryResult<JellyfinMediaSegmentDto>([], 0, 0));

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
            IncludeItemTypes = ["Season"]
        };
        var result = await catalog.GetItemsAsync(
            query,
            state.ServerId.ToString("N"),
            NsfwVisibility.ShouldHide(null, httpContext),
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
            IncludeItemTypes = ["Episode"]
        };
        var result = await catalog.GetItemsAsync(
            query,
            state.ServerId.ToString("N"),
            NsfwVisibility.ShouldHide(null, httpContext),
            cancellationToken);
        return Results.Ok(result);
    }

    private static async Task<IResult> AuthenticateAsync(
        string? username,
        string? password,
        HttpContext httpContext,
        PrismediaSecurityService security,
        CancellationToken cancellationToken) {
        var result = await security.AuthenticateJellyfinProfileAsync(
            username,
            password,
            httpContext.Request.GetJellyfinClientIdentity(),
            PrismediaAuthentication.BucketFor(httpContext, username),
            cancellationToken);

        if (result.IsThrottled) {
            return Results.Json(
                new ApiProblem("auth_rate_limited", "Too many failed authentication attempts."),
                statusCode: StatusCodes.Status429TooManyRequests);
        }

        if (!result.Succeeded || result.Profile is null || result.Session is null || result.AccessToken is null) {
            return Results.Json(
                new ApiProblem("jellyfin_auth_failed", "Invalid username or API key."),
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var state = await security.EnsureSecurityAsync(cancellationToken);
        var user = ToUserDto(result.Profile, state);
        return Results.Ok(new JellyfinAuthenticationResult(
            user,
            ToSessionDto(result.Profile, result.Session),
            result.AccessToken,
            state.ServerId.ToString("N")));
    }

    private static async Task<IResult> StreamImageAsync(
        Guid itemId,
        string imageType,
        int? imageIndex,
        HttpContext httpContext,
        JellyfinCatalogService catalog,
        IJellyfinImageFileService files,
        CancellationToken cancellationToken) {
        var image = await catalog.GetImageAssetAsync(
            itemId,
            imageType,
            imageIndex,
            NsfwVisibility.ShouldHide(null, httpContext),
            cancellationToken);
        if (image is null) {
            return Results.NotFound(new ApiProblem("jellyfin_image_not_found", $"Image '{imageType}' for item '{itemId}' was not found."));
        }

        var file = await files.ResolveAsync(image, cancellationToken);
        if (file is null) {
            return Results.NotFound(new ApiProblem("jellyfin_image_not_found", $"Image '{imageType}' for item '{itemId}' was not found."));
        }

        if (file.RedirectUrl is not null) {
            return Results.Redirect(file.RedirectUrl);
        }

        httpContext.Response.Headers.ETag = $"\"{file.ImageTag}\"";
        return Results.File(File.OpenRead(file.FilePath!), file.ContentType, enableRangeProcessing: false);
    }

    private static JellyfinItemQuery ItemQueryFrom(HttpRequest request) =>
        new(
            TryGuid(request.Query["ParentId"].FirstOrDefault()),
            SplitGuids(request.Query["Ids"].FirstOrDefault()),
            TryBool(request.Query["Recursive"].FirstOrDefault()) ?? false,
            request.Query["SearchTerm"].FirstOrDefault(),
            SplitCsv(request.Query["IncludeItemTypes"].FirstOrDefault()),
            Math.Max(0, TryInt(request.Query["StartIndex"].FirstOrDefault()) ?? 0),
            TryInt(request.Query["Limit"].FirstOrDefault()),
            request.Query["SortBy"].FirstOrDefault(),
            request.Query["SortOrder"].FirstOrDefault(),
            TryBool(request.Query["IsFavorite"].FirstOrDefault()),
            TryBool(request.Query["IsPlayed"].FirstOrDefault()));

    private static JellyfinPublicSystemInfo ToPublicSystemInfo(HttpContext httpContext, AppSecurityState state) {
        var version = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        return new JellyfinPublicSystemInfo(
            $"{httpContext.Request.Scheme}://{httpContext.Request.Host}",
            "Prismedia",
            version,
            "Prismedia",
            state.ServerId.ToString("N"),
            StartupWizardCompleted: true);
    }

    private static async Task<IReadOnlyList<JellyfinUserDto>> JellyfinUsersAsync(
        PrismediaSecurityService security,
        bool enabledOnly,
        CancellationToken cancellationToken) {
        var state = await security.EnsureSecurityAsync(cancellationToken);
        var profiles = (await security.ListProfilesAsync(cancellationToken)).Items
            .Where(profile => !enabledOnly || profile.Enabled)
            .Select(profile => ToUserDto(profile, state))
            .ToArray();
        return profiles;
    }

    private static async Task<JellyfinUserDto?> ResolveUserAsync(
        HttpContext httpContext,
        PrismediaSecurityService security,
        Guid? userId,
        CancellationToken cancellationToken) {
        var state = await security.EnsureSecurityAsync(cancellationToken);
        if (httpContext.GetJellyfinProfile() is { } activeProfile && (userId is null || activeProfile.Id == userId)) {
            return ToUserDto(activeProfile, state);
        }

        var profiles = (await security.ListProfilesAsync(cancellationToken)).Items;
        var profile = userId is null
            ? profiles.FirstOrDefault(item => item.Enabled)
            : profiles.FirstOrDefault(item => item.Id == userId && item.Enabled);
        return profile is null ? null : ToUserDto(profile, state);
    }

    private static JellyfinUserDto ToUserDto(JellyfinProfileResponse profile, AppSecurityState state) =>
        new(
            profile.DisplayName,
            state.ServerId.ToString("N"),
            "Prismedia",
            profile.Id,
            HasPassword: true,
            HasConfiguredPassword: true,
            HasConfiguredEasyPassword: true,
            EnableAutoLogin: false,
            profile.LastLoginAt,
            profile.LastLoginAt,
            UserPolicy(profile),
            UserConfiguration());

    private static JellyfinUserDto ToUserDto(JellyfinProfile profile, AppSecurityState state) =>
        new(
            profile.DisplayName,
            state.ServerId.ToString("N"),
            "Prismedia",
            profile.Id,
            HasPassword: true,
            HasConfiguredPassword: true,
            HasConfiguredEasyPassword: true,
            EnableAutoLogin: false,
            profile.LastLoginAt,
            profile.LastLoginAt,
            UserPolicy(profile),
            UserConfiguration());

    private static JellyfinSessionInfoDto ToSessionDto(JellyfinProfile profile, JellyfinSession session) =>
        new(
            session.Id.ToString("N"),
            profile.Id,
            profile.DisplayName,
            session.Client,
            session.DeviceName,
            session.DeviceId,
            session.ApplicationVersion,
            IsActive: true);

    private static JellyfinUserPolicyDto UserPolicy(JellyfinProfileResponse profile) =>
        UserPolicy(profile.Enabled);

    private static JellyfinUserPolicyDto UserPolicy(JellyfinProfile profile) =>
        UserPolicy(profile.Enabled);

    private static JellyfinUserPolicyDto UserPolicy(bool enabled) =>
        new(
            IsAdministrator: false,
            IsHidden: false,
            IsDisabled: !enabled,
            EnableRemoteControlOfOtherUsers: false,
            EnableSharedDeviceControl: false,
            EnableContentDeletion: false,
            EnableContentDownloading: true,
            EnableSyncTranscoding: true,
            EnableMediaPlayback: true);

    private static JellyfinUserConfigurationDto UserConfiguration() =>
        new(
            AudioLanguagePreference: null,
            PlayDefaultAudioTrack: true,
            SubtitleLanguagePreference: null,
            DisplayMissingEpisodes: false,
            GroupedFolders: [],
            SubtitleMode: "Default");

    private static string VirtualFolderCollectionType(JellyfinBaseItemDto item) =>
        item.CollectionType switch {
            "tvshows" => "tvshows",
            "boxsets" => "boxsets",
            _ => "movies"
        };

    private static IReadOnlyList<string> SplitCsv(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static IReadOnlyList<Guid> SplitGuids(string? value) =>
        SplitCsv(value).Select(TryGuid).Where(id => id is not null).Select(id => id!.Value).ToArray();

    private static Guid? TryGuid(string? value) =>
        Guid.TryParse(value, out var result) ? result : null;

    private static int? TryInt(string? value) =>
        int.TryParse(value, out var result) ? result : null;

    private static bool? TryBool(string? value) =>
        bool.TryParse(value, out var result) ? result : null;
}
