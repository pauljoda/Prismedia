using Prismedia.Contracts.Entities;
using Prismedia.Domain.Entities;

namespace Prismedia.Api.Endpoints;

public static class MusicArtistEndpoints {
    public static RouteGroupBuilder MapMusicArtistEndpoints(this IEndpointRouteBuilder routes) =>
        routes.MapEntityKindRoutes(
            "/api/music-artists",
            EntityKind.MusicArtist.ToCode(),
            "Artists",
            "ListMusicArtists",
            "GetMusicArtist");
}
