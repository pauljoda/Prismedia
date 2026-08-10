using Prismedia.Application.Playback;
using Prismedia.Contracts.Playback;
using Prismedia.Domain.Entities;

namespace Prismedia.Api.Endpoints;

public static class MusicPlayerEndpoints {
    public static RouteGroupBuilder MapMusicPlayerEndpoints(this IEndpointRouteBuilder routes) {
        var group = routes.MapGroup("/api/music-player")
            .WithTags("Playback");

        group.MapGet("/state", async (
            HttpContext httpContext,
            BrowserSessionService sessions,
            MusicPlayerStateService playerState,
            CancellationToken cancellationToken) => {
            var browserSession = await BrowserSessionHttp.EnsureAsync(httpContext, sessions, cancellationToken);
            return await playerState.GetAsync(browserSession.SessionId, cancellationToken);
        })
            .WithName("GetMusicPlayerState")
            .WithSummary("Gets the browser-scoped music player state.")
            .Produces<MusicPlayerStateResponse>();

        group.MapPut("/state", async (
            HttpContext httpContext,
            UpdateMusicPlayerStateRequest request,
            BrowserSessionService sessions,
            MusicPlayerStateService playerState,
            CancellationToken cancellationToken) => {
            var browserSession = await BrowserSessionHttp.EnsureAsync(httpContext, sessions, cancellationToken);
            await playerState.SaveAsync(browserSession.SessionId, request, cancellationToken);
            return Results.NoContent();
        })
            .WithName("UpdateMusicPlayerState")
            .WithSummary("Saves the browser-scoped music player state.")
            .Produces(StatusCodes.Status204NoContent);

        group.MapPatch("/state/progress", async (
            HttpContext httpContext,
            UpdateMusicPlayerProgressRequest request,
            BrowserSessionService sessions,
            MusicPlayerStateService playerState,
            CancellationToken cancellationToken) => {
            var browserSession = await BrowserSessionHttp.EnsureAsync(httpContext, sessions, cancellationToken);
            await playerState.UpdateProgressAsync(browserSession.SessionId, request, cancellationToken);
            return Results.NoContent();
        })
            .WithName("UpdateMusicPlayerProgress")
            .WithSummary("Updates progress for the persisted browser-scoped music queue.")
            .Produces(StatusCodes.Status204NoContent);

        group.MapDelete("/state", async (
            HttpContext httpContext,
            BrowserSessionService sessions,
            MusicPlayerStateService playerState,
            CancellationToken cancellationToken) => {
            var browserSession = await BrowserSessionHttp.EnsureAsync(httpContext, sessions, cancellationToken);
            await playerState.ClearAsync(browserSession.SessionId, cancellationToken);
            return Results.NoContent();
        })
            .WithName("ClearMusicPlayerState")
            .WithSummary("Clears the browser-scoped music player queue state.")
            .Produces(StatusCodes.Status204NoContent);

        group.MapPost("/diagnostics", (
            AudioPlaybackDiagnosticRequest request,
            ILoggerFactory loggerFactory) => {
            var logger = loggerFactory.CreateLogger("Prismedia.AudioPlaybackDiagnostics");
            logger.LogInformation(
                "Audio playback {Event} track={TrackId} position={PositionSeconds:F3}s duration={DurationSeconds} bufferedAhead={BufferedAheadSeconds:F3}s readyState={ReadyState} networkState={NetworkState} paused={Paused} ended={Ended} playIntent={PlayIntent} visible={DocumentVisible} focused={DocumentHasFocus} pauseSource={PauseSource} interruptionMs={InterruptionMilliseconds} mediaError={MediaErrorCode}",
                request.Event.ToCode(),
                request.TrackId,
                request.PositionSeconds,
                request.DurationSeconds,
                request.BufferedAheadSeconds,
                request.ReadyState,
                request.NetworkState,
                request.Paused,
                request.Ended,
                request.PlayIntent,
                request.DocumentVisible,
                request.DocumentHasFocus,
                request.PauseSource?.ToCode(),
                request.InterruptionMilliseconds,
                request.MediaErrorCode);
            return Results.NoContent();
        })
            .WithName("ReportAudioPlaybackDiagnostic")
            .WithSummary("Reports a browser audio lifecycle transition for intermittent-stall diagnostics.")
            .Produces(StatusCodes.Status204NoContent);

        return group;
    }
}
