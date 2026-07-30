using Prismedia.Contracts.Entities;
using Prismedia.Domain.Entities;

namespace Prismedia.Api.Endpoints;

public static class AudioLibraryEndpoints {
    public static RouteGroupBuilder MapAudioLibraryEndpoints(this IEndpointRouteBuilder routes) =>
        routes.MapEntityKindRoutes(
            "/api/audio-libraries",
            EntityKindRegistry.AudioLibrary.Code,
            "Audio",
            "ListAudioLibraries",
            "GetAudioLibrary");
}
