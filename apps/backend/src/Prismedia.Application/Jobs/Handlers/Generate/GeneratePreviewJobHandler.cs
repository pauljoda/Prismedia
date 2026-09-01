using Prismedia.Application.Jobs.Handlers;
using Microsoft.Extensions.Logging;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Contracts.Media;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers.Generate;

/// <summary>
/// Generates the thumbnail and short preview assets needed by library surfaces.
/// Long-running trickplay generation is dispatched independently so it cannot delay these assets.
/// </summary>
[JobDefinition(JobType.GeneratePreview, ResourceClass = JobResourceClass.HeavyCpu, Importance = JobNodeImportance.BestEffort)]
public sealed class GeneratePreviewJobHandler(
    ILogger<GeneratePreviewJobHandler> logger,
    IMediaAssetGenerator assets,
    IMediaProcessingStatePersistence persistence,
    ILibraryScanRootPersistence roots,
    IGridThumbnailService gridThumbnails) : EntityFileJobHandler(logger, persistence) {
    protected override async Task ExecuteAsync(
        JobContext context, Guid entityId, string filePath, CancellationToken cancellationToken) {
        var timer = new JobPhaseTimer();
        var settings = await roots.GetSettingsAsync(cancellationToken);

        var tech = await Persistence.GetEntityTechnicalAsync(entityId, cancellationToken);
        var (duration, width, height) = (tech?.DurationSeconds, tech?.Width, tech?.Height);

        if (duration is null or <= 0 && tech?.ProbeFailedAt is not null) {
            // The probe already established the source is unreadable (corrupt media); ffmpeg
            // cannot generate anything from it either. Complete quietly — the probe failure
            // surfaced the bad file, and the marker keeps scans from re-queueing this work.
            logger.LogWarning(
                "GeneratePreview: skipping {EntityId} — source file could not be probed (marked unreadable)",
                entityId);
            await context.ReportProgressAsync(
                100, "Skipped: source file could not be read (corrupt or truncated)", cancellationToken);
            return;
        }

        if (settings.AutoGeneratePreview) {
            using (timer.Phase("thumbnail+preview")) {
                await context.ReportProgressAsync(10, "Generating thumbnail and preview", cancellationToken);
                await GenerateThumbnailAndPreviewAsync(entityId, filePath, settings, duration, width, height, cancellationToken);
            }
            // Derive the small grid-card variant from the freshly generated cover.
            await gridThumbnails.EnsureAsync(entityId, cancellationToken);
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

    private static double ComputeSeekTime(double? duration) {
        var seekTime = Math.Max(0, (duration ?? 10) * 0.18);
        if (duration is not null && seekTime > duration.Value - 0.5)
            seekTime = Math.Max(0, duration.Value - 0.5);
        return seekTime;
    }

    /// <summary>
    /// Maximum generated-thumbnail width per quality preset (1 = Best … 5 = Lowest),
    /// never upscaling past the source. Detail/hero surfaces are the largest consumers
    /// of this image; grid cards use the separate 480/960 grid variants derived from it,
    /// so scaling to near-source resolution only produces oversized files.
    /// </summary>
    private static int ScaleWidth(int sourceWidth, int quality) {
        var maxWidth = Math.Clamp(quality, 1, 5) switch {
            1 => 1920,
            2 => 1280,
            3 => 1024,
            4 => 800,
            _ => 640
        };
        return Math.Min(sourceWidth, maxWidth);
    }

    private static int ScaleHeight(int sourceHeight, int sourceWidth, int targetWidth) {
        if (sourceWidth == 0) return sourceHeight;
        return targetWidth * sourceHeight / sourceWidth;
    }

    /// <summary>
    /// Maps the 1–5 quality preset onto ffmpeg's inverse -q:v scale (2 ≈ visually
    /// lossless, 31 = worst). Preset 1 (Best) → 2, preset 5 (Lowest) → 6.
    /// </summary>
    private static int QualityToJpeg(int quality) => Math.Clamp(quality + 1, 2, 6);

}
