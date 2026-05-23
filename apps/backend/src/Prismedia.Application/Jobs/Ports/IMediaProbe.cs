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
    IReadOnlyList<MediaStreamProbeData>? Streams = null);

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
    string? TrackNumber);

public sealed record ImageProbeData(int Width, int Height, string? Codec);

public sealed record SubtitleStreamData(int StreamIndex, string CodecName, string Language, string? Title);
