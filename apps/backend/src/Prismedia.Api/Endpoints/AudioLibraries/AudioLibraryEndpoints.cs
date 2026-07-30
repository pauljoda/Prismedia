using Prismedia.Contracts.Entities;
using Prismedia.Domain.Entities;

namespace Prismedia.Api.Endpoints;

public static class AudioLibraryEndpoints {
    public static RouteGroupBuilder MapAudioLibraryEndpoints(this IEndpointRouteBuilder routes) =>
        routes.MapEntityKindRoutes(
            "/api/audio-libraries",
            EntityKind.AudioLibrary.ToCode(),
            "Audio",
            "ListAudioLibraries",
            "GetAudioLibrary");
}
