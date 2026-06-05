using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Prismedia.Application.Settings;
using Prismedia.Application.Videos;
using Prismedia.Contracts.Media;
using Prismedia.Infrastructure.Media.Processing;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Processes;
using Prismedia.Infrastructure.Settings;

namespace Prismedia.Infrastructure.Videos;

/// <summary>
/// ffmpeg rendition argument building, quality/codec selection, transcoder-profile resolution, and the virtual-rendition value records for <see cref="HlsAssetService"/>.
/// </summary>
public sealed partial class HlsAssetService {
    private static IReadOnlyList<string> VirtualRenditionArguments(
        VideoSourceFile source,
        VirtualHlsRendition rendition,
        int? audioStreamIndex,
        int startSegment,
        string playlistPath,
        string segmentPattern,
        HlsTranscoderProfile transcoderProfile,
        string vaapiDevice,
        int threadCount,
        bool enableToneMapping = true) {
        var gop = Math.Max(1, (int)Math.Ceiling(SegmentDurationSeconds * (source.FrameRate ?? 24)));
        var startSeconds = startSegment * SegmentDurationSeconds;
        var arguments = new List<string>
        {
            "-hide_banner",
            "-y",
            "-loglevel",
            "error",
            "-nostats",
            "-ss",
            startSeconds.ToString("0.000")
        };

        if (transcoderProfile == HlsTranscoderProfile.Vaapi) {
            arguments.AddRange(["-vaapi_device", vaapiDevice]);
        }

        // When tone mapping on VideoToolbox, offload decode to the GPU as well; ffmpeg downloads the
        // frames for the CPU tone-map filter, leaving only that filter on the CPU. Decode hardware
        // acceleration must be declared before the input.
        if (transcoderProfile == HlsTranscoderProfile.VideoToolbox && enableToneMapping && NeedsToneMapping(source)) {
            arguments.AddRange(["-hwaccel", "videotoolbox"]);
        }

        arguments.AddRange(
        [
            "-i",
            source.Path,
            "-map_metadata",
            "-1",
            "-map_chapters",
            "-1"
        ]);

        arguments.AddRange(VideoFilterArguments(source, rendition, transcoderProfile, enableToneMapping));

        arguments.AddRange(
        [
            "-map",
            "0:v:0",
            "-map",
            audioStreamIndex is null ? "0:a:0?" : $"0:{audioStreamIndex.Value}?"
        ]);

        arguments.AddRange(VideoEncoderArguments(rendition, transcoderProfile, threadCount));

        arguments.AddRange(
        [
            "-force_key_frames:0",
            $"expr:gte(t,n_forced*{SegmentDurationSeconds})",
            "-g",
            gop.ToString(),
            "-keyint_min",
            gop.ToString(),
            "-sc_threshold",
            "0",
            "-c:a",
            "aac",
            "-b:a",
            rendition.AudioBitrate,
            "-ac",
            "2",
            "-ar",
            "48000",
            "-copyts",
            "-avoid_negative_ts",
            "disabled",
            "-max_muxing_queue_size",
            "128",
            "-f",
            "hls",
            "-max_delay",
            "5000000",
            "-hls_time",
            SegmentDurationSeconds.ToString(),
            "-hls_segment_type",
            "mpegts",
            "-hls_playlist_type",
            "vod",
            "-hls_list_size",
            "0",
            "-hls_flags",
            "temp_file",
            "-start_number",
            startSegment.ToString(),
            "-hls_segment_filename",
            segmentPattern,
            playlistPath
        ]);

        return arguments;
    }

    private static IReadOnlyList<string> VideoFilterArguments(
        VideoSourceFile source,
        VirtualHlsRendition rendition,
        HlsTranscoderProfile transcoderProfile,
        bool enableToneMapping = true) {
        if (enableToneMapping && NeedsToneMapping(source)) {
            return
            [
                "-vf",
                ToneMappingFilter(source, rendition)
            ];
        }

        if (transcoderProfile == HlsTranscoderProfile.Vaapi) {
            var width = ScaledWidth(source.Width, source.Height, rendition.Height);
            var scaleWidth = width?.ToString() ?? "-2";
            return
            [
                "-vf",
                $"format=nv12,hwupload,scale_vaapi=w={scaleWidth}:h={rendition.Height}:format=nv12"
            ];
        }

        var outputFormat = transcoderProfile == HlsTranscoderProfile.Qsv ? "nv12" : "yuv420p";
        return
        [
            "-vf",
            $"scale=w=-2:h={rendition.Height}:force_original_aspect_ratio=decrease:force_divisible_by=2,format={outputFormat}"
        ];
    }

    private static string ToneMappingFilter(VideoSourceFile source, VirtualHlsRendition rendition) {
        var scale = $"scale=w=-2:h={rendition.Height}:force_original_aspect_ratio=decrease:force_divisible_by=2";
        var stream = PrimaryVideoStream(source);
        return FfmpegToneMapping.BuildFilter(
            stream?.ColorTransfer,
            stream?.DvProfile,
            stream?.DvBlSignalCompatibilityId,
            scale,
            trailingFormat: "yuv420p");
    }

    private static IReadOnlyList<string> VideoEncoderArguments(
        VirtualHlsRendition rendition,
        HlsTranscoderProfile transcoderProfile,
        int threadCount) {
        var encoder = transcoderProfile switch {
            HlsTranscoderProfile.VideoToolbox => "h264_videotoolbox",
            HlsTranscoderProfile.Vaapi => "h264_vaapi",
            HlsTranscoderProfile.Nvenc => "h264_nvenc",
            HlsTranscoderProfile.Qsv => "h264_qsv",
            _ => "libx264"
        };

        var arguments = new List<string>
        {
            "-c:v",
            encoder
        };

        if (transcoderProfile == HlsTranscoderProfile.Software) {
            // Cap libx264 worker threads so one transcode cannot saturate every core. Without this,
            // x264 grabs ~1.5x the core count in worker threads and pins the box; the reference media
            // server always emits an explicit -threads. Hardware encoders do their work on the GPU and
            // are left at ffmpeg defaults.
            arguments.AddRange(
            [
                "-threads",
                threadCount.ToString(),
                "-preset",
                "veryfast",
                "-crf",
                rendition.Crf.ToString(),
                "-profile:v",
                "main",
                "-pix_fmt",
                "yuv420p"
            ]);
        } else {
            if (transcoderProfile == HlsTranscoderProfile.VideoToolbox) {
                arguments.AddRange(["-allow_sw", "1"]);
            }

            arguments.AddRange(
            [
                "-profile:v",
                "main"
            ]);

            if (transcoderProfile != HlsTranscoderProfile.Vaapi) {
                arguments.AddRange(["-pix_fmt", transcoderProfile == HlsTranscoderProfile.Qsv ? "nv12" : "yuv420p"]);
            }
        }

        arguments.AddRange(
        [
            "-b:v",
            rendition.VideoBitrate,
            "-maxrate",
            rendition.MaxRate,
            "-bufsize",
            rendition.BufferSize
        ]);

        return arguments;
    }

    private static int SegmentCount(double durationSeconds) =>
        !double.IsFinite(durationSeconds) || durationSeconds <= 0
            ? 0
            : (int)Math.Ceiling(durationSeconds / SegmentDurationSeconds);

    private static double SegmentDuration(double durationSeconds, int index) {
        var total = SegmentCount(durationSeconds);
        if (index < 0 || index >= total) return 0;
        if (index < total - 1) return SegmentDurationSeconds;
        var duration = durationSeconds - (total - 1) * SegmentDurationSeconds;
        return duration > 0 ? duration : SegmentDurationSeconds;
    }

    private static int ToBitsPerSecond(string rate) {
        var value = rate.Trim();
        var unit = value[^1];
        if (unit is 'k' or 'K' or 'm' or 'M') {
            var number = int.TryParse(value[..^1], out var parsed) ? parsed : 0;
            return unit is 'm' or 'M' ? number * 1_000_000 : number * 1_000;
        }

        return int.TryParse(value, out var raw) ? raw : 0;
    }

    private static IReadOnlyList<JellyfinQualityOption> JellyfinQualityOptions(
        int sourceVideoBitrate,
        string? videoCodec) {
        var options = JellyfinQualityPresetOptions();
        if (sourceVideoBitrate <= 0) {
            return options;
        }

        var comparableBitrate = sourceVideoBitrate;
        if (IsEfficientVideoCodec(videoCodec) && comparableBitrate <= 20_000_000) {
            comparableBitrate = (int)Math.Round(comparableBitrate * 1.5);
        }

        var selected = new List<JellyfinQualityOption>();
        var nextHigher = options
            .Where(option => option.Bitrate > comparableBitrate)
            .LastOrDefault();
        if (nextHigher is not null) {
            selected.Add(nextHigher);
        }

        selected.AddRange(options.Where(option => option.Bitrate <= comparableBitrate));
        return selected.Count > 0 ? selected : [options[^1]];
    }

    private static IReadOnlyList<JellyfinQualityOption> JellyfinQualityPresetOptions() =>
    [
        new("120mbps", 2160, 120_000_000),
        new("80mbps", 2160, 80_000_000),
        new("60mbps", 2160, 60_000_000),
        new("40mbps", 2160, 40_000_000),
        new("20mbps", 2160, 20_000_000),
        new("15mbps", 1440, 15_000_000),
        new("10mbps", 1440, 10_000_000),
        new("8mbps", 1080, 8_000_000),
        new("6mbps", 1080, 6_000_000),
        new("4mbps", 720, 4_000_000),
        new("3mbps", 720, 3_000_000),
        new("1500kbps", 720, 1_500_000),
        new("720kbps", 480, 720_000),
        new("420kbps", 360, 420_000)
    ];

    private static VirtualHlsRendition RenditionForQualityOption(
        JellyfinQualityOption option,
        int sourceHeight) {
        var height = Math.Min(sourceHeight, option.MaxHeight);
        var videoBitrate = ToRate(option.Bitrate);
        var maxRate = ToRate((int)Math.Round(option.Bitrate * 1.15));
        var bufferSize = ToRate(option.Bitrate * 2);
        return new(
            option.Name,
            height,
            videoBitrate,
            maxRate,
            bufferSize,
            option.Bitrate >= 15_000_000 ? "192k" : "128k",
            CrfForHeight(height));
    }

    private static int SourceVideoBitrate(VideoSourceFile source) =>
        source.Streams?
            .Where(stream => stream.Type.Equals("Video", StringComparison.OrdinalIgnoreCase))
            .OrderBy(stream => stream.StreamIndex)
            .Select(stream => stream.BitRate)
            .FirstOrDefault(bitRate => bitRate is > 0) ??
        source.BitRate ??
        0;

    private static bool IsEfficientVideoCodec(string? codec) =>
        codec is not null &&
        (codec.Equals("hevc", StringComparison.OrdinalIgnoreCase) ||
            codec.Equals("h265", StringComparison.OrdinalIgnoreCase) ||
            codec.Equals("av1", StringComparison.OrdinalIgnoreCase) ||
            codec.Equals("vp9", StringComparison.OrdinalIgnoreCase));

    private static HlsTranscoderProfile ResolveEffectiveTranscoderProfile(
        VideoSourceFile source,
        HlsTranscoderProfile requestedProfile) {
        if (!NeedsToneMapping(source)) {
            return requestedProfile;
        }

        // VideoToolbox keeps decode and encode on the GPU while the HDR/Dolby Vision tone map runs
        // on the CPU, which is dramatically faster than an all-software transcode. Other accelerators
        // have no wired tone-map path, so they fall back to software to guarantee correct SDR output.
        return requestedProfile == HlsTranscoderProfile.VideoToolbox
            ? HlsTranscoderProfile.VideoToolbox
            : HlsTranscoderProfile.Software;
    }

    private static bool NeedsToneMapping(VideoSourceFile source) {
        var range = VideoPlaybackRangePolicy.Classify(PrimaryVideoStream(source));
        return !range.VideoRangeType.Equals("SDR", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMissingVideoFilterError(string standardError) =>
        standardError.Contains("No such filter", StringComparison.OrdinalIgnoreCase) ||
        standardError.Contains("Filter not found", StringComparison.OrdinalIgnoreCase);

    private static VideoSourceStream? PrimaryVideoStream(VideoSourceFile source) =>
        source.Streams?
            .Where(stream => stream.Type.Equals("Video", StringComparison.OrdinalIgnoreCase))
            .OrderBy(stream => stream.StreamIndex)
            .FirstOrDefault();

    private static string ToRate(int bitsPerSecond) =>
        bitsPerSecond % 1_000_000 == 0
            ? $"{bitsPerSecond / 1_000_000}M"
            : $"{Math.Max(1, bitsPerSecond / 1_000)}k";

    private static int CrfForHeight(int height) =>
        height switch {
            <= 480 => 22,
            <= 720 => 21,
            <= 1080 => 20,
            <= 1440 => 19,
            _ => 18
        };

    private static int NormalizeRenditionHeight(int height) =>
        Math.Max(2, height % 2 == 0 ? height : height - 1);

    private static string H264CodecForHeight(int height) =>
        height switch {
            <= 480 => "avc1.4d401e",
            <= 720 => "avc1.4d401f",
            <= 1080 => "avc1.4d4029",
            <= 1440 => "avc1.4d4032",
            _ => "avc1.4d4033"
        };

    private static int? ScaledWidth(int? sourceWidth, int? sourceHeight, int targetHeight) {
        if (sourceWidth is not > 0 || sourceHeight is not > 0 || targetHeight <= 0) return null;
        var width = (int)Math.Round((double)sourceWidth.Value / sourceHeight.Value * targetHeight);
        return width % 2 == 0 ? width : width - 1;
    }

    private static int? ParseSegmentIndex(string fileName) {
        if (!fileName.StartsWith("seg_", StringComparison.OrdinalIgnoreCase) ||
            !fileName.EndsWith(".ts", StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        var value = fileName["seg_".Length..^".ts".Length];
        return int.TryParse(value, out var index) ? index : null;
    }

    private static bool IsSameVirtualCache(
        VirtualCacheMetadata? left,
        VirtualCacheMetadata right) =>
        left is not null &&
        left.SourcePath == right.SourcePath &&
        left.SourceSize == right.SourceSize &&
        left.SourceModifiedUtc == right.SourceModifiedUtc &&
        Math.Abs(left.DurationSeconds - right.DurationSeconds) < 0.001 &&
        left.Renditions.SequenceEqual(right.Renditions) &&
        left.TranscoderProfile == right.TranscoderProfile &&
        left.FormatVersion == right.FormatVersion;

    private static HlsTranscoderProfile ResolveTranscoderProfile(HlsAssetServiceOptions options) {
        if (options.TranscoderProfile != HlsTranscoderProfile.Auto) {
            return options.TranscoderProfile;
        }

        if (OperatingSystem.IsMacOS()) {
            return HlsTranscoderProfile.VideoToolbox;
        }

        if (OperatingSystem.IsLinux() && File.Exists(options.VaapiDevice)) {
            return HlsTranscoderProfile.Vaapi;
        }

        return HlsTranscoderProfile.Software;
    }

    private async Task<HlsAssetServiceOptions> ResolveTranscoderOptionsAsync(CancellationToken cancellationToken) {
        if (_db is null) {
            return _options;
        }

        var settings = await new SettingsService(new EfSettingsPersistence(_db))
            .GetHlsSettingsAsync(cancellationToken);

        return new HlsAssetServiceOptions(
            _options.CacheRoot,
            HlsTranscoderProfiles.ParseOrDefault(settings.TranscoderProfile, _options.TranscoderProfile),
            string.IsNullOrWhiteSpace(settings.FfmpegPath) ? _options.FfmpegPath : settings.FfmpegPath.Trim(),
            string.IsNullOrWhiteSpace(settings.VaapiDevice) ? _options.VaapiDevice : settings.VaapiDevice.Trim(),
            _options.FfprobePath,
            settings.EnableAdaptiveBitrate,
            settings.EncodingThreadCount);
    }

    // Resolves the hard ffmpeg thread cap for a software transcode. A configured value is clamped to
    // the host's core count; 0 (auto) leaves one core free so a single transcode never freezes the
    // API/worker/PostgreSQL. Hardware encoders ignore this — their work runs on the GPU.
    private static int ResolveEncoderThreadCount(int configured) =>
        configured > 0
            ? Math.Min(configured, Environment.ProcessorCount)
            : Math.Max(1, Environment.ProcessorCount - 1);

    private static void ResetStagingDirectory(string stagingDirectory) {
        if (Directory.Exists(stagingDirectory)) {
            Directory.Delete(stagingDirectory, recursive: true);
        }

        Directory.CreateDirectory(stagingDirectory);
    }

    private static string MimeForExtension(string extension) {
        return extension.ToLowerInvariant() switch {
            ".m3u8" => MediaContentTypes.HlsPlaylist,
            ".ts" => MediaContentTypes.VideoMp2t,
            ".mp4" or ".m4s" => MediaContentTypes.VideoMp4,
            ".vtt" => MediaContentTypes.Vtt,
            _ => MediaContentTypes.OctetStream
        };
    }

    private static string CacheControlForExtension(string extension) {
        return extension.Equals(".m3u8", StringComparison.OrdinalIgnoreCase)
            ? "public, max-age=60"
            : "public, max-age=31536000, immutable";
    }

    private sealed record VirtualHlsRendition(
        string Name,
        int Height,
        string VideoBitrate,
        string MaxRate,
        string BufferSize,
        string AudioBitrate,
        int Crf);

    private sealed record JellyfinQualityOption(string Name, int MaxHeight, int Bitrate);

    private sealed record VirtualCacheMetadata(
        string SourcePath,
        long SourceSize,
        DateTime SourceModifiedUtc,
        double DurationSeconds,
        IReadOnlyList<string> Renditions,
        string TranscoderProfile = nameof(HlsTranscoderProfile.Software),
        int FormatVersion = 0);

    private sealed record VirtualRenditionGeneration(
        int StartSegment,
        int EndSegment,
        string StagingDirectory,
        CancellationTokenSource Cancellation,
        Task Task,
        Guid EntityId = default,
        DateTimeOffset StartedAtUtc = default);

    private sealed record VirtualTrickplayStream(int Width, int Height, int Bandwidth);
}
