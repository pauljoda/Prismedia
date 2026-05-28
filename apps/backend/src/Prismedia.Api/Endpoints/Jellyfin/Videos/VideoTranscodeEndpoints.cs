using Prismedia.Application.Videos;

namespace Prismedia.Api.Endpoints;

internal static class VideoTranscodeEndpoints {
    internal static IEndpointRouteBuilder MapJellyfinVideoTranscodeEndpoints(this IEndpointRouteBuilder routes) {
        routes.MapDelete("/Videos/ActiveEncodings", async (
            ITranscodeSessionService transcodes,
            CancellationToken cancellationToken) => {
                var killed = await transcodes.CancelAllAsync(cancellationToken);
                return Results.Ok(new { killed });
            })
            .WithName("DeleteJellyfinActiveEncodings")
            .WithSummary("Delete Jellyfin Active Encodings.")
            .WithTags("Jellyfin Videos");

        return routes;
    }
}
