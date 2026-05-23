namespace Prismedia.Infrastructure.Media.Processing;

/// <summary>
/// Availability and version hints for media command-line tools used by workers.
/// </summary>
/// <param name="FfmpegAvailable">Whether ffmpeg executed successfully.</param>
/// <param name="FfprobeAvailable">Whether ffprobe executed successfully.</param>
/// <param name="FfmpegVersion">First ffmpeg version output line, when available.</param>
/// <param name="FfprobeVersion">First ffprobe version output line, when available.</param>
public sealed record MediaToolStatus(
    bool FfmpegAvailable,
    bool FfprobeAvailable,
    string? FfmpegVersion,
    string? FfprobeVersion);
