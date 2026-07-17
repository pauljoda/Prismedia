namespace Prismedia.Application.Audio;

/// <summary>
/// Streams a live MP3 transcode of a source audio file into a caller-owned stream
/// (typically an HTTP response body). Implemented in Infrastructure over the shared
/// process executor so HTTP endpoints never spawn media tooling themselves.
/// </summary>
public interface IAudioTranscodeStreamer {
    /// <summary>
    /// Transcodes the planned stream's source file to stereo MP3 and writes the encoded
    /// bytes to <paramref name="output"/> as they are produced.
    /// </summary>
    /// <param name="stream">Resolved source file and ffmpeg location for this playback.</param>
    /// <param name="output">Destination stream; not disposed by the implementation.</param>
    /// <param name="cancellationToken">
    /// Cancels the transcode; client disconnects are expected and must not fault.
    /// </param>
    Task StreamMp3Async(AudioStreamPlan stream, Stream output, CancellationToken cancellationToken);
}
