using Prismedia.Infrastructure.Media.Processing;
using Prismedia.Application.Jobs.Ports;

namespace Prismedia.Infrastructure.Media.Adapters;

/// <summary>
/// Adapts the Infrastructure MediaProbeService to the Application port interface.
/// </summary>
public sealed class MediaProbeAdapter(MediaProbeService inner) : IMediaProbe {
    public async Task<VideoProbeData?> ProbeVideoAsync(string filePath, CancellationToken cancellationToken) {
        var result = await inner.ProbeVideoAsync(filePath, cancellationToken);
        if (result is null) return null;
        return new VideoProbeData(result.DurationSeconds, result.FileSize, result.Width, result.Height,
            result.FrameRate, result.BitRate, result.Codec, result.Container,
            result.SampleRate, result.Channels, result.AudioCodec,
            result.Streams?.Select(stream => new MediaStreamProbeData(
                stream.StreamIndex,
                stream.Type,
                stream.Codec,
                stream.Language,
                stream.Title,
                stream.Width,
                stream.Height,
                stream.FrameRate,
                stream.BitRate,
                stream.SampleRate,
                stream.Channels,
                stream.IsDefault,
                stream.IsForced,
                stream.PixelFormat,
                stream.BitDepth,
                stream.ColorRange,
                stream.ColorSpace,
                stream.ColorTransfer,
                stream.ColorPrimaries,
                stream.DvProfile,
                stream.DvLevel,
                stream.RpuPresentFlag,
                stream.ElPresentFlag,
                stream.BlPresentFlag,
                stream.DvBlSignalCompatibilityId,
                stream.Hdr10PlusPresentFlag)).ToList());
    }

    public async Task<AudioProbeData?> ProbeAudioAsync(string filePath, CancellationToken cancellationToken) {
        var result = await inner.ProbeAudioAsync(filePath, cancellationToken);
        if (result is null) return null;
        return new AudioProbeData(result.DurationSeconds, result.FileSize, result.BitRate, result.Codec,
            result.Container, result.SampleRate, result.Channels,
            result.Artist, result.Album, result.Title, result.TrackNumber);
    }

    public async Task<ImageProbeData?> ProbeImageAsync(string filePath, CancellationToken cancellationToken) {
        var result = await inner.ProbeImageAsync(filePath, cancellationToken);
        if (result is null) return null;
        return new ImageProbeData(result.Width, result.Height, result.Codec);
    }

    public async Task<IReadOnlyList<SubtitleStreamData>> ProbeSubtitleStreamsAsync(
        string filePath, CancellationToken cancellationToken) {
        var results = await inner.ProbeSubtitleStreamsAsync(filePath, cancellationToken);
        return results.Select(s => new SubtitleStreamData(s.StreamIndex, s.CodecName, s.Language, s.Title)).ToList();
    }
}
