using System.Diagnostics;
using Prismedia.Application.Audio;
using Prismedia.Contracts.Media;

namespace Prismedia.Api.Endpoints;

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

        using var process = new Process {
            StartInfo = new ProcessStartInfo {
                FileName = stream.FfmpegPath,
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
        process.StartInfo.ArgumentList.Add(stream.Path);
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
                    .LogWarning("Audio transcode failed for {Path}: {Error}", stream.Path, stderr);
            }
        } catch (OperationCanceledException) when (httpContext.RequestAborted.IsCancellationRequested) {
            if (!process.HasExited) {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }
    }
}
