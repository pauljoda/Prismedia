using Prismedia.Api.Security;
using Prismedia.Application.Jellyfin;
using Prismedia.Application.Security;
using Prismedia.Contracts.Jellyfin;
using Prismedia.Contracts.Security;
using Prismedia.Contracts.System;

namespace Prismedia.Api.Endpoints;

public static partial class JellyfinCompatibilityEndpoints {
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

        routes.MapGet("/System/Endpoint", (HttpContext httpContext) =>
            Results.Ok(ToEndpointInfo(httpContext)))
            .WithTags("Jellyfin System")
            .WithName("GetJellyfinEndpointInfo")
            .Produces<JellyfinEndpointInfo>();

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

        routes.MapGet("/Branding/Splashscreen", () => Results.NotFound())
            .WithTags("Jellyfin Branding")
            .WithName("GetJellyfinBrandingSplashscreen")
            .Produces(StatusCodes.Status404NotFound);

        // Clients probe this during the connect phase to decide whether to offer QuickConnect login.
        // It must answer with a JSON boolean — falling through to the SPA returns HTML and can wedge
        // a client's init before it ever loads libraries.
        routes.MapGet("/QuickConnect/Enabled", () => Results.Ok(false))
            .WithTags("Jellyfin System")
            .WithName("GetJellyfinQuickConnectEnabled")
            .Produces<bool>();

        routes.MapPost("/QuickConnect/Initiate", () =>
            Results.Json(
                new ApiProblem(ApiProblemCodes.JellyfinQuickConnectDisabled, "Quick Connect is disabled."),
                statusCode: StatusCodes.Status401Unauthorized))
            .WithTags("Jellyfin System")
            .WithName("InitiateJellyfinQuickConnect")
            .Produces<ApiProblem>(StatusCodes.Status401Unauthorized);

        routes.MapGet("/QuickConnect/Connect", () =>
            Results.Json(
                new ApiProblem(ApiProblemCodes.JellyfinQuickConnectNotFound, "Unknown Quick Connect secret."),
                statusCode: StatusCodes.Status404NotFound))
            .WithTags("Jellyfin System")
            .WithName("GetJellyfinQuickConnectState")
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        routes.MapGet("/Startup/Configuration", () =>
            Results.Ok(new JellyfinStartupConfiguration("Prismedia", "", "US", "en")))
            .WithTags("Jellyfin Startup")
            .WithName("GetJellyfinStartupConfiguration")
            .Produces<JellyfinStartupConfiguration>();
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
                ? Results.NotFound(new ApiProblem(ApiProblemCodes.JellyfinUserNotFound, "No Jellyfin user was found."))
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
                ? Results.NotFound(new ApiProblem(ApiProblemCodes.JellyfinUserNotFound, $"User '{userId}' was not found."))
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

        routes.MapPost("/Users/AuthenticateWithQuickConnect", () =>
            Results.Json(
                new ApiProblem(ApiProblemCodes.JellyfinQuickConnectDisabled, "Quick Connect is disabled."),
                statusCode: StatusCodes.Status401Unauthorized))
            .WithTags("Jellyfin Users")
            .WithName("AuthenticateJellyfinUserWithQuickConnect")
            .Produces<ApiProblem>(StatusCodes.Status401Unauthorized);

        routes.MapPost("/Users/{userId:guid}/Authenticate", async (
            Guid userId,
            string? pw,
            HttpContext httpContext,
            PrismediaSecurityService security,
            CancellationToken cancellationToken) => {
            var profile = (await security.ListProfilesAsync(cancellationToken)).Items
                .FirstOrDefault(item => item.Id == userId && item.Enabled);
            return profile is null
                ? Results.NotFound(new ApiProblem(ApiProblemCodes.JellyfinUserNotFound, $"User '{userId}' was not found."))
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

        routes.MapGet("/Playlists/{playlistId}/Items", GetPlaylistItemsAsync)
            .WithTags("Jellyfin Playlists")
            .WithName("GetJellyfinPlaylistItems")
            .Produces<JellyfinQueryResult<JellyfinBaseItemDto>>()
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

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

        routes.MapGet("/Items/{itemId:guid}/Similar", EmptyPagedItemListAsync)
            .WithTags("Jellyfin Catalog")
            .WithName("GetJellyfinSimilarItems")
            .Produces<JellyfinQueryResult<JellyfinBaseItemDto>>();

        routes.MapGet("/Shows/{itemId:guid}/Similar", EmptyPagedItemListAsync)
            .WithTags("Jellyfin Catalog")
            .WithName("GetJellyfinSimilarShows")
            .ExcludeFromDescription()
            .Produces<JellyfinQueryResult<JellyfinBaseItemDto>>();

        routes.MapGet("/Movies/{itemId:guid}/Similar", EmptyPagedItemListAsync)
            .WithTags("Jellyfin Catalog")
            .WithName("GetJellyfinSimilarMovies")
            .ExcludeFromDescription()
            .Produces<JellyfinQueryResult<JellyfinBaseItemDto>>();

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

        routes.MapGet("/Artists", GetArtistsAsync)
            .WithTags("Jellyfin Music")
            .WithName("GetJellyfinArtists")
            .Produces<JellyfinQueryResult<JellyfinBaseItemDto>>();

        routes.MapGet("/Artists/AlbumArtists", GetArtistsAsync)
            .WithTags("Jellyfin Music")
            .WithName("GetJellyfinAlbumArtists")
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
            Results.Ok(await catalog.GetImageInfosAsync(itemId, NsfwVisibility.JellyfinContent(httpContext), cancellationToken)))
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

        routes.MapGet("/Items/Images/{imageType}", MalformedItemImageProbe)
            .WithTags("Jellyfin Images")
            .WithName("GetMalformedJellyfinItemImageProbe")
            .ExcludeFromDescription()
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        routes.MapGet("/Items/Images/{imageType}/{imageIndex:int}", MalformedItemImageProbe)
            .WithTags("Jellyfin Images")
            .WithName("GetMalformedJellyfinItemImageProbeByIndex")
            .ExcludeFromDescription()
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);
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

        // Some clients enumerate libraries via /Library/MediaFolders instead of /UserViews; return the
        // same top-level library views so either entry point discovers the Music (and video) libraries.
        routes.MapGet("/Library/MediaFolders", async (
            HttpContext httpContext,
            PrismediaSecurityService security,
            JellyfinCatalogService catalog,
            CancellationToken cancellationToken) => {
            var state = await security.EnsureSecurityAsync(cancellationToken);
            var views = await catalog.GetUserViewsWithArtworkAsync(
                state.ServerId.ToString("N"),
                NsfwVisibility.JellyfinContent(httpContext),
                cancellationToken);
            return Results.Ok(views);
        })
            .WithTags("Jellyfin Library")
            .WithName("GetJellyfinMediaFolders")
            .WithSummary("Gets Jellyfin-compatible media library folders.")
            .Produces<JellyfinQueryResult<JellyfinBaseItemDto>>();
    }

    private static void MapJellyfinCompatibilityNoOps(this IEndpointRouteBuilder routes) {
        routes.MapGet("/Sessions", (HttpContext httpContext) =>
            Results.Ok(CurrentSessions(httpContext)))
            .WithTags("Jellyfin Sessions")
            .WithName("GetJellyfinSessions")
            .Produces<IReadOnlyList<JellyfinSessionInfoDto>>();

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
                new ApiProblem(ApiProblemCodes.AuthRateLimited, "Too many failed authentication attempts."),
                statusCode: StatusCodes.Status429TooManyRequests);
        }

        if (!result.Succeeded || result.Profile is null || result.Session is null || result.AccessToken is null) {
            return Results.Json(
                new ApiProblem(ApiProblemCodes.JellyfinAuthFailed, "Invalid username or API key."),
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

    private static IReadOnlyList<JellyfinSessionInfoDto> CurrentSessions(HttpContext httpContext) =>
        httpContext.GetPrismediaAuth()?.JellyfinSession is { } resolution
            ? [ToSessionDto(resolution.Profile, resolution.Session)]
            : [];

    private static JellyfinEndpointInfo ToEndpointInfo(HttpContext httpContext) {
        var remote = httpContext.Connection.RemoteIpAddress;
        var isLocal = remote is null || System.Net.IPAddress.IsLoopback(remote);
        return new JellyfinEndpointInfo(isLocal, isLocal || IsPrivateNetworkAddress(remote));
    }

    private static bool IsPrivateNetworkAddress(System.Net.IPAddress? address) {
        if (address is null) {
            return true;
        }

        if (address.IsIPv4MappedToIPv6) {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) {
            return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6UniqueLocal;
        }

        var bytes = address.GetAddressBytes();
        return bytes[0] == 10 ||
            (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
            (bytes[0] == 192 && bytes[1] == 168) ||
            (bytes[0] == 169 && bytes[1] == 254);
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
            NsfwVisibility.JellyfinContent(httpContext),
            cancellationToken);
        if (image is null) {
            return Results.NotFound(new ApiProblem(ApiProblemCodes.JellyfinImageNotFound, $"Image '{imageType}' for item '{itemId}' was not found."));
        }

        var file = await files.ResolveAsync(image, cancellationToken);
        if (file is null) {
            return Results.NotFound(new ApiProblem(ApiProblemCodes.JellyfinImageNotFound, $"Image '{imageType}' for item '{itemId}' was not found."));
        }

        if (file.RedirectUrl is not null) {
            return Results.Redirect(file.RedirectUrl);
        }

        httpContext.Response.Headers.ETag = $"\"{file.ImageTag}\"";
        return Results.File(File.OpenRead(file.FilePath!), file.ContentType, enableRangeProcessing: false);
    }

    private static IResult MalformedItemImageProbe(string imageType) =>
        Results.NotFound(new ApiProblem(
            ApiProblemCodes.JellyfinImageNotFound,
            $"Image '{imageType}' was not found."));

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
            TryBool(request.Query["IsPlayed"].FirstOrDefault()),
            // Infuse drills into a cast member with PersonIds (plural CSV); accept the singular
            // PersonId form too so either client spelling resolves the performer's filmography.
            SplitGuids(request.Query["PersonIds"].FirstOrDefault() ?? request.Query["PersonId"].FirstOrDefault()));

    private static string VirtualFolderCollectionType(JellyfinBaseItemDto item) =>
        item.CollectionType switch {
            JellyfinProtocol.CollectionTypes.Shows => JellyfinProtocol.CollectionTypes.Shows,
            JellyfinProtocol.CollectionTypes.BoxSets => JellyfinProtocol.CollectionTypes.BoxSets,
            _ => JellyfinProtocol.CollectionTypes.Movies
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
