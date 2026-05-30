using Prismedia.Application.Jobs.Handlers;
using Microsoft.Extensions.Logging;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers.Generate;

/// <summary>
/// Generates a thumbnail for an image entity by scaling it to 640px width through the
/// shared image resizer (in-process SkiaSharp, with an ffmpeg fallback for exotic formats).
/// </summary>
public sealed class GenerateImageThumbnailJobHandler(
    ILogger<GenerateImageThumbnailJobHandler> logger,
    IMediaAssetGenerator assets,
    IImageThumbnailGenerator imageThumbnails,
    IMediaProcessingStatePersistence persistence) : EntityFileJobHandler(logger, persistence) {
    public override JobType Type => JobType.GenerateImageThumbnail;

    protected override async Task ExecuteAsync(
        JobContext context, Guid entityId, string filePath, CancellationToken cancellationToken) {
        await context.ReportProgressAsync(20, "Generating thumbnail", cancellationToken);

        var thumbPath = assets.ImageThumbnailPath(entityId);
        var success = await imageThumbnails.GenerateAsync(filePath, thumbPath, 640, 80, cancellationToken);

        if (success) {
            var size = new FileInfo(thumbPath).Length;
            await Persistence.UpsertEntityFileAsync(entityId, EntityFileRole.Thumbnail, assets.ImageThumbnailUrl(entityId), "image/jpeg", size, cancellationToken);
            logger.LogInformation("GenerateImageThumbnail: created thumbnail for {Label}", context.Job.TargetLabel);
        } else {
            logger.LogWarning("GenerateImageThumbnail: failed for {Label}", context.Job.TargetLabel);
        }

        await context.ReportProgressAsync(100, "Thumbnail complete", cancellationToken);
    }
}
