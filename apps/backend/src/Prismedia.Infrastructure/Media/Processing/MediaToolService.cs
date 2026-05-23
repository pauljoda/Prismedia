using Prismedia.Infrastructure.Processes;

namespace Prismedia.Infrastructure.Media.Processing;

/// <summary>
/// Checks whether required media binaries are available to the backend process.
/// </summary>
public sealed class MediaToolService {
    private readonly ProcessExecutor _processExecutor;
    private readonly MediaToolOptions _toolOptions;

    /// <summary>
    /// Creates the media tool checker.
    /// </summary>
    /// <param name="processExecutor">Process runner used to invoke media binaries.</param>
    /// <param name="toolOptions">Configured media tool paths.</param>
    public MediaToolService(ProcessExecutor processExecutor, MediaToolOptions? toolOptions = null) {
        _processExecutor = processExecutor;
        _toolOptions = toolOptions ?? new MediaToolOptions();
    }

    /// <summary>
    /// Checks for ffmpeg and ffprobe and returns availability plus version hints.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel process checks.</param>
    /// <returns>Current media tool availability status.</returns>
    public async Task<MediaToolStatus> CheckAsync(CancellationToken cancellationToken) {
        var ffmpeg = await CheckToolAsync(_toolOptions.FfmpegPath, cancellationToken);
        var ffprobe = await CheckToolAsync(_toolOptions.FfprobePath, cancellationToken);

        return new MediaToolStatus(
            ffmpeg.Available,
            ffprobe.Available,
            ffmpeg.Version,
            ffprobe.Version);
    }

    private async Task<(bool Available, string? Version)> CheckToolAsync(
        string fileName,
        CancellationToken cancellationToken) {
        try {
            var result = await _processExecutor.RunAsync(
                fileName,
                ["-version"],
                null,
                cancellationToken);
            var firstLine = result.StandardOutput
                .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault();

            return (result.ExitCode == 0, firstLine);
        } catch {
            return (false, null);
        }
    }
}
