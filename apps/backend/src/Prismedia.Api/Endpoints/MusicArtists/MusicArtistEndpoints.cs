using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Media;

namespace Prismedia.Api.Endpoints;

public static class MusicArtistEndpoints {
    public static RouteGroupBuilder MapMusicArtistEndpoints(this IEndpointRouteBuilder routes) =>
        routes.MapEntityKindRoutes(
            "/api/music-artists",
            "music-artist",
            "Artists",
            "ListMusicArtists",
            "GetMusicArtist",
            typeof(EntityListResponse),
            typeof(MusicArtistDetail));
}
