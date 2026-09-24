namespace Prismedia.Application.Jobs.Ports;

/// <summary>
/// Port for probing media files for technical metadata.
/// </summary>
public interface IMediaProbe {
    Task<VideoProbeData?> ProbeVideoAsync(string filePath, CancellationToken cancellationToken);
    Task<AudioProbeData?> ProbeAudioAsync(string filePath, CancellationToken cancellationToken);
    Task<ImageProbeData?> ProbeImageAsync(string filePath, CancellationToken cancellationToken);
    Task<IReadOnlyList<SubtitleStreamData>> ProbeSubtitleStreamsAsync(string filePath, CancellationToken cancellationToken);
}

public sealed record VideoProbeData(
    double? DurationSeconds,
    long? FileSize,
    int? Width,
    int? Height,
    double? FrameRate,
    int? BitRate,
    string? Codec,
    string? Container,
    int? SampleRate,
    int? Channels,
    string? AudioCodec,
    IReadOnlyList<MediaStreamProbeData>? Streams = null) {
    /// <summary>Text subtitle streams from the same video probe; null means the adapter did not provide this inventory.</summary>
    public IReadOnlyList<SubtitleStreamData>? SubtitleStreams { get; init; }
}

public sealed record AudioProbeData(
    double? DurationSeconds,
    long? FileSize,
    int? BitRate,
    string? Codec,
    string? Container,
    int? SampleRate,
    int? Channels,
    string? Artist,
    string? Album,
    string? Title,
    string? TrackNumber,
    IReadOnlyList<AudioChapterProbeData>? Chapters = null);

/// <summary>One ordered embedded chapter window parsed from an audiobook container.</summary>
/// <param name="Index">Chapter index reported by the container.</param>
/// <param name="Title">Declared title, or a display placeholder when <paramref name="Untitled"/>.</param>
/// <param name="StartSeconds">Chapter start.</param>
/// <param name="EndSeconds">Chapter end.</param>
/// <param name="Untitled">Whether the container declared no title, so the title never proves chapter identity.</param>
public sealed record AudioChapterProbeData(
    int Index,
    string Title,
    double StartSeconds,
    double EndSeconds,
    bool Untitled = false);

public sealed record ImageProbeData(int Width, int Height, string? Codec);

public sealed record SubtitleStreamData(int StreamIndex, string CodecName, string Language, string? Title);
