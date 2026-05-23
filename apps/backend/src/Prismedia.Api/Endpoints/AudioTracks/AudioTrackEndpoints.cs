using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Media;

namespace Prismedia.Api.Endpoints;

public static class AudioTrackEndpoints {
    public static RouteGroupBuilder MapAudioTrackEndpoints(this IEndpointRouteBuilder routes) =>
        routes.MapEntityKindRoutes(
            "/api/audio-tracks",
            "audio-track",
            "Audio",
            "ListAudioTracks",
            "GetAudioTrack",
            typeof(EntityListResponse),
            typeof(AudioTrackDetail));
}
