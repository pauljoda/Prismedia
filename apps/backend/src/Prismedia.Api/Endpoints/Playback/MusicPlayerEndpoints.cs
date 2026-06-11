using Prismedia.Application.Playback;
using Prismedia.Contracts.Playback;

namespace Prismedia.Api.Endpoints;

public static class MusicPlayerEndpoints {
    public static RouteGroupBuilder MapMusicPlayerEndpoints(this IEndpointRouteBuilder routes) {
        var group = routes.MapGroup("/api/music-player")
            .WithTags("Playback");

        group.MapGet("/state", async (
            MusicPlayerStateService playerState,
            CancellationToken cancellationToken) =>
            await playerState.GetAsync(cancellationToken))
            .WithName("GetMusicPlayerState")
            .WithSummary("Gets the server-persisted music player state.")
            .Produces<MusicPlayerStateResponse>();

        group.MapPut("/state", async (
            UpdateMusicPlayerStateRequest request,
            MusicPlayerStateService playerState,
            CancellationToken cancellationToken) =>
            await playerState.SaveAsync(request, cancellationToken))
            .WithName("UpdateMusicPlayerState")
            .WithSummary("Saves the server-persisted music player state.")
            .Produces<MusicPlayerStateResponse>();

        group.MapDelete("/state", async (
            MusicPlayerStateService playerState,
            CancellationToken cancellationToken) => {
            await playerState.ClearAsync(cancellationToken);
            return Results.NoContent();
        })
            .WithName("ClearMusicPlayerState")
            .WithSummary("Clears the server-persisted music player state.")
            .Produces(StatusCodes.Status204NoContent);

        return group;
    }
}
