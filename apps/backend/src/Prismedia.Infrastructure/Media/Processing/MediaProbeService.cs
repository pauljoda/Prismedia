using System.Text.Json;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Processes;

namespace Prismedia.Infrastructure.Media.Processing;

/// <summary>
/// Probes media files via ffprobe and parses technical metadata from the JSON output.
/// </summary>
public sealed class MediaProbeService {
    private readonly ProcessExecutor _processExecutor;
    private readonly MediaToolOptions _toolOptions;

    public MediaProbeService(ProcessExecutor processExecutor, MediaToolOptions? toolOptions = null) {
        _processExecutor = processExecutor;
        _toolOptions = toolOptions ?? new MediaToolOptions();
    }

    /// <summary>
    /// Probes a video file for duration, dimensions, codec, bitrate, and container info.
    /// </summary>
    public async Task<VideoProbeResult?> ProbeVideoAsync(
        string filePath,
        CancellationToken cancellationToken,
        string? ffprobePath = null) {
        var result = await RunFfprobeAsync(
            ["-v", "error",
             "-show_entries", "format=duration,size,bit_rate,format_name:stream=index,codec_type,codec_name,pix_fmt,width,height,avg_frame_rate,bit_rate,sample_rate,channels,color_range,color_space,color_transfer,color_primaries:stream_side_data=side_data_type,dv_profile,dv_level,rpu_present_flag,el_present_flag,bl_present_flag,dv_bl_signal_compatibility_id:stream_tags=language,title:stream_disposition=default,forced",
             "-of", "json",
             filePath],
            cancellationToken,
            ffprobePath);

        if (result is null)
            return null;

        var format = result.RootElement.GetPropertyOrDefault("format");
        var streams = result.RootElement.GetPropertyOrDefault("streams");

        JsonElement? videoStream = null;
        JsonElement? audioStream = null;
        var streamResults = new List<MediaStreamProbeResult>();

        if (streams.ValueKind == JsonValueKind.Array) {
            foreach (var stream in streams.EnumerateArray()) {
                // prism-vocab: external — ffprobe codec_type values decoded at this boundary only.
                var codecType = stream.GetStringOrDefault("codec_type");
                if (codecType == "video" && videoStream is null)
                    videoStream = stream;
                else if (codecType == "audio" && audioStream is null)
                    audioStream = stream;

                if (codecType is not ("video" or "audio"))
                    continue;

                var tags = stream.GetPropertyOrDefault("tags");
                var disposition = stream.GetPropertyOrDefault("disposition");
                var sideData = ParseVideoSideData(stream);
                var pixelFormat = stream.GetStringOrDefault("pix_fmt");
                streamResults.Add(new MediaStreamProbeResult(
                    stream.GetIntOrDefault("index") ?? 0,
                    codecType == "video" ? StreamKind.Video.ToCode() : StreamKind.Audio.ToCode(),
                    stream.GetStringOrDefault("codec_name"),
                    tags.GetStringOrDefault("language"),
                    tags.GetStringOrDefault("title"),
                    stream.GetIntOrDefault("width"),
                    stream.GetIntOrDefault("height"),
                    ParseFrameRate(stream.GetStringOrDefault("avg_frame_rate")),
                    stream.GetIntOrDefault("bit_rate"),
                    stream.GetIntOrDefault("sample_rate"),
                    stream.GetIntOrDefault("channels"),
                    disposition.GetIntOrDefault("default") == 1,
                    disposition.GetIntOrDefault("forced") == 1,
                    PixelFormat: pixelFormat,
                    BitDepth: ParseBitDepth(pixelFormat),
                    ColorRange: stream.GetStringOrDefault("color_range"),
                    ColorSpace: stream.GetStringOrDefault("color_space"),
                    ColorTransfer: stream.GetStringOrDefault("color_transfer"),
                    ColorPrimaries: stream.GetStringOrDefault("color_primaries"),
                    DvProfile: sideData.DvProfile,
                    DvLevel: sideData.DvLevel,
                    RpuPresentFlag: sideData.RpuPresentFlag,
                    ElPresentFlag: sideData.ElPresentFlag,
                    BlPresentFlag: sideData.BlPresentFlag,
                    DvBlSignalCompatibilityId: sideData.DvBlSignalCompatibilityId,
                    Hdr10PlusPresentFlag: sideData.Hdr10PlusPresentFlag));
            }
        }

        var duration = format.GetDoubleOrDefault("duration");
        var fileSize = format.GetLongOrDefault("size");
        var bitRate = format.GetIntOrDefault("bit_rate");
        var formatName = format.GetStringOrDefault("format_name");
        var container = formatName?.Split(',').FirstOrDefault() ?? Path.GetExtension(filePath).TrimStart('.');

        int? width = null, height = null;
        double? frameRate = null;
        string? codec = null;

        if (videoStream is { } vs) {
            width = vs.GetIntOrDefault("width");
            height = vs.GetIntOrDefault("height");
            codec = vs.GetStringOrDefault("codec_name");
            frameRate = ParseFrameRate(vs.GetStringOrDefault("avg_frame_rate"));
        }

        int? sampleRate = null, channels = null;
        string? audioCodec = null;

        if (audioStream is { } audio) {
            sampleRate = audio.GetIntOrDefault("sample_rate");
            channels = audio.GetIntOrDefault("channels");
            audioCodec = audio.GetStringOrDefault("codec_name");
        }

        return new VideoProbeResult(
            duration, fileSize, width, height, frameRate, bitRate, codec, container,
            sampleRate, channels, audioCodec, streamResults);
    }

    /// <summary>
    /// Probes an audio file for duration, codec, bitrate, sample rate, channels, and embedded tags.
    /// </summary>
    public async Task<AudioProbeResult?> ProbeAudioAsync(
        string filePath,
        CancellationToken cancellationToken,
        string? ffprobePath = null) {
        var result = await RunFfprobeAsync(
            ["-v", "error",
             "-show_entries", "format=duration,size,bit_rate,format_name:format_tags=artist,album,title,track:stream=codec_name,sample_rate,channels",
             "-of", "json",
             filePath],
            cancellationToken,
            ffprobePath);

        if (result is null)
            return null;

        var format = result.RootElement.GetPropertyOrDefault("format");
        var streams = result.RootElement.GetPropertyOrDefault("streams");
        var tags = format.GetPropertyOrDefault("tags");

        var duration = format.GetDoubleOrDefault("duration");
        var fileSize = format.GetLongOrDefault("size");
        var bitRate = format.GetIntOrDefault("bit_rate");
        var formatName = format.GetStringOrDefault("format_name");
        var container = formatName?.Split(',').FirstOrDefault() ?? Path.GetExtension(filePath).TrimStart('.');

        string? codec = null;
        int? sampleRate = null, channels = null;

        if (streams.ValueKind == JsonValueKind.Array) {
            foreach (var stream in streams.EnumerateArray()) {
                codec ??= stream.GetStringOrDefault("codec_name");
                sampleRate ??= stream.GetIntOrDefault("sample_rate");
                channels ??= stream.GetIntOrDefault("channels");
            }
        }

        var artist = tags.GetStringOrDefault("artist") ?? tags.GetStringOrDefault("ARTIST");
        var album = tags.GetStringOrDefault("album") ?? tags.GetStringOrDefault("ALBUM");
        var title = tags.GetStringOrDefault("title") ?? tags.GetStringOrDefault("TITLE");
        var track = tags.GetStringOrDefault("track") ?? tags.GetStringOrDefault("TRACK");

        return new AudioProbeResult(
            duration, fileSize, bitRate, codec, container, sampleRate, channels,
            artist, album, title, track);
    }

    /// <summary>
    /// Probes for subtitle streams in a video file, returning stream metadata.
    /// </summary>
    public async Task<IReadOnlyList<SubtitleStreamInfo>> ProbeSubtitleStreamsAsync(
        string filePath,
        CancellationToken cancellationToken,
        string? ffprobePath = null) {
        var result = await RunFfprobeAsync(
            ["-v", "error",
             "-select_streams", "s",
             "-show_entries", "stream=index,codec_name,codec_type:stream_tags=language,title",
             "-of", "json",
             filePath],
            cancellationToken,
            ffprobePath);

        if (result is null)
            return [];

        var streams = result.RootElement.GetPropertyOrDefault("streams");
        if (streams.ValueKind != JsonValueKind.Array)
            return [];

        var imageBased = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "pgs", "vobsub", "dvb_subtitle", "hdmv_pgs_subtitle", "xsub", "dvd_subtitle"
        };

        var results = new List<SubtitleStreamInfo>();

        foreach (var stream in streams.EnumerateArray()) {
            var codecName = stream.GetStringOrDefault("codec_name") ?? "";
            if (imageBased.Contains(codecName))
                continue;

            var index = stream.GetIntOrDefault("index") ?? 0;
            var streamTags = stream.GetPropertyOrDefault("tags");
            var language = streamTags.GetStringOrDefault("language") ?? "und";
            var title = streamTags.GetStringOrDefault("title");

            results.Add(new SubtitleStreamInfo(index, codecName, language, title));
        }

        return results;
    }

    /// <summary>
    /// Probes an image file for dimensions and codec.
    /// </summary>
    public async Task<ImageProbeResult?> ProbeImageAsync(
        string filePath,
        CancellationToken cancellationToken,
        string? ffprobePath = null) {
        var result = await RunFfprobeAsync(
            ["-v", "error",
             "-select_streams", "v:0",
             "-show_entries", "stream=width,height,codec_name",
             "-of", "json",
             filePath],
            cancellationToken,
            ffprobePath);

        if (result is null)
            return null;

        var streams = result.RootElement.GetPropertyOrDefault("streams");
        if (streams.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var stream in streams.EnumerateArray()) {
            var width = stream.GetIntOrDefault("width");
            var height = stream.GetIntOrDefault("height");
            var codec = stream.GetStringOrDefault("codec_name");
            if (width is not null && height is not null)
                return new ImageProbeResult(width.Value, height.Value, codec);
        }

        return null;
    }

    private async Task<JsonDocument?> RunFfprobeAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        string? ffprobePath = null) {
        try {
            var result = await _processExecutor.RunAsync(
                string.IsNullOrWhiteSpace(ffprobePath) ? _toolOptions.FfprobePath : ffprobePath.Trim(),
                arguments,
                null,
                cancellationToken);
            if (result.ExitCode != 0)
                return null;

            return JsonDocument.Parse(result.StandardOutput);
        } catch (JsonException) {
            return null;
        } catch (InvalidOperationException) {
            return null;
        }
    }

    private static double? ParseFrameRate(string? value) {
        if (string.IsNullOrEmpty(value))
            return null;

        var parts = value.Split('/');
        if (parts.Length == 2
            && double.TryParse(parts[0], out var num)
            && double.TryParse(parts[1], out var den)
            && den > 0) {
            return Math.Round(num / den, 2);
        }

        return double.TryParse(value, out var flat) ? flat : null;
    }

    private static VideoSideData ParseVideoSideData(JsonElement stream) {
        var sideDataList = stream.GetPropertyOrDefault("side_data_list");
        if (sideDataList.ValueKind != JsonValueKind.Array) {
            return new VideoSideData();
        }

        var result = new VideoSideData();
        foreach (var sideData in sideDataList.EnumerateArray()) {
            var sideDataType = sideData.GetStringOrDefault("side_data_type");
            if (string.Equals(sideDataType, "DOVI configuration record", StringComparison.OrdinalIgnoreCase)) {
                result = result with {
                    DvProfile = sideData.GetIntOrDefault("dv_profile"),
                    DvLevel = sideData.GetIntOrDefault("dv_level"),
                    RpuPresentFlag = Flag(sideData, "rpu_present_flag"),
                    ElPresentFlag = Flag(sideData, "el_present_flag"),
                    BlPresentFlag = Flag(sideData, "bl_present_flag"),
                    DvBlSignalCompatibilityId = sideData.GetIntOrDefault("dv_bl_signal_compatibility_id")
                };
            } else if (string.Equals(sideDataType, "HDR Dynamic Metadata SMPTE2094-40 (HDR10+)", StringComparison.OrdinalIgnoreCase)) {
                result = result with { Hdr10PlusPresentFlag = true };
            }
        }

        return result;
    }

    private static bool? Flag(JsonElement element, string name) =>
        element.GetIntOrDefault(name) switch {
            0 => false,
            1 => true,
            _ => null
        };

    private static int? ParseBitDepth(string? pixelFormat) {
        if (string.IsNullOrWhiteSpace(pixelFormat)) {
            return null;
        }

        foreach (var marker in new[] { "12", "10", "9", "8" }) {
            if (pixelFormat.Contains(marker, StringComparison.OrdinalIgnoreCase)) {
                return int.Parse(marker);
            }
        }

        return pixelFormat.Contains('p', StringComparison.OrdinalIgnoreCase) ? 8 : null;
    }
}

internal sealed record VideoSideData(
    int? DvProfile = null,
    int? DvLevel = null,
    bool? RpuPresentFlag = null,
    bool? ElPresentFlag = null,
    bool? BlPresentFlag = null,
    int? DvBlSignalCompatibilityId = null,
    bool Hdr10PlusPresentFlag = false);

public sealed record VideoProbeResult(
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
    IReadOnlyList<MediaStreamProbeResult>? Streams = null);

public sealed record MediaStreamProbeResult(
    int StreamIndex,
    string Type,
    string? Codec,
    string? Language,
    string? Title,
    int? Width,
    int? Height,
    double? FrameRate,
    int? BitRate,
    int? SampleRate,
    int? Channels,
    bool IsDefault,
    bool IsForced,
    string? PixelFormat = null,
    int? BitDepth = null,
    string? ColorRange = null,
    string? ColorSpace = null,
    string? ColorTransfer = null,
    string? ColorPrimaries = null,
    int? DvProfile = null,
    int? DvLevel = null,
    bool? RpuPresentFlag = null,
    bool? ElPresentFlag = null,
    bool? BlPresentFlag = null,
    int? DvBlSignalCompatibilityId = null,
    bool Hdr10PlusPresentFlag = false);

public sealed record AudioProbeResult(
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

public sealed record SubtitleStreamInfo(
    int StreamIndex,
    string CodecName,
    string Language,
    string? Title);

public sealed record ImageProbeResult(
    int Width,
    int Height,
    string? Codec);

/// <summary>
/// Extension helpers for safe JSON element traversal.
/// </summary>
internal static class JsonElementExtensions {
    public static JsonElement GetPropertyOrDefault(this JsonElement element, string name) {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value))
            return value;
        return default;
    }

    public static string? GetStringOrDefault(this JsonElement element, string name) {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)) {
            return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
        }
        return null;
    }

    public static double? GetDoubleOrDefault(this JsonElement element, string name) {
        var str = element.GetStringOrDefault(name);
        return str is not null && double.TryParse(str, out var v) ? v : null;
    }

    public static int? GetIntOrDefault(this JsonElement element, string name) {
        var str = element.GetStringOrDefault(name);
        return str is not null && int.TryParse(str, out var v) ? v : null;
    }

    public static long? GetLongOrDefault(this JsonElement element, string name) {
        var str = element.GetStringOrDefault(name);
        return str is not null && long.TryParse(str, out var v) ? v : null;
    }
}
