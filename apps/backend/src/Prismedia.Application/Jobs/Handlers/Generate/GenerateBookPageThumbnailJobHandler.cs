using Prismedia.Application.Jobs.Handlers;
using Prismedia.Application.Files;
using System.IO.Compression;
using Microsoft.Extensions.Logging;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Contracts.Media;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers.Generate;

/// <summary>
/// Generates a thumbnail for a comic book page entity by extracting the image from the
/// archive and scaling it through the shared image resizer (in-process SkiaSharp, with an
/// ffmpeg fallback for exotic formats).
/// </summary>
public sealed class GenerateBookPageThumbnailJobHandler(
    ILogger<GenerateBookPageThumbnailJobHandler> logger,
    IMediaAssetGenerator assets,
    IImageThumbnailGenerator imageThumbnails,
    IMediaProcessingStatePersistence persistence) : EntityFileJobHandler(logger, persistence) {
    public override JobType Type => JobType.GenerateBookPageThumbnail;

    protected override bool ValidateFilePath(string filePath) {
        return EntitySourcePath.TrySplitArchiveMember(filePath, out var archivePath, out _)
            && File.Exists(archivePath);
    }

    protected override async Task ExecuteAsync(
        JobContext context, Guid entityId, string filePath, CancellationToken cancellationToken) {
        await context.ReportProgressAsync(20, "Extracting page", cancellationToken);

        if (!EntitySourcePath.TrySplitArchiveMember(filePath, out var archivePath, out var memberPath)) {
            return;
        }

        var tempPath = Path.Combine(Path.GetTempPath(), $"prismedia-page-{entityId}{Path.GetExtension(memberPath)}");
        try {
            if (!ExtractZipMember(archivePath, memberPath, tempPath)) {
                logger.LogWarning("GenerateBookPageThumbnail: failed to extract {Member} from {Archive}", memberPath, archivePath);
                return;
            }

            await context.ReportProgressAsync(60, "Generating thumbnail", cancellationToken);

            var thumbPath = assets.BookPageThumbnailPath(entityId);
            var success = await imageThumbnails.GenerateAsync(tempPath, thumbPath, 640, 80, cancellationToken);

            if (success) {
                var size = new FileInfo(thumbPath).Length;
                await Persistence.UpsertEntityFileAsync(entityId, EntityFileRole.Thumbnail, assets.BookPageThumbnailUrl(entityId), MediaContentTypes.ImageJpeg, size, cancellationToken);
                logger.LogInformation("GenerateBookPageThumbnail: created thumbnail for {Label}", context.Job.TargetLabel);
            }
        } finally {
            try { File.Delete(tempPath); } catch { }
        }

        await context.ReportProgressAsync(100, "Thumbnail complete", cancellationToken);
    }

    private static bool ExtractZipMember(string archivePath, string memberPath, string outputPath) {
        try {
            using var archive = ZipFile.OpenRead(archivePath);
            var entry = archive.GetEntry(memberPath);
            if (entry is null) return false;

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            entry.ExtractToFile(outputPath, overwrite: true);
            return true;
        } catch {
            return false;
        }
    }
}
