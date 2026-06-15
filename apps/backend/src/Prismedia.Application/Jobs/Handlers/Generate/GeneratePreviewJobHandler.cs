using Prismedia.Application.Jobs.Handlers;
using Microsoft.Extensions.Logging;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Contracts.Media;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers.Generate;

/// <summary>
/// Generates video thumbnails, preview clips, and Jellyfin-style trickplay tiles via ffmpeg.
/// Optimized for throughput: uses batch trickplay extraction (single ffmpeg pass)
/// and combined thumbnail+preview generation.
/// </summary>
public sealed class GeneratePreviewJobHandler(
    ILogger<GeneratePreviewJobHandler> logger,
    IMediaAssetGenerator assets,
    IMediaProcessingStatePersistence persistence,
    ILibraryScanRootPersistence roots,
    IGridThumbnailService gridThumbnails) : EntityFileJobHandler(logger, persistence) {
    public override JobType Type => JobType.GeneratePreview;

    protected override async Task ExecuteAsync(
        JobContext context, Guid entityId, string filePath, CancellationToken cancellationToken) {
        var timer = new JobPhaseTimer();
        var settings = await roots.GetSettingsAsync(cancellationToken);

        var (duration, width, height) = await GetDimensionsAsync(entityId, cancellationToken);

        if (settings.GenerateTrickplay && duration is null or <= 0) {
            await context.EnqueueIfNeededAsync(
                EnqueueJobRequest.ForEntity(
                    JobType.ProbeVideo,
                    EntityKind.Video,
                    entityId.ToString(),
                    context.Job.TargetLabel,
                    JobPriorities.Probe),
                cancellationToken);
            throw new InvalidOperationException($"Cannot generate trickplay for {entityId} until video probe metadata is available.");
        }

        if (settings.AutoGeneratePreview) {
            using (timer.Phase("thumbnail+preview")) {
                await context.ReportProgressAsync(10, "Generating thumbnail and preview", cancellationToken);
                await GenerateThumbnailAndPreviewAsync(entityId, filePath, settings, duration, width, height, cancellationToken);
            }
            // Derive the small grid-card variant from the freshly generated cover.
            await gridThumbnails.EnsureAsync(entityId, cancellationToken);
        }

        if (settings.GenerateTrickplay) {
            using (timer.Phase("trickplay")) {
                await context.ReportProgressAsync(50, "Generating trickplay tiles", cancellationToken);
                var trickplayGenerated = await GenerateTrickplayBatchAsync(
                    entityId, filePath, settings, duration, width, height, cancellationToken);
                if (!trickplayGenerated) {
                    throw new InvalidOperationException($"Failed to generate trickplay tiles for {entityId}.");
                }
            }
        }

        var report = timer.Finish();
        logger.LogInformation(
            "[METRICS] generate-preview {Label} — {Timing}",
            context.Job.TargetLabel, report.ToLogString());
        await context.ReportProgressAsync(100, "Preview complete", cancellationToken);
    }

    private async Task GenerateThumbnailAndPreviewAsync(
        Guid entityId, string filePath, LibrarySettingsData settings,
        double? duration, int? width, int? height, CancellationToken cancellationToken) {
        var thumbPath = assets.VideoThumbnailPath(entityId);
        var previewPath = assets.VideoPreviewPath(entityId);

        var seekTime = ComputeSeekTime(duration);
        var thumbWidth = ScaleWidth(width ?? 1920, settings.ThumbnailQuality);
        var thumbHeight = ScaleHeight(height ?? 1080, width ?? 1920, thumbWidth);

        var clipDuration = Math.Max(4, settings.PreviewClipDurationSeconds);
        var previewStart = (duration ?? 0) > clipDuration ? (duration!.Value * 0.1) : 0;

        var (thumbOk, previewOk) = await assets.GenerateThumbnailAndPreviewAsync(
            filePath,
            thumbPath, seekTime, thumbWidth, thumbHeight, QualityToJpeg(settings.ThumbnailQuality),
            previewPath, previewStart, clipDuration,
            cancellationToken);

        if (thumbOk) {
            var size = new FileInfo(thumbPath).Length;
            await Persistence.UpsertEntityFileAsync(entityId, EntityFileRole.Thumbnail,
                assets.VideoThumbnailUrl(entityId), MediaContentTypes.ImageJpeg, size, cancellationToken);
        }

        if (previewOk) {
            var size = new FileInfo(previewPath).Length;
            await Persistence.UpsertEntityFileAsync(entityId, EntityFileRole.Preview,
                assets.VideoPreviewUrl(entityId), MediaContentTypes.VideoMp4, size, cancellationToken);
        }
    }

    private async Task<bool> GenerateTrickplayBatchAsync(
        Guid entityId, string filePath, LibrarySettingsData settings,
        double? duration, int? width, int? height, CancellationToken cancellationToken) {
        if (duration is null or <= 0) return true;

        var interval = Math.Max(3, settings.TrickplayIntervalSeconds);
        var frameCount = (int)(duration.Value / interval);
        if (frameCount < 1) return true;

        var (frameWidth, frameHeight) = ComputeTrickplayDimensions(
            width ?? 1920, height ?? 1080, settings.TrickplayQuality);

        var frameDir = assets.TrickplayFrameDir(entityId);

        var extractedCount = await assets.ExtractTrickplayFramesBatchAsync(
            filePath, frameDir, duration.Value, interval,
            frameWidth, frameHeight, QualityToJpeg(settings.TrickplayQuality),
            cancellationToken);

        if (extractedCount == 0) {
            logger.LogWarning("Trickplay batch extraction produced zero frames for {EntityId}", entityId);
            return false;
        }

        logger.LogInformation(
            "Trickplay: extracted {Count} frames in single pass (expected {Expected})",
            extractedCount, frameCount);

        const int columns = 5;
        const int rows = 5;
        var tileDir = assets.TrickplayTileDir(entityId, frameWidth);
        var tileCount = await assets.ComposeTiledJpegSheetsAsync(
            frameDir, tileDir, columns, rows, frameWidth, frameHeight,
            QualityToJpeg(settings.TrickplayQuality), cancellationToken);

        if (tileCount == 0) {
            logger.LogWarning("Failed to compose trickplay tiles for {EntityId}", entityId);
            return false;
        }

        await Persistence.UpsertTrickplayInfoAsync(
            entityId,
            new TrickplayInfoData(
                frameWidth,
                frameHeight,
                columns,
                rows,
                extractedCount,
                interval,
                EstimateTrickplayBandwidth(tileDir, tileCount, extractedCount, interval)),
            cancellationToken);

        await Persistence.UpsertEntityFileAsync(entityId, EntityFileRole.Trickplay,
            assets.TrickplayPlaylistUrl(entityId, frameWidth), MediaContentTypes.HlsPlaylist, null, cancellationToken);

        return true;
    }

    private async Task<(double? Duration, int? Width, int? Height)> GetDimensionsAsync(
        Guid entityId, CancellationToken cancellationToken) {
        var tech = await Persistence.GetEntityTechnicalAsync(entityId, cancellationToken);
        if (tech is null)
            return (null, null, null);

        return (tech.DurationSeconds, tech.Width, tech.Height);
    }

    private static double ComputeSeekTime(double? duration) {
        var seekTime = Math.Max(0, (duration ?? 10) * 0.18);
        if (duration is not null && seekTime > duration.Value - 0.5)
            seekTime = Math.Max(0, duration.Value - 0.5);
        return seekTime;
    }

    private static int ScaleWidth(int sourceWidth, int quality) {
        var min = 320;
        var max = sourceWidth;
        var factor = Math.Clamp(quality, 1, 31);
        return min + (max - min) * (31 - factor) / 30;
    }

    private static int ScaleHeight(int sourceHeight, int sourceWidth, int targetWidth) {
        if (sourceWidth == 0) return sourceHeight;
        return targetWidth * sourceHeight / sourceWidth;
    }

    /// <summary>
    /// Trickplay frames are small scrubber-preview thumbnails, not full-resolution images.
    /// Capped at 320×180 regardless of source resolution (matching v1 behavior).
    /// Quality 1 (best) = 320w, quality 5 (lowest) = 160w.
    /// </summary>
    private static (int Width, int Height) ComputeTrickplayDimensions(int sourceWidth, int sourceHeight, int quality) {
        const int maxWidth = 320;
        const int minWidth = 160;
        var q = Math.Clamp(quality, 1, 5);
        var targetWidth = maxWidth - (q - 1) * (maxWidth - minWidth) / 4;
        targetWidth = targetWidth / 2 * 2;

        var targetHeight = sourceWidth > 0
            ? targetWidth * sourceHeight / sourceWidth
            : targetWidth * 9 / 16;
        targetHeight = targetHeight / 2 * 2;

        return (targetWidth, Math.Max(2, targetHeight));
    }

    private static int QualityToJpeg(int quality) => Math.Clamp(quality, 1, 10);

    private static int EstimateTrickplayBandwidth(string tileDir, int tileCount, int thumbnailCount, int interval) {
        var totalBytes = Directory.GetFiles(tileDir, "*.jpg")
            .Take(tileCount)
            .Sum(path => new FileInfo(path).Length);
        var totalSeconds = Math.Max(interval, thumbnailCount * interval);
        return (int)Math.Ceiling(totalBytes * 8d / totalSeconds);
    }
}
