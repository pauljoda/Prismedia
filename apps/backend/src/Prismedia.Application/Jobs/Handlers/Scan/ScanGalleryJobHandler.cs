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
        var validGalleryPaths = ContainerPathsFor(root.Path, dirGroups.Keys);
        var galleryIdsByPath = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        var siblingSortOrders = SiblingSortOrders(validGalleryPaths);

        foreach (var dirPath in validGalleryPaths) {
            var galleryTitle = Path.GetFileName(dirPath);
            var parentPath = Path.GetDirectoryName(dirPath);
            Guid? parentGalleryId = parentPath is not null && !SamePath(parentPath, root.Path)
                ? galleryIdsByPath[parentPath]
                : null;

            var galleryId = await Persistence.UpsertGalleryAsync(
                dirPath,
                galleryTitle,
                root.Id,
                parentGalleryId,
                siblingSortOrders[dirPath],
                root.IsNsfw,
                cancellationToken);
            galleryIdsByPath[dirPath] = galleryId;
        }

        var validLooseImagePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var validImagePathsByGalleryPath = validGalleryPaths.ToDictionary(
            path => path,
            _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
        var orderedDirGroups = dirGroups
            .OrderBy(group => PathDepth(root.Path, group.Key))
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var processedDirs = 0;

        foreach (var (dirPath, imageFiles) in orderedDirGroups) {
            var isRootDirectory = SamePath(dirPath, root.Path);
            var validImagePaths = isRootDirectory
                ? validLooseImagePaths
                : validImagePathsByGalleryPath[dirPath];
            var galleryId = isRootDirectory ? (Guid?)null : galleryIdsByPath[dirPath];
            var orderedImageFiles = imageFiles.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();

            for (var i = 0; i < orderedImageFiles.Length; i++) {
                var filePath = orderedImageFiles[i];
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

            processedDirs++;

            if (processedDirs % 10 == 0) {
                await context.ReportProgressAsync(processedDirs * 80 / dirGroups.Count,
                    $"Processed {processedDirs}/{dirGroups.Count} directories", cancellationToken);
            }
        }

        await Persistence.RemoveStaleLooseImagesInRootAsync(root.Id, validLooseImagePaths, cancellationToken);

        foreach (var galleryPath in validGalleryPaths) {
            await Persistence.RemoveStaleImagesInGalleryAsync(
                galleryIdsByPath[galleryPath],
                validImagePathsByGalleryPath[galleryPath],
                cancellationToken);
        }

        await Persistence.RemoveStaleGalleriesInRootAsync(root.Id, validGalleryPaths.ToHashSet(StringComparer.OrdinalIgnoreCase), cancellationToken);
    }

    private static bool SamePath(string left, string right) =>
        string.Equals(NormalizePath(left), NormalizePath(right), StringComparison.OrdinalIgnoreCase);

    private static string NormalizePath(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static IReadOnlyList<string> ContainerPathsFor(string rootPath, IEnumerable<string> directoryPaths) {
        var containers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var directoryPath in directoryPaths) {
            if (!IsBelowRoot(rootPath, directoryPath)) {
                continue;
            }

            var current = NormalizePath(directoryPath);
            while (!SamePath(current, rootPath)) {
                containers.Add(current);
                var parent = Path.GetDirectoryName(current);
                if (parent is null) {
                    break;
                }

                current = parent;
            }
        }

        return containers
            .OrderBy(path => PathDepth(rootPath, path))
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsBelowRoot(string rootPath, string path) {
        if (SamePath(rootPath, path)) {
            return false;
        }

        var relative = Path.GetRelativePath(rootPath, path);
        return !relative.Equals("..", StringComparison.Ordinal)
            && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            && !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal)
            && !Path.IsPathRooted(relative);
    }

    private static int PathDepth(string rootPath, string path) {
        var relative = Path.GetRelativePath(rootPath, path);
        return relative == "."
            ? 0
            : relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries).Length;
    }

    private static Dictionary<string, int> SiblingSortOrders(IReadOnlyList<string> folderPaths) {
        var sortOrders = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var siblings in folderPaths.GroupBy(path => Path.GetDirectoryName(path) ?? string.Empty, StringComparer.OrdinalIgnoreCase)) {
            var ordered = siblings
                .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            for (var i = 0; i < ordered.Length; i++) {
                sortOrders[ordered[i]] = i;
            }
        }

        return sortOrders;
    }
}
