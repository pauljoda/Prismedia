namespace Prismedia.Infrastructure.Media.Processing;

/// <summary>
/// Resolves the ffmpeg and ffprobe executables used by media probing, playback
/// transcodes, thumbnails, preview clips, trickplay images, subtitles, and waveforms.
/// When only ffmpeg is configured, ffprobe is resolved as the companion binary in the
/// same directory so custom builds such as Jellyfin FFmpeg are used consistently.
/// </summary>
/// <param name="FfmpegPath">Executable name or absolute path for ffmpeg.</param>
/// <param name="ConfiguredFfprobePath">Optional executable name or absolute path for ffprobe.</param>
public sealed record MediaToolOptions(
    string FfmpegPath = "ffmpeg",
    string? ConfiguredFfprobePath = null) {
    private const string DefaultFfmpegName = "ffmpeg";
    private const string JellyfinFfmpegName = "jellyfin-ffmpeg";

    /// <summary>
    /// Gets the ffprobe executable that belongs with <see cref="FfmpegPath" /> unless
    /// an explicit ffprobe path was configured.
    /// </summary>
    public string FfprobePath => ResolveFfprobePath(FfmpegPath, ConfiguredFfprobePath);

    /// <summary>
    /// Creates tool options from configuration, preferring a Jellyfin FFmpeg command on
    /// <c>PATH</c> when no explicit ffmpeg path was supplied.
    /// </summary>
    /// <param name="configuredFfmpegPath">Optional configured ffmpeg command or absolute path.</param>
    /// <param name="configuredFfprobePath">Optional configured ffprobe command or absolute path.</param>
    /// <returns>Resolved media tool options for runtime services.</returns>
    public static MediaToolOptions FromConfiguration(string? configuredFfmpegPath, string? configuredFfprobePath) =>
        FromConfiguration(configuredFfmpegPath, configuredFfprobePath, FindExecutableOnPath);

    internal static MediaToolOptions FromConfiguration(
        string? configuredFfmpegPath,
        string? configuredFfprobePath,
        Func<string, string?> pathResolver) {
        var ffmpegPath = string.IsNullOrWhiteSpace(configuredFfmpegPath)
            ? pathResolver(JellyfinFfmpegName) ?? DefaultFfmpegName
            : configuredFfmpegPath.Trim();

        return new MediaToolOptions(ffmpegPath, configuredFfprobePath);
    }

    /// <summary>
    /// Resolves ffprobe from an explicit override or by replacing the configured
    /// ffmpeg executable name with ffprobe while preserving its directory.
    /// </summary>
    /// <param name="ffmpegPath">Executable name or absolute path for ffmpeg.</param>
    /// <param name="configuredFfprobePath">Optional executable name or absolute path for ffprobe.</param>
    /// <returns>The executable name or absolute path to use for ffprobe.</returns>
    public static string ResolveFfprobePath(string? ffmpegPath, string? configuredFfprobePath = null) {
        if (!string.IsNullOrWhiteSpace(configuredFfprobePath)) {
            return configuredFfprobePath.Trim();
        }

        var normalizedFfmpeg = string.IsNullOrWhiteSpace(ffmpegPath) ? "ffmpeg" : ffmpegPath.Trim();
        var fileName = Path.GetFileName(normalizedFfmpeg);
        var probeName = ResolveFfprobeFileName(fileName);
        var directory = Path.GetDirectoryName(normalizedFfmpeg);
        return string.IsNullOrWhiteSpace(directory) ? probeName : Path.Combine(directory, probeName);
    }

    private static string ResolveFfprobeFileName(string fileName) {
        var ffmpegIndex = fileName.LastIndexOf("ffmpeg", StringComparison.OrdinalIgnoreCase);
        if (ffmpegIndex >= 0) {
            return string.Concat(
                fileName.AsSpan(0, ffmpegIndex),
                "ffprobe",
                fileName.AsSpan(ffmpegIndex + "ffmpeg".Length));
        }

        return fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? "ffprobe.exe" : "ffprobe";
    }

    private static string? FindExecutableOnPath(string executableName) {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path)) {
            return null;
        }

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
            var candidate = Path.Combine(directory, executableName);
            if (File.Exists(candidate)) {
                return executableName;
            }
        }

        return null;
    }
}
