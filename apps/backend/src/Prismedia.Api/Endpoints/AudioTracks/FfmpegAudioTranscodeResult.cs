using Prismedia.Application.Audio;
using Prismedia.Contracts.Media;

namespace Prismedia.Api.Endpoints;

/// <summary>
/// HTTP result that serves a live MP3 transcode of the planned audio stream. All media
/// tooling runs behind <see cref="IAudioTranscodeStreamer"/>; this result only shapes
/// the response.
/// </summary>
internal sealed class FfmpegAudioTranscodeResult(AudioStreamPlan stream) : IResult {
    public async Task ExecuteAsync(HttpContext httpContext) {
        var response = httpContext.Response;
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = MediaContentTypes.AudioMpeg;
        response.Headers.CacheControl = "no-store";
        response.Headers["X-Transcoded-From"] = stream.Codec ?? "unknown";

        if (HttpMethods.IsHead(httpContext.Request.Method)) {
            return;
        }

        var streamer = httpContext.RequestServices.GetRequiredService<IAudioTranscodeStreamer>();
        await streamer.StreamMp3Async(stream, response.Body, httpContext.RequestAborted);
    }
}
