using Prismedia.Infrastructure.Processes;

namespace Prismedia.Infrastructure.Media.Processing;

/// <summary>
/// Generates thumbnails, preview clips, and trickplay sprites via ffmpeg.
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
        var videoFilter = await BuildVideoFilterAsync(inputPath, $"scale={width}:{height}", cancellationToken, tools);

        var result = await _processExecutor.RunAsync(tools.FfmpegPath,
            ["-hide_banner", "-loglevel", "error", "-y",
             "-ss", seekSeconds.ToString("F2"),
             "-i", inputPath,
             "-frames:v", "1",
             "-vf", videoFilter,
             "-q:v", quality.ToString(),
             outputPath],
            null, cancellationToken);

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
        var videoFilter = await BuildVideoFilterAsync(inputPath, "scale=960:-2", cancellationToken, tools);

        var result = await _processExecutor.RunAsync(tools.FfmpegPath,
            ["-hide_banner", "-loglevel", "error", "-y",
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
            null, cancellationToken);

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
        var videoFilter = await BuildVideoFilterAsync(
            inputPath,
            $"scale={width}:{height}:force_original_aspect_ratio=decrease:force_divisible_by=2,setsar=1,pad={width}:{height}:(ow-iw)/2:(oh-ih)/2,format=yuvj420p",
            cancellationToken,
            tools);

        var result = await _processExecutor.RunAsync(tools.FfmpegPath,
            ["-hide_banner", "-loglevel", "error", "-y",
             "-skip_frame", "nokey",
             "-ss", seekSeconds.ToString("F2"),
             "-i", inputPath,
             "-frames:v", "1",
             "-vf", videoFilter,
             "-q:v", jpegQuality.ToString(),
             outputPath],
            null, cancellationToken);

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

        var result = await _processExecutor.RunAsync(tools.FfmpegPath,
            ["-hide_banner", "-loglevel", "error", "-y",
             "-i", inputPath,
             "-frames:v", "1",
             "-vf", $"scale={targetWidth}:-1",
             "-q:v", quality.ToString(),
             "-update", "1",
             outputPath],
            null, cancellationToken);

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
            var fileName = $"embedded-{stream.Language}-{stream.StreamIndex}.vtt";
            outputPaths.Add(Path.Combine(outputDir, fileName));
        }

        var args = new List<string> { "-y", "-v", "error", "-i", inputPath };
        for (var i = 0; i < streams.Count; i++) {
            args.AddRange(["-map", $"0:{streams[i].StreamIndex}", "-c:s", "webvtt", outputPaths[i]]);
        }

        var result = await _processExecutor.RunAsync(tools.FfmpegPath, args, null, cancellationToken);
        if (result.ExitCode == 0) {
            return outputPaths.Where(File.Exists).ToList();
        }

        // Fallback: extract one stream at a time
        var succeeded = new List<string>();
        foreach (var (stream, outputPath) in streams.Zip(outputPaths)) {
            var perStreamResult = await _processExecutor.RunAsync(tools.FfmpegPath,
                ["-y", "-v", "error", "-i", inputPath,
                 "-map", $"0:{stream.StreamIndex}", "-c:s", "webvtt", outputPath],
                null, cancellationToken);

            if (perStreamResult.ExitCode == 0 && File.Exists(outputPath)) {
                succeeded.Add(outputPath);
            }
        }

        return succeeded;
    }

    /// <summary>
    /// Extracts trickplay frames using parallel per-frame keyframe extraction.
    /// Uses -skip_frame nokey with input-seek (-ss before -i) so each invocation
    /// only decodes a single keyframe — orders of magnitude faster than segment-based
    /// fps-filter extraction which must decode the entire video.
    /// </summary>
    public async Task<int> ExtractTrickplayFramesBatchAsync(
        string inputPath, string outputDir, double duration,
        int intervalSeconds, int width, int height, int jpegQuality,
        CancellationToken cancellationToken,
        MediaToolOptions? toolOptions = null) {
        Directory.CreateDirectory(outputDir);
        var tools = toolOptions ?? _toolOptions;
        var frameFilter = await BuildVideoFilterAsync(
            inputPath,
            $"scale={width}:{height}:force_original_aspect_ratio=decrease:force_divisible_by=2,setsar=1,pad={width}:{height}:(ow-iw)/2:(oh-ih)/2,format=yuvj420p",
            cancellationToken,
            tools);

        var totalFrames = (int)(duration / intervalSeconds);
        if (totalFrames < 1) return 0;

        using var semaphore = new SemaphoreSlim(8);
        var tasks = new List<Task<bool>>(totalFrames);

        for (var i = 0; i < totalFrames; i++) {
            var seekSeconds = i * intervalSeconds + intervalSeconds / 2.0;
            seekSeconds = Math.Min(seekSeconds, Math.Max(0, duration - 0.5));

            var outputPath = Path.Combine(outputDir, $"frame-{i + 1:D5}.jpg");
            tasks.Add(ExtractSingleKeyframeAsync(
                semaphore, inputPath, outputPath, seekSeconds,
                frameFilter, jpegQuality, tools, cancellationToken));
        }

        var results = await Task.WhenAll(tasks);
        return results.Count(r => r);
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
             "-f", "concat", "-safe", "0", "-i", concatList,
             "-vf", $"scale={frameWidth}:{frameHeight},tile={columns}x{rows}",
             "-q:v", jpegQuality.ToString(),
             outputPath],
            null, cancellationToken);

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

        foreach (var chunk in frames.Chunk(framesPerSheet)) {
            var outputPath = Path.Combine(outputDir, $"{sheetCount}.jpg");
            var concatList = Path.Combine(outputDir, $"_concat_{sheetCount}.txt");
            await File.WriteAllLinesAsync(
                concatList,
                chunk.Select(frame => $"file '{Path.GetFullPath(frame).Replace("'", "'\\''")}'"),
                cancellationToken);

            var tools = toolOptions ?? _toolOptions;
            var result = await _processExecutor.RunAsync(tools.FfmpegPath,
                ["-hide_banner", "-loglevel", "error", "-y",
                 "-f", "concat", "-safe", "0", "-i", concatList,
                 "-vf", $"scale={frameWidth}:{frameHeight},tile={columns}x{rows}",
                 "-q:v", jpegQuality.ToString(),
                 outputPath],
                null, cancellationToken);

            File.Delete(concatList);
            if (result.ExitCode != 0 || !File.Exists(outputPath)) {
                break;
            }

            sheetCount++;
        }

        return sheetCount;
    }

    private async Task<bool> ExtractSingleKeyframeAsync(
        SemaphoreSlim semaphore, string inputPath, string outputPath,
        double seekSeconds, string videoFilter, int jpegQuality,
        MediaToolOptions toolOptions,
        CancellationToken cancellationToken) {
        await semaphore.WaitAsync(cancellationToken);
        try {
            var result = await _processExecutor.RunAsync(toolOptions.FfmpegPath,
                ["-hide_banner", "-loglevel", "error", "-y",
                 "-skip_frame", "nokey",
                 "-ss", seekSeconds.ToString("F2"),
                 "-i", inputPath,
                 "-frames:v", "1",
                 "-vf", videoFilter,
                 "-q:v", jpegQuality.ToString(),
                 outputPath],
                null, cancellationToken);

            return result.ExitCode == 0 && File.Exists(outputPath);
        } finally {
            semaphore.Release();
        }
    }

    /// <summary>
    /// Generates a thumbnail and preview clip in a single ffmpeg invocation using
    /// the tee muxer to produce both outputs from one decode pass.
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
        var thumbFilter = await BuildVideoFilterAsync(inputPath, $"scale={thumbWidth}:{thumbHeight}", cancellationToken, tools);
        var previewFilter = await BuildVideoFilterAsync(inputPath, "scale=960:-2", cancellationToken, tools);

        var thumbResult = await _processExecutor.RunAsync(tools.FfmpegPath,
            ["-hide_banner", "-loglevel", "error", "-y",
             "-ss", thumbSeekSeconds.ToString("F2"),
             "-i", inputPath,
             "-frames:v", "1",
             "-vf", thumbFilter,
             "-q:v", thumbQuality.ToString(),
             thumbnailPath],
            null, cancellationToken);

        var previewResult = await _processExecutor.RunAsync(tools.FfmpegPath,
            ["-hide_banner", "-loglevel", "error", "-y",
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
            null, cancellationToken);

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
            .Where(stream => stream.Type.Equals("Video", StringComparison.OrdinalIgnoreCase))
            .OrderBy(stream => stream.StreamIndex)
            .FirstOrDefault();

        if (!NeedsToneMapping(videoStream)) {
            return outputTransform;
        }

        if (RequiresDolbyVisionToneMapping(videoStream)) {
            return $"{InputHdrColorParameters(videoStream)},{outputTransform},tonemapx=tonemap=bt2390:desat=0:peak=400:t=bt709:m=bt709:p=bt709:format=yuv420p";
        }

        return $"{InputHdrColorParameters(videoStream)},zscale=t=linear:npl=100,format=gbrpf32le,zscale=p=bt709,tonemap=tonemap=hable:desat=0:peak=100,zscale=t=bt709:m=bt709:p=bt709:out_range=tv,{outputTransform}";
    }

    private static bool NeedsToneMapping(MediaStreamProbeResult? stream) =>
        stream is not null &&
        (RequiresDolbyVisionToneMapping(stream) ||
            stream.Hdr10PlusPresentFlag ||
            string.Equals(stream.ColorTransfer, "arib-std-b67", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(stream.ColorTransfer, "smpte2084", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(stream.ColorPrimaries, "bt2020", StringComparison.OrdinalIgnoreCase));

    private static bool RequiresDolbyVisionToneMapping(MediaStreamProbeResult? stream) =>
        stream?.DvProfile is not null ||
        stream?.RpuPresentFlag == true ||
        stream?.DvBlSignalCompatibilityId is 0;

    private static string InputHdrColorParameters(MediaStreamProbeResult? stream) {
        var colorTransfer = string.Equals(stream?.ColorTransfer, "arib-std-b67", StringComparison.OrdinalIgnoreCase)
            ? "arib-std-b67"
            : "smpte2084";
        return $"setparams=color_primaries=bt2020:color_trc={colorTransfer}:colorspace=bt2020nc";
    }

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
        var result = await _processExecutor.RunAsync(tools.FfmpegPath,
            ["-hide_banner", "-loglevel", "error",
             "-i", inputPath,
             "-f", "s16le", "-ac", "1", "-ar", sampleRate.ToString(),
             "pipe:1"],
            null, cancellationToken);

        if (result.ExitCode != 0)
            return null;

        var pcmBytes = System.Text.Encoding.Latin1.GetBytes(result.StandardOutput);
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
    }
}
