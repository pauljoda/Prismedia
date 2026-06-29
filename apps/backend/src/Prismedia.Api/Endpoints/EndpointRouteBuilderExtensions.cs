using Prismedia.Api.Security;
using Prismedia.Application.Jellyfin;

namespace Prismedia.Api.Endpoints;

public static class EndpointRouteBuilderExtensions {
    public static IEndpointRouteBuilder MapPrismediaEndpoints(this IEndpointRouteBuilder routes) {
        routes.MapHealthEndpoints();
        routes.MapSecurityEndpoints();
        routes.MapEntityEndpoints();
        routes.MapVideoEndpoints();
        routes.MapMovieEndpoints();
        routes.MapSeriesEndpoints();
        routes.MapImageEndpoints();
        routes.MapGalleryEndpoints();
        routes.MapBookEndpoints();
        routes.MapBookAuthorEndpoints();
        routes.MapOpdsEndpoints();
        routes.MapMusicArtistEndpoints();
        routes.MapAudioLibraryEndpoints();
        routes.MapAudioTrackEndpoints();
        routes.MapPeopleEndpoints();
        routes.MapStudioEndpoints();
        routes.MapTagEndpoints();
        routes.MapCollectionEndpoints();
        routes.MapJellyfinCompatibilityEndpoints();
        routes.MapJellyfinPlaybackEndpoints();
        routes.MapBrowserSessionEndpoints();
        routes.MapMusicPlayerEndpoints();
        routes.MapPlaybackStatisticsEndpoints();
        routes.MapJobEndpoints();
        routes.MapSettingsEndpoints();
        routes.MapNavLayoutEndpoints();
        routes.MapLibraryEndpoints();
        routes.MapFilesEndpoints();
        routes.MapUpdateCheckEndpoints();
        routes.MapPluginEndpoints();
        routes.MapIdentifyEndpoints();
        routes.MapOrganizeEndpoints();
        routes.MapRequestEndpoints();
        routes.MapAcquisitionEndpoints();
        routes.MapMonitorEndpoints();

        return routes;
    }
}

/// <summary>
/// Resolves the caller's NSFW visibility preference at the HTTP edge. Content is
/// hidden unless the request explicitly opts into show mode by query or cookie.
/// </summary>
internal static class NsfwVisibility {
    private const string CookieName = "prismedia-nsfw-mode";

    /// <summary>
    /// Returns true when NSFW rows should be withheld from this response.
    /// </summary>
    /// <param name="explicitHide">Optional route query override.</param>
    /// <param name="httpContext">Current HTTP context used to inspect the NSFW mode cookie.</param>
    public static bool ShouldHide(bool? explicitHide, HttpContext httpContext) {
        if (httpContext.GetJellyfinProfile() is { } profile) {
            return !profile.AllowNsfw;
        }

        if (explicitHide is { } hide) {
            return hide;
        }

        return !httpContext.Request.Cookies.TryGetValue(CookieName, out var mode) ||
            !string.Equals(mode, "show", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Returns the Jellyfin profile's explicit content visibility toggles, or the web portal cookie mode for app-key requests.</summary>
    public static JellyfinContentVisibility JellyfinContent(HttpContext httpContext) =>
        httpContext.GetJellyfinProfile() is { } profile
            ? new JellyfinContentVisibility(profile.AllowSfw, profile.AllowNsfw)
            : JellyfinContentVisibility.FromHideNsfw(ShouldHide(null, httpContext));
}
