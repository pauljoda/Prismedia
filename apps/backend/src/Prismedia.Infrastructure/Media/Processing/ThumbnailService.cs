using Prismedia.Application.Videos;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Processes;

namespace Prismedia.Infrastructure.Media.Processing;

/// <summary>
/// Generates thumbnails, preview clips, and trickplay sprites via ffmpeg.
/// All generation runs at below-normal process priority with a bounded ffmpeg thread
/// count so background work never saturates the host or starves playback.
/// </summary>
public sealed class ThumbnailService {
    private readonly ProcessExecutor _processExecutor;
    private readonly MediaProbeService? _mediaProbe;
    private readonly MediaToolOptions _toolOptions;

    public ThumbnailService(
        ProcessExecutor processExecutor,
        MediaProbeService? mediaProbe = null,
        MediaToolOptions? toolOptions = null) {
        _processExecutor = processExecutor;
        _mediaProbe = mediaProbe;
        _toolOptions = toolOptions ?? new MediaToolOptions();
    }

    /// <summary>
    /// Generates a single JPEG thumbnail from a video at the given seek time.
    /// </summary>
    public async Task<bool> GenerateVideoThumbnailAsync(
        string inputPath, string outputPath, double seekSeconds,
        int width, int height, int quality, CancellationToken cancellationToken,
        MediaToolOptions? toolOptions = null) {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var tools = toolOptions ?? _toolOptions;
        var threads = Threads(tools);
        var videoFilter = await BuildVideoFilterAsync(inputPath, $"scale={width}:{height}", cancellationToken, tools);

        var result = await _processExecutor.RunAsync(tools.FfmpegPath,
            ["-hide_banner", "-loglevel", "error", "-y",
             "-threads", threads,
             "-ss", seekSeconds.ToString("F2"),
             "-i", inputPath,
             "-frames:v", "1",
             "-vf", videoFilter,
             "-q:v", quality.ToString(),
             outputPath],
            null, cancellationToken, lowPriority: true);

        return result.ExitCode == 0 && File.Exists(outputPath);
    }

    /// <summary>
    /// Generates a short H.264 preview clip from a video.
    /// </summary>
    public async Task<bool> GeneratePreviewClipAsync(
        string inputPath, string outputPath,
        double startSeconds, int durationSeconds,
        CancellationToken cancellationToken,
        MediaToolOptions? toolOptions = null) {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var tools = toolOptions ?? _toolOptions;
        var threads = Threads(tools);
        var videoFilter = await BuildVideoFilterAsync(inputPath, "scale=960:-2", cancellationToken, tools);

        var result = await _processExecutor.RunAsync(tools.FfmpegPath,
            ["-hide_banner", "-loglevel", "error", "-y",
             "-threads", threads,
             "-ss", startSeconds.ToString("F2"),
             "-t", durationSeconds.ToString(),
             "-i", inputPath,
             "-vf", videoFilter,
             "-an",
             "-c:v", "libx264",
             "-preset", "veryfast",
             "-crf", "24",
             "-movflags", "+faststart",
             outputPath],
            null, cancellationToken, lowPriority: true);

        return result.ExitCode == 0 && File.Exists(outputPath);
    }

    /// <summary>
    /// Extracts a single trickplay frame at the given timestamp.
    /// </summary>
    public async Task<bool> ExtractTrickplayFrameAsync(
        string inputPath, string outputPath,
        double seekSeconds, int width, int height, int jpegQuality,
        CancellationToken cancellationToken,
        MediaToolOptions? toolOptions = null) {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var tools = toolOptions ?? _toolOptions;
        var threads = Threads(tools);
        var videoFilter = await BuildVideoFilterAsync(
            inputPath,
            $"scale={width}:{height}:force_original_aspect_ratio=decrease:force_divisible_by=2,setsar=1,pad={width}:{height}:(ow-iw)/2:(oh-ih)/2,format=yuvj420p",
            cancellationToken,
            tools);

        var result = await _processExecutor.RunAsync(tools.FfmpegPath,
            ["-hide_banner", "-loglevel", "error", "-y",
             "-skip_frame", "nokey",
             "-threads", threads,
             "-ss", seekSeconds.ToString("F2"),
             "-i", inputPath,
             "-frames:v", "1",
             "-vf", videoFilter,
             "-q:v", jpegQuality.ToString(),
             outputPath],
            null, cancellationToken, lowPriority: true);

        return result.ExitCode == 0 && File.Exists(outputPath);
    }

    /// <summary>
    /// Generates a thumbnail from an image file, scaling to the target width.
    /// </summary>
    public async Task<bool> GenerateImageThumbnailAsync(
        string inputPath, string outputPath,
        int targetWidth, int quality,
        CancellationToken cancellationToken,
        MediaToolOptions? toolOptions = null) {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var tools = toolOptions ?? _toolOptions;
        var threads = Threads(tools);

        var result = await _processExecutor.RunAsync(tools.FfmpegPath,
            ["-hide_banner", "-loglevel", "error", "-y",
             "-threads", threads,
             "-i", inputPath,
             "-frames:v", "1",
             "-vf", $"scale={targetWidth}:-1",
             "-q:v", quality.ToString(),
             "-update", "1",
             outputPath],
            null, cancellationToken, lowPriority: true);

        return result.ExitCode == 0 && File.Exists(outputPath);
    }

    /// <summary>
    /// Extracts embedded subtitle streams from a video to WebVTT files.
    /// Attempts a single-pass multi-stream extraction, falling back to per-stream on failure.
    /// </summary>
    public async Task<IReadOnlyList<string>> ExtractSubtitlesAsync(
        string inputPath, string outputDir,
        IReadOnlyList<SubtitleStreamInfo> streams,
        CancellationToken cancellationToken,
        MediaToolOptions? toolOptions = null) {
        Directory.CreateDirectory(outputDir);
        var tools = toolOptions ?? _toolOptions;

        if (streams.Count == 0)
            return [];

        var outputPaths = new List<string>();
        foreach (var stream in streams) {
            // Stream metadata is untrusted input. Keep generated paths independent of
            // language/title values so path separators cannot escape the owned cache.
            var fileName = $"embedded-{stream.StreamIndex}.vtt";
            outputPaths.Add(Path.Combine(outputDir, fileName));
        }

        var args = new List<string> { "-y", "-v", "error", "-i", inputPath };
        for (var i = 0; i < streams.Count; i++) {
            args.AddRange(["-map", $"0:{streams[i].StreamIndex}", "-c:s", "webvtt", outputPaths[i]]);
        }

        var result = await _processExecutor.RunAsync(tools.FfmpegPath, args, null, cancellationToken, lowPriority: true);
        if (result.ExitCode == 0) {
            return outputPaths.Where(File.Exists).ToList();
        }

        // Fallback: extract one stream at a time
        var succeeded = new List<string>();
        foreach (var (stream, outputPath) in streams.Zip(outputPaths)) {
            var perStreamResult = await _processExecutor.RunAsync(tools.FfmpegPath,
                ["-y", "-v", "error", "-i", inputPath,
                 "-map", $"0:{stream.StreamIndex}", "-c:s", "webvtt", outputPath],
                null, cancellationToken, lowPriority: true);

            if (perStreamResult.ExitCode == 0 && File.Exists(outputPath)) {
                succeeded.Add(outputPath);
            }
        }

        return succeeded;
    }

    /// <summary>
    /// Extracts trickplay frames in a single ffmpeg decode pass. The file is opened and
    /// demuxed exactly once; the <c>fps=1/interval</c> filter samples one keyframe per
    /// interval (mirroring Jellyfin's trickplay extraction). This replaces the previous
    /// per-frame approach that spawned one input-seeking ffmpeg process per frame — that
    /// paid full process-startup and decode-thread-pool overhead hundreds of times per
    /// video and saturated every core. Frames are written as <c>frame-00001.jpg</c>…
    /// in lexical order so <see cref="ComposeTiledJpegSheetsAsync"/> can tile them directly.
    /// </summary>
    public async Task<int> ExtractTrickplayFramesBatchAsync(
        string inputPath, string outputDir, double duration,
        int intervalSeconds, int width, int height, int jpegQuality,
        CancellationToken cancellationToken,
        MediaToolOptions? toolOptions = null) {
        Directory.CreateDirectory(outputDir);
        var tools = toolOptions ?? _toolOptions;
        if (intervalSeconds < 1) intervalSeconds = 1;

        var totalFrames = (int)(duration / intervalSeconds);
        if (totalFrames < 1) return 0;

        var scaleFilter = await BuildVideoFilterAsync(
            inputPath,
            $"scale={width}:{height}:force_original_aspect_ratio=decrease:force_divisible_by=2,setsar=1,pad={width}:{height}:(ow-iw)/2:(oh-ih)/2,format=yuvj420p",
            cancellationToken,
            tools);

        // fps filter runs first so frames are dropped before the (more expensive)
        // scale/tonemap stage. -skip_frame nokey keeps the decoder to keyframes only.
        var frameFilter = $"fps=1/{intervalSeconds},{scaleFilter}";
        var threads = Threads(tools);
        var outputPattern = Path.Combine(outputDir, "frame-%05d.jpg");

        var result = await _processExecutor.RunAsync(tools.FfmpegPath,
            ["-hide_banner", "-loglevel", "error", "-y",
             "-skip_frame", "nokey",
             "-threads", threads,
             "-i", inputPath,
             "-an", "-sn",
             "-vf", frameFilter,
             "-threads", threads,
             "-c:v", "mjpeg",
             "-q:v", jpegQuality.ToString(),
             "-fps_mode", "passthrough",
             "-f", "image2",
             outputPattern],
            null, cancellationToken, lowPriority: true);

        if (result.ExitCode != 0)
            return 0;

        return Directory.EnumerateFiles(outputDir, "frame-*.jpg").Count();
    }

    /// <summary>
    /// Composites individual frame images into a single sprite-sheet JPEG using
    /// ffmpeg's concat demuxer and tile filter. Frames are read in lexical order
    /// from <paramref name="frameDir"/>.
    /// </summary>
    public async Task<bool> ComposeSpriteSheetAsync(
        string frameDir, string outputPath, int columns,
        int frameWidth, int frameHeight, int jpegQuality,
        CancellationToken cancellationToken,
        MediaToolOptions? toolOptions = null) {
        var frames = Directory.GetFiles(frameDir, "frame-*.jpg")
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToArray();
        if (frames.Length == 0) return false;

        var rows = (int)Math.Ceiling((double)frames.Length / columns);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        var concatList = Path.Combine(frameDir, "_concat.txt");
        await File.WriteAllLinesAsync(concatList,
            frames.Select(f => $"file '{f}'"),
            cancellationToken);

        var tools = toolOptions ?? _toolOptions;
        var result = await _processExecutor.RunAsync(tools.FfmpegPath,
            ["-hide_banner", "-loglevel", "error", "-y",
             "-threads", Threads(tools),
             "-f", "concat", "-safe", "0", "-i", concatList,
             "-vf", $"scale={frameWidth}:{frameHeight},tile={columns}x{rows}",
             "-q:v", jpegQuality.ToString(),
             outputPath],
            null, cancellationToken, lowPriority: true);

        File.Delete(concatList);
        return result.ExitCode == 0 && File.Exists(outputPath);
    }

    /// <summary>
    /// Composites extracted trickplay frames into numbered Jellyfin-style JPEG tile sheets.
    /// Each sheet contains up to <paramref name="columns"/> × <paramref name="rows"/> thumbnails.
    /// </summary>
    public async Task<int> ComposeTiledJpegSheetsAsync(
        string frameDir,
        string outputDir,
        int columns,
        int rows,
        int frameWidth,
        int frameHeight,
        int jpegQuality,
        CancellationToken cancellationToken,
        MediaToolOptions? toolOptions = null) {
        var frames = Directory.GetFiles(frameDir, "frame-*.jpg")
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToArray();
        if (frames.Length == 0) return 0;

        Directory.CreateDirectory(outputDir);
        var framesPerSheet = columns * rows;
        var sheetCount = 0;
        var tools = toolOptions ?? _toolOptions;
        var threads = Threads(tools);

        foreach (var chunk in frames.Chunk(framesPerSheet)) {
            var outputPath = Path.Combine(outputDir, $"{sheetCount}.jpg");
            var concatList = Path.Combine(outputDir, $"_concat_{sheetCount}.txt");
            await File.WriteAllLinesAsync(
                concatList,
                chunk.Select(frame => $"file '{Path.GetFullPath(frame).Replace("'", "'\\''")}'"),
                cancellationToken);

            var result = await _processExecutor.RunAsync(tools.FfmpegPath,
                ["-hide_banner", "-loglevel", "error", "-y",
                 "-threads", threads,
                 "-f", "concat", "-safe", "0", "-i", concatList,
                 "-vf", $"scale={frameWidth}:{frameHeight},tile={columns}x{rows}",
                 "-q:v", jpegQuality.ToString(),
                 outputPath],
                null, cancellationToken, lowPriority: true);

            File.Delete(concatList);
            if (result.ExitCode != 0 || !File.Exists(outputPath)) {
                break;
            }

            sheetCount++;
        }

        return sheetCount;
    }

    /// <summary>
    /// Generates a thumbnail and preview clip from a video. The two outputs are produced
    /// by separate ffmpeg invocations because they seek to different points in the file.
    /// </summary>
    public async Task<(bool Thumbnail, bool Preview)> GenerateThumbnailAndPreviewAsync(
        string inputPath,
        string thumbnailPath, double thumbSeekSeconds, int thumbWidth, int thumbHeight, int thumbQuality,
        string previewPath, double previewStartSeconds, int previewDurationSeconds,
        CancellationToken cancellationToken,
        MediaToolOptions? toolOptions = null) {
        Directory.CreateDirectory(Path.GetDirectoryName(thumbnailPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(previewPath)!);
        var tools = toolOptions ?? _toolOptions;
        var threads = Threads(tools);
        var thumbFilter = await BuildVideoFilterAsync(inputPath, $"scale={thumbWidth}:{thumbHeight}", cancellationToken, tools);
        var previewFilter = await BuildVideoFilterAsync(inputPath, "scale=960:-2", cancellationToken, tools);

        var thumbResult = await _processExecutor.RunAsync(tools.FfmpegPath,
            ["-hide_banner", "-loglevel", "error", "-y",
             "-threads", threads,
             "-ss", thumbSeekSeconds.ToString("F2"),
             "-i", inputPath,
             "-frames:v", "1",
             "-vf", thumbFilter,
             "-q:v", thumbQuality.ToString(),
             thumbnailPath],
            null, cancellationToken, lowPriority: true);

        var previewResult = await _processExecutor.RunAsync(tools.FfmpegPath,
            ["-hide_banner", "-loglevel", "error", "-y",
             "-threads", threads,
             "-ss", previewStartSeconds.ToString("F2"),
             "-t", previewDurationSeconds.ToString(),
             "-i", inputPath,
             "-vf", previewFilter,
             "-an",
             "-c:v", "libx264",
             "-preset", "veryfast",
             "-crf", "24",
             "-movflags", "+faststart",
             previewPath],
            null, cancellationToken, lowPriority: true);

        return (
            thumbResult.ExitCode == 0 && File.Exists(thumbnailPath),
            previewResult.ExitCode == 0 && File.Exists(previewPath));
    }

    private async Task<string> BuildVideoFilterAsync(
        string inputPath,
        string outputTransform,
        CancellationToken cancellationToken,
        MediaToolOptions toolOptions) {
        if (_mediaProbe is null) {
            return outputTransform;
        }

        var probe = await _mediaProbe.ProbeVideoAsync(inputPath, cancellationToken, toolOptions.FfprobePath);
        var videoStream = probe?.Streams?
            .Where(stream => stream.Type.Equals(StreamKind.Video.ToCode(), StringComparison.OrdinalIgnoreCase))
            .OrderBy(stream => stream.StreamIndex)
            .FirstOrDefault();

        if (!NeedsToneMapping(videoStream)) {
            return outputTransform;
        }

        return FfmpegToneMapping.BuildFilter(
            videoStream?.ColorTransfer,
            videoStream?.DvProfile,
            videoStream?.DvBlSignalCompatibilityId,
            outputTransform);
    }

    private static bool NeedsToneMapping(MediaStreamProbeResult? stream) {
        if (stream is null) {
            return false;
        }

        var range = VideoPlaybackRangePolicy.Classify(
            stream.ColorTransfer,
            stream.ColorPrimaries,
            stream.DvProfile,
            stream.RpuPresentFlag,
            stream.Hdr10PlusPresentFlag);
        return !range.VideoRangeType.Equals("SDR", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Per-process ffmpeg thread cap for background generation, as a CLI argument value.
    /// </summary>
    private static string Threads(MediaToolOptions toolOptions) =>
        Math.Max(1, toolOptions.AssetGenerationThreads).ToString();

    /// <summary>
    /// Generates audio waveform peak data via ffmpeg PCM decode.
    /// Returns min/max pairs for the given pixels-per-second resolution.
    /// </summary>
    public async Task<int[]?> GenerateWaveformDataAsync(
        string inputPath, double durationSeconds, int pixelsPerSecond,
        CancellationToken cancellationToken,
        MediaToolOptions? toolOptions = null) {
        const int sampleRate = 8000;
        var tools = toolOptions ?? _toolOptions;
        var pcmPath = Path.Combine(Path.GetTempPath(), $"prismedia-waveform-{Guid.NewGuid():N}.pcm");
        var result = await _processExecutor.RunToFileAsync(tools.FfmpegPath,
            ["-hide_banner", "-loglevel", "error",
             "-threads", Threads(tools),
             "-i", inputPath,
             "-f", "s16le", "-ac", "1", "-ar", sampleRate.ToString(),
             "pipe:1"],
            null, pcmPath, cancellationToken, lowPriority: true);

        try {
            if (result.ExitCode != 0)
                return null;

            var pcmBytes = await File.ReadAllBytesAsync(pcmPath, cancellationToken);
            if (pcmBytes.Length < 2)
                return null;

            var totalSamples = pcmBytes.Length / 2;
            var totalPixels = (int)(durationSeconds * pixelsPerSecond);
            if (totalPixels < 1)
                totalPixels = 1;

            var samplesPerPixel = totalSamples / totalPixels;
            if (samplesPerPixel < 1)
                samplesPerPixel = 1;

            var data = new int[totalPixels * 2];
            for (var pixel = 0; pixel < totalPixels; pixel++) {
                var startSample = pixel * samplesPerPixel;
                var endSample = Math.Min(startSample + samplesPerPixel, totalSamples);
                short min = 0, max = 0;

                for (var s = startSample; s < endSample; s++) {
                    var offset = s * 2;
                    if (offset + 1 >= pcmBytes.Length) break;
                    var sample = (short)(pcmBytes[offset] | (pcmBytes[offset + 1] << 8));
                    if (sample < min) min = sample;
                    if (sample > max) max = sample;
                }

                data[pixel * 2] = min;
                data[pixel * 2 + 1] = max;
            }

            return data;
        } finally {
            if (File.Exists(pcmPath)) {
                File.Delete(pcmPath);
            }
        }
    }
}
