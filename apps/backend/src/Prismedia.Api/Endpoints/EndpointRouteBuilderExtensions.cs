using Prismedia.Api.Security;
using Prismedia.Application.Jellyfin;

namespace Prismedia.Api.Endpoints;

public static class EndpointRouteBuilderExtensions {
    public static IEndpointRouteBuilder MapPrismediaEndpoints(this IEndpointRouteBuilder routes) {
        routes.MapHealthEndpoints();
        routes.MapAuthEndpoints();
        routes.MapUserEndpoints();
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
/// Resolves the caller's NSFW visibility preference at the HTTP edge. The user's
/// <c>AllowNsfw</c> flag is a server-enforced ceiling. Within it, web-browser sessions
/// (authenticated via the session cookie) opt into show mode by query or cookie, while
/// protocol clients (Jellyfin/OPDS tokens, Basic auth) have no toggle — the permission
/// alone decides.
/// </summary>
internal static class NsfwVisibility {
    private const string CookieName = "prismedia-nsfw-mode";
    private const string ShowMode = "show";

    /// <summary>
    /// Returns true when NSFW rows should be withheld from this response.
    /// </summary>
    /// <param name="explicitHide">Optional route query override (only widens hiding within the user cap).</param>
    /// <param name="httpContext">Current HTTP context used to inspect the NSFW mode cookie.</param>
    public static bool ShouldHide(bool? explicitHide, HttpContext httpContext) {
        var auth = httpContext.GetPrismediaAuth();
        if (auth is not null && !auth.User.AllowNsfw) {
            return true;
        }

        if (auth is { ViaCookie: false }) {
            return false;
        }

        if (explicitHide is { } hide) {
            return hide;
        }

        return !httpContext.Request.Cookies.TryGetValue(CookieName, out var mode) ||
            !string.Equals(mode, ShowMode, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Returns the caller's content visibility toggles for Jellyfin projections.</summary>
    public static JellyfinContentVisibility JellyfinContent(HttpContext httpContext) =>
        httpContext.GetCurrentUser() is { } user
            ? new JellyfinContentVisibility(user.AllowSfw, !ShouldHide(null, httpContext))
            : JellyfinContentVisibility.FromHideNsfw(ShouldHide(null, httpContext));
}
