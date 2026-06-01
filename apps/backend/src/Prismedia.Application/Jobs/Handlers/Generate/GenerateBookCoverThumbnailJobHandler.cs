using Microsoft.Extensions.Logging;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Contracts.Media;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers.Generate;

/// <summary>
/// Generates the cover thumbnail for a single-file book (EPUB/PDF) by extracting the cover or
/// first page through the format-specific extractor and scaling it via the shared image resizer.
/// </summary>
public sealed class GenerateBookCoverThumbnailJobHandler(
    ILogger<GenerateBookCoverThumbnailJobHandler> logger,
    IMediaAssetGenerator assets,
    IImageThumbnailGenerator imageThumbnails,
    IBookCoverImageExtractor coverExtractor,
    IMediaProcessingStatePersistence persistence) : EntityFileJobHandler(logger, persistence) {
    public override JobType Type => JobType.GenerateBookCoverThumbnail;

    protected override async Task ExecuteAsync(
        JobContext context, Guid entityId, string filePath, CancellationToken cancellationToken) {
        var format = BookFormatFor(filePath);
        if (format is null) {
            logger.LogWarning("GenerateBookCoverThumbnail: unsupported book file {Path}", filePath);
            return;
        }

        await context.ReportProgressAsync(20, "Extracting cover", cancellationToken);

        var tempPath = await coverExtractor.ExtractCoverToTempAsync(filePath, format.Value, entityId, cancellationToken);
        if (tempPath is null) {
            logger.LogWarning("GenerateBookCoverThumbnail: no cover available for {Path}", filePath);
            return;
        }

        try {
            await context.ReportProgressAsync(60, "Generating thumbnail", cancellationToken);

            var thumbPath = assets.BookCoverThumbnailPath(entityId);
            var success = await imageThumbnails.GenerateAsync(tempPath, thumbPath, 640, 80, cancellationToken);

            if (success) {
                var size = new FileInfo(thumbPath).Length;
                await Persistence.UpsertEntityFileAsync(
                    entityId, EntityFileRole.Thumbnail, assets.BookCoverThumbnailUrl(entityId),
                    MediaContentTypes.ImageJpeg, size, cancellationToken);
                logger.LogInformation("GenerateBookCoverThumbnail: created cover for {Label}", context.Job.TargetLabel);
            }
        } finally {
            try { File.Delete(tempPath); } catch { }
        }

        await context.ReportProgressAsync(100, "Cover complete", cancellationToken);
    }

    private static BookFormat? BookFormatFor(string sourcePath) =>
        Path.GetExtension(sourcePath).ToLowerInvariant() switch {
            ".epub" => BookFormat.Epub,
            ".pdf" => BookFormat.Pdf,
            _ => null
        };
}
