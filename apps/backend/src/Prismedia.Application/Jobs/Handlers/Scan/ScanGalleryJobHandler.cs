using Prismedia.Application.Jobs.Handlers;
using Microsoft.Extensions.Logging;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers.Scan;

/// <summary>
/// Discovers image files organized by directory, creates gallery and image entities,
/// and chains downstream thumbnail/fingerprint jobs.
/// </summary>
public sealed class ScanGalleryJobHandler(
    ILogger<ScanGalleryJobHandler> logger,
    IFileDiscovery fileDiscovery,
    ILibraryScanPersistence persistence) : ScanJobHandler(logger, fileDiscovery, persistence) {
    public override JobType Type => JobType.ScanGallery;

    protected override bool IsEligibleRoot(LibraryRootData root) => root.ScanImages;

    protected override async Task ScanRootAsync(JobContext context, LibraryRootData root, CancellationToken cancellationToken) {
        logger.LogInformation("ScanGallery: discovering images in {Path}", root.Path);

        var dirGroups = await FileDiscovery.DiscoverFilesByDirectoryAsync(
            root.Path, MediaCategory.Image, root.Recursive, cancellationToken);

        logger.LogInformation("ScanGallery: found {DirCount} directories with images in {Label}",
            dirGroups.Count, root.Label);

        var settings = await Persistence.GetSettingsAsync(cancellationToken);
        var validGalleryPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var processedDirs = 0;

        foreach (var (dirPath, imageFiles) in dirGroups) {
            var galleryTitle = Path.GetFileName(dirPath);
            validGalleryPaths.Add(dirPath);

            var galleryId = await Persistence.UpsertGalleryAsync(dirPath, galleryTitle, root.Id, root.IsNsfw, cancellationToken);
            var validImagePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < imageFiles.Count; i++) {
                var filePath = imageFiles[i];
                var title = Path.GetFileNameWithoutExtension(filePath);
                validImagePaths.Add(filePath);

                long? size = null;
                try { size = new FileInfo(filePath).Length; } catch { }

                var imageId = await Persistence.UpsertImageAsync(filePath, title, galleryId, size, i, root.IsNsfw, cancellationToken);

                if (settings.AutoGeneratePreview && !await Persistence.HasEntityFileAsync(imageId, EntityFileRole.Thumbnail, cancellationToken)) {
                    await context.EnqueueIfNeededAsync(new EnqueueJobRequest(
                        JobType.GenerateImageThumbnail, TargetEntityKind: "image",
                        TargetEntityId: imageId.ToString(), TargetLabel: title), cancellationToken);
                }

                if (settings.AutoGenerateFingerprints && !await Persistence.HasEntityFingerprintAsync(imageId, FingerprintAlgorithm.Md5, cancellationToken)) {
                    await context.EnqueueIfNeededAsync(new EnqueueJobRequest(
                        JobType.FingerprintImage, TargetEntityKind: "image",
                        TargetEntityId: imageId.ToString(), TargetLabel: title), cancellationToken);
                }
            }

            await Persistence.RemoveStaleImagesInGalleryAsync(galleryId, validImagePaths, cancellationToken);
            processedDirs++;

            if (processedDirs % 10 == 0) {
                await context.ReportProgressAsync(processedDirs * 80 / dirGroups.Count,
                    $"Processed {processedDirs}/{dirGroups.Count} directories", cancellationToken);
            }
        }

        await Persistence.RemoveStaleGalleriesInRootAsync(root.Id, validGalleryPaths, cancellationToken);
    }
}
