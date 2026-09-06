using Microsoft.Extensions.Logging;
using Prismedia.Application.Acquisition;
using Prismedia.Infrastructure.Media.Processing;
using Prismedia.Infrastructure.Processes;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>Fully decodes acquired video and audio at background priority before any library mutation.</summary>
public sealed class VideoPayloadVerifier(ProcessExecutor processes, MediaToolOptions tools,
    ILogger<VideoPayloadVerifier> logger) : IVideoPayloadVerifier {
    private static readonly TimeSpan VerificationLimit = TimeSpan.FromHours(6);

    /// <inheritdoc />
    public async Task<string?> FindFailureAsync(string filePath, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(VerificationLimit);
        try {
            var before = new FileInfo(filePath);
            if (!before.Exists || before.Length == 0) return "The downloaded video is missing or empty. The download was preserved for review.";
            var length = before.Length;
            var modified = before.LastWriteTimeUtc;
            var result = await processes.RunAsync(tools.FfmpegPath,
                ["-nostdin", "-hide_banner", "-v", DecodeProtocol.ErrorLevel, "-xerror",
                    "-abort_on", DecodeProtocol.EmptyOutputStream, "-threads", "1", "-filter_threads", "1",
                    "-filter_complex_threads", "1", "-i", filePath,
                    "-map", DecodeProtocol.VideoStreams, "-map", DecodeProtocol.AudioStreams,
                    "-threads", "1", "-f", DecodeProtocol.NullMuxer, "-"],
                null, deadline.Token, lowPriority: true);
            if (result.ExitCode != 0) {
                logger.LogWarning("Downloaded video verification failed for {Path}: {Diagnostic}", filePath, result.StandardError);
                return "The downloaded video could not be decoded completely. The download was preserved for review.";
            }
            var after = new FileInfo(filePath);
            if (!after.Exists || after.Length != length || after.LastWriteTimeUtc != modified) {
                return "The downloaded video changed during verification. Retry after the download has settled.";
            }
            return null;
        } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            throw;
        } catch (OperationCanceledException) {
            return "Full video verification exceeded its time limit. The download was preserved for review.";
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException) {
            logger.LogWarning(ex, "Could not finish downloaded video verification for {Path}.", filePath);
            return "Full video verification could not finish. The download was preserved for review.";
        }
    }

    private static class DecodeProtocol {
        public const string ErrorLevel = "error";
        public const string EmptyOutputStream = "empty_output_stream";
        public const string VideoStreams = "0:V";
        public const string AudioStreams = "0:a?";
        public const string NullMuxer = "null";
    }
}
