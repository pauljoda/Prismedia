namespace Prismedia.Api.Endpoints;

public static class EndpointRouteBuilderExtensions {
    public static IEndpointRouteBuilder MapPrismediaEndpoints(this IEndpointRouteBuilder routes) {
        routes.MapHealthEndpoints();
        routes.MapEntityEndpoints();
        routes.MapVideoEndpoints();
        routes.MapSeriesEndpoints();
        routes.MapImageEndpoints();
        routes.MapGalleryEndpoints();
        routes.MapBookEndpoints();
        routes.MapAudioLibraryEndpoints();
        routes.MapAudioTrackEndpoints();
        routes.MapPeopleEndpoints();
        routes.MapStudioEndpoints();
        routes.MapTagEndpoints();
        routes.MapCollectionEndpoints();
        routes.MapJellyfinPlaybackEndpoints();
        routes.MapJobEndpoints();
        routes.MapSettingsEndpoints();
        routes.MapLibraryEndpoints();
        routes.MapFilesEndpoints();
        routes.MapUserStateEndpoints();
        routes.MapPluginEndpoints();
        routes.MapIdentifyEndpoints();
        routes.MapOrganizeEndpoints();

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
        if (explicitHide is { } hide) {
            return hide;
        }

        return !httpContext.Request.Cookies.TryGetValue(CookieName, out var mode) ||
            !string.Equals(mode, "show", StringComparison.OrdinalIgnoreCase);
    }
}
