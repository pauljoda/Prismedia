using Prismedia.Application.Jobs.Handlers;
using Microsoft.Extensions.Logging;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Contracts.Media;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers.Generate;

/// <summary>
/// Generates a thumbnail for an image entity by scaling it to 640px width through the
/// shared image resizer (in-process SkiaSharp, with an ffmpeg fallback for exotic formats).
/// </summary>
[JobDefinition(JobType.GenerateImageThumbnail, ResourceClass = JobResourceClass.StandardCpu, Importance = JobNodeImportance.BestEffort, BlocksAutoIdentify = true)]
public sealed class GenerateImageThumbnailJobHandler(
    ILogger<GenerateImageThumbnailJobHandler> logger,
    IMediaAssetGenerator assets,
    IImageThumbnailGenerator imageThumbnails,
    IMediaProcessingStatePersistence persistence,
    ILibraryScanRootPersistence roots,
    IGridThumbnailService gridThumbnails) : EntityFileJobHandler(logger, persistence) {
    protected override async Task ExecuteAsync(
        JobContext context, Guid entityId, string filePath, CancellationToken cancellationToken) {
        await context.ReportProgressAsync(20, "Generating thumbnail", cancellationToken);

        var thumbPath = assets.ImageThumbnailPath(entityId);
        var success = await imageThumbnails.GenerateAsync(filePath, thumbPath, 640, 80, cancellationToken);

        if (success) {
            var size = new FileInfo(thumbPath).Length;
            await Persistence.UpsertEntityFileAsync(entityId, EntityFileRole.Thumbnail, assets.ImageThumbnailUrl(entityId), MediaContentTypes.ImageJpeg, size, cancellationToken);
            await gridThumbnails.EnsureAsync(entityId, cancellationToken);
            logger.LogInformation("GenerateImageThumbnail: created thumbnail for {Label}", context.Job.TargetLabel);
        } else {
            logger.LogWarning("GenerateImageThumbnail: failed for {Label}", context.Job.TargetLabel);
        }

        if (AnimatedImagePreviewPolicy.RequiresPreviewClip(filePath)) {
            await context.ReportProgressAsync(65, "Generating animated preview", cancellationToken);
            var settings = await roots.GetSettingsAsync(cancellationToken);
            var previewPath = assets.ImagePreviewPath(entityId);
            var duration = Math.Max(4, settings.PreviewClipDurationSeconds);
            var previewOk = await assets.GeneratePreviewClipAsync(filePath, previewPath, 0, duration, cancellationToken);

            if (previewOk) {
                var previewSize = new FileInfo(previewPath).Length;
                await Persistence.UpsertEntityFileAsync(entityId, EntityFileRole.Preview, assets.ImagePreviewUrl(entityId), MediaContentTypes.VideoMp4, previewSize, cancellationToken);
                logger.LogInformation("GenerateImageThumbnail: created animated preview for {Label}", context.Job.TargetLabel);
            } else {
                logger.LogWarning("GenerateImageThumbnail: failed animated preview for {Label}", context.Job.TargetLabel);
            }
        }

        await context.ReportProgressAsync(100, "Thumbnail complete", cancellationToken);
    }

}
