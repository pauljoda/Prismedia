using Prismedia.Application.Audio;
using Prismedia.Contracts.System;

namespace Prismedia.Api.Endpoints;

/// <summary>
/// Jellyfin-compatible audio streaming endpoints used by music clients (Manet, Finamp, Symfonium,
/// the official Jellyfin apps). All three routes resolve the same source plan: a browser-/client-
/// native codec is served directly with range support, anything else is transcoded to MP3 on the fly.
/// Transcode-shaping query parameters (audioCodec, container, maxStreamingBitrate, …) are accepted
/// but not yet honored — direct-play or MP3 covers the common music-client matrix on a LAN.
/// </summary>
internal static class AudioStreamEndpoints {
    internal static IEndpointRouteBuilder MapJellyfinAudioStreamEndpoints(this IEndpointRouteBuilder routes) {
        // The endpoint most music clients use; negotiates direct-play vs. transcode server-side.
        Map(routes, "/Audio/{itemId:guid}/universal", "GetJellyfinAudioUniversal");
        Map(routes, "/Audio/{itemId:guid}/stream", "GetJellyfinAudioStream");
        // Some clients append the desired container as an extension (e.g. stream.mp3); ignored for now.
        Map(routes, "/Audio/{itemId:guid}/stream.{container}", "GetJellyfinAudioStreamContainer");

        // Manet (and other clients) download/play the original via /Items/{id}/File and /Download.
        MapFile(routes, "/Items/{itemId:guid}/File", "GetJellyfinItemFile");
        MapFile(routes, "/Items/{itemId:guid}/Download", "DownloadJellyfinItemFile");

        return routes;
    }

    private static void MapFile(IEndpointRouteBuilder routes, string pattern, string name) {
        routes.MapGet(pattern, StreamItemFileAsync)
            .WithName(name)
            .WithSummary("Get Jellyfin Item File.")
            .WithTags("Jellyfin Music")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status206PartialContent)
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        routes.MapMethods(pattern, [HttpMethods.Head], StreamItemFileAsync)
            .ExcludeFromDescription();
    }

    // Serves the original media file for an item: audio tracks via the audio source, falling back to
    // the video source so video items downloaded by Jellyfin clients also resolve.
    private static async Task<IResult> StreamItemFileAsync(
        Guid itemId,
        IAudioStreamService audio,
        Prismedia.Application.Videos.IVideoSourceService video,
        CancellationToken cancellationToken) {
        var stream = await audio.GetStreamAsync(itemId, cancellationToken);
        if (stream is not null) {
            return Results.File(File.OpenRead(stream.Path), stream.ContentType, enableRangeProcessing: true);
        }

        var source = await video.GetSourceAsync(itemId, cancellationToken);
        if (source is not null) {
            return Results.File(File.OpenRead(source.Path), source.ContentType, enableRangeProcessing: true);
        }

        return Results.NotFound(new ApiProblem(ApiProblemCodes.JellyfinItemFileNotFound, $"File for item '{itemId}' was not found."));
    }

    private static void Map(IEndpointRouteBuilder routes, string pattern, string name) {
        routes.MapGet(pattern, StreamAudioAsync)
            .WithName(name)
            .WithSummary("Get Jellyfin Audio Stream.")
            .WithTags("Jellyfin Music")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status206PartialContent)
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        routes.MapMethods(pattern, [HttpMethods.Head], StreamAudioAsync)
            .ExcludeFromDescription();
    }

    // container is bound (though unused) so the {container} token in the stream.{container} route is
    // described as a path parameter; without it the generated OpenAPI document is invalid.
    private static async Task<IResult> StreamAudioAsync(
        Guid itemId,
        IAudioStreamService streams,
        CancellationToken cancellationToken,
        string? container = null) {
        var stream = await streams.GetStreamAsync(itemId, cancellationToken);
        if (stream is null) {
            return Results.NotFound(new ApiProblem(ApiProblemCodes.JellyfinAudioNotFound, $"Audio stream '{itemId}' was not found."));
        }

        if (!stream.DirectPlayable) {
            return new FfmpegAudioTranscodeResult(stream);
        }

        return Results.File(File.OpenRead(stream.Path), stream.ContentType, enableRangeProcessing: true);
    }
}
