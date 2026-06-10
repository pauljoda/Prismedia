using Prismedia.Application.Audio;
using Prismedia.Application.Entities;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Media;
using Prismedia.Contracts.System;

namespace Prismedia.Api.Endpoints;

public static class AudioTrackEndpoints {
    public static RouteGroupBuilder MapAudioTrackEndpoints(this IEndpointRouteBuilder routes) {
        var group = routes.MapEntityKindRoutes(
            "/api/audio-tracks",
            "audio-track",
            "Audio",
            "ListAudioTracks",
            "GetAudioTrack",
            typeof(EntityListResponse),
            typeof(AudioTrackDetail));

        group.MapPost("/{id:guid}/play", async (
            Guid id,
            EntityCapabilityService capabilities,
            CancellationToken cancellationToken) =>
            EntityEndpointResults.ToResult(id, await capabilities.RecordCompletedPlaybackAsync(id, cancellationToken)))
            .WithName("RecordAudioTrackPlay")
            .WithSummary("Record Audio Track Play.")
            .WithTags("Audio")
            .Produces<EntityCard>()
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        routes.MapGet("/api/audio-stream/{id:guid}", StreamAudioAsync)
            .WithName("GetAudioStream")
            .WithSummary("Get Audio Stream.")
            .WithTags("Audio")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status206PartialContent)
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        routes.MapMethods("/api/audio-stream/{id:guid}", [HttpMethods.Head], StreamAudioAsync)
            .ExcludeFromDescription();

        return group;
    }

    private static async Task<IResult> StreamAudioAsync(
        Guid id,
        IAudioStreamService streams,
        CancellationToken cancellationToken) {
        var stream = await streams.GetStreamAsync(id, cancellationToken);
        if (stream is null) {
            return Results.NotFound(new ApiProblem(ApiProblemCodes.AudioStreamNotFound, $"Audio stream '{id}' was not found."));
        }

        if (!stream.DirectPlayable) {
            return new FfmpegAudioTranscodeResult(stream);
        }

        return Results.File(File.OpenRead(stream.Path), stream.ContentType, enableRangeProcessing: true);
    }
}
