using System.Diagnostics;
using Prismedia.Application.Audio;
using Prismedia.Application.Entities;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Media;
using Prismedia.Contracts.System;
using Prismedia.Infrastructure.Media.Processing;

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
            EntityEndpointResults.ToResult(id, await capabilities.UpdatePlaybackAsync(
                id, resumeSeconds: 0, durationSeconds: null, completed: true, cancellationToken)))
            .WithName("RecordAudioTrackPlay")
            .WithTags("Audio")
            .Produces<EntityCard>()
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        routes.MapGet("/api/audio-stream/{id:guid}", StreamAudioAsync)
            .WithName("GetAudioStream")
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
        IAudioSourceService sourceFiles,
        MediaToolOptions mediaTools,
        CancellationToken cancellationToken) {
        var source = await sourceFiles.GetSourceAsync(id, cancellationToken);
        if (source is null) {
            return Results.NotFound(new ApiProblem("audio_stream_not_found", $"Audio stream '{id}' was not found."));
        }

        if (!source.DirectPlayable) {
            return new FfmpegAudioTranscodeResult(source, mediaTools.FfmpegPath);
        }

        return Results.File(File.OpenRead(source.Path), source.ContentType, enableRangeProcessing: true);
    }

    private sealed class FfmpegAudioTranscodeResult : IResult {
        private readonly AudioSourceFile _source;
        private readonly string _ffmpegPath;

        public FfmpegAudioTranscodeResult(AudioSourceFile source, string ffmpegPath) {
            _source = source;
            _ffmpegPath = ffmpegPath;
        }

        public async Task ExecuteAsync(HttpContext httpContext) {
            var response = httpContext.Response;
            response.StatusCode = StatusCodes.Status200OK;
            response.ContentType = "audio/mpeg";
            response.Headers.CacheControl = "no-store";
            response.Headers["X-Transcoded-From"] = _source.Codec ?? "unknown";

            if (HttpMethods.IsHead(httpContext.Request.Method)) {
                return;
            }

            using var process = new Process {
                StartInfo = new ProcessStartInfo {
                    FileName = _ffmpegPath,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false
                },
                EnableRaisingEvents = true
            };
            process.StartInfo.ArgumentList.Add("-hide_banner");
            process.StartInfo.ArgumentList.Add("-loglevel");
            process.StartInfo.ArgumentList.Add("error");
            process.StartInfo.ArgumentList.Add("-i");
            process.StartInfo.ArgumentList.Add(_source.Path);
            process.StartInfo.ArgumentList.Add("-vn");
            process.StartInfo.ArgumentList.Add("-acodec");
            process.StartInfo.ArgumentList.Add("libmp3lame");
            process.StartInfo.ArgumentList.Add("-b:a");
            process.StartInfo.ArgumentList.Add("192k");
            process.StartInfo.ArgumentList.Add("-ar");
            process.StartInfo.ArgumentList.Add("44100");
            process.StartInfo.ArgumentList.Add("-ac");
            process.StartInfo.ArgumentList.Add("2");
            process.StartInfo.ArgumentList.Add("-f");
            process.StartInfo.ArgumentList.Add("mp3");
            process.StartInfo.ArgumentList.Add("pipe:1");

            process.Start();

            var stderrTask = process.StandardError.ReadToEndAsync(httpContext.RequestAborted);
            try {
                await process.StandardOutput.BaseStream.CopyToAsync(response.Body, httpContext.RequestAborted);
                await process.WaitForExitAsync(httpContext.RequestAborted);
                var stderr = await stderrTask;
                if (process.ExitCode != 0) {
                    httpContext.RequestServices
                        .GetRequiredService<ILogger<FfmpegAudioTranscodeResult>>()
                        .LogWarning("Audio transcode failed for {Path}: {Error}", _source.Path, stderr);
                }
            } catch (OperationCanceledException) when (httpContext.RequestAborted.IsCancellationRequested) {
                if (!process.HasExited) {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(CancellationToken.None);
                }
            }
        }
    }
}
