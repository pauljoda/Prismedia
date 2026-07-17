using Microsoft.Extensions.Logging;
using Prismedia.Application.Audio;
using Prismedia.Contracts.Media;
using Prismedia.Infrastructure.Processes;

namespace Prismedia.Infrastructure.Audio;

/// <summary>
/// ffmpeg-backed <see cref="IAudioTranscodeStreamer"/> that pipes a stereo MP3 encode of
/// the source file straight to the caller's stream via the shared process executor.
/// </summary>
public sealed class FfmpegAudioTranscodeStreamer(
    ProcessExecutor processes,
    ILogger<FfmpegAudioTranscodeStreamer> logger) : IAudioTranscodeStreamer {
    /// <inheritdoc />
    public async Task StreamMp3Async(AudioStreamPlan stream, Stream output, CancellationToken cancellationToken) {
        try {
            var result = await processes.RunToStreamAsync(
                stream.FfmpegPath,
                [
                    "-hide_banner",
                    "-loglevel", "error",
                    "-i", stream.Path,
                    "-vn",
                    "-acodec", MediaCodecs.LibMp3LameEncoder,
                    "-b:a", "192k",
                    "-ar", "44100",
                    "-ac", "2",
                    "-f", MediaCodecs.Mp3,
                    "pipe:1",
                ],
                environment: null,
                output,
                cancellationToken);

            if (result.ExitCode != 0) {
                logger.LogWarning("Audio transcode failed for {Path}: {Error}", stream.Path, result.StandardError);
            }
        } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            // Client disconnected mid-stream; the executor already killed the process.
        }
    }
}
