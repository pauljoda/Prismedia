using Microsoft.Extensions.Logging;
using Prismedia.Application.Files;
using Prismedia.Application.Jobs;
using Prismedia.Application.Jobs.Handlers;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Application.Jobs.Scanning;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers.Scan;

/// <summary>
/// Discovers image files organized by directory, creates gallery and image entities,
/// and chains downstream thumbnail/fingerprint jobs.
/// </summary>
public sealed class ScanGalleryJobHandler(
    ILogger<ScanGalleryJobHandler> logger,
    IFileDiscovery fileDiscovery,
    ILibraryScanRootPersistence roots,
    IImageGalleryScanPersistence images,
    IDownstreamNeedsPersistence downstreamNeeds,
    IScanSnapshotStore? snapshots = null) : ScanJobHandler(logger, fileDiscovery, roots, snapshots) {
    public override JobType Type => JobType.ScanGallery;

    protected override bool IsEligibleRoot(LibraryRootData root) => root.ScanImages;

    protected override IReadOnlyList<MediaCategory> ScanCategories => [MediaCategory.Image];

    protected override Task OnNoFileChangesAsync(
        JobContext context, LibraryRootData root, CancellationToken cancellationToken) =>
        AutoIdentifyScanEnqueue.EnqueueExistingRootsForUnchangedScanAsync(
            context, Roots, downstreamNeeds, root, ScanCategories, cancellationToken);

    protected override async Task<ScanRootOutcome> ScanRootCoreAsync(JobContext context, LibraryRootData root, CancellationToken cancellationToken) {
        logger.LogInformation("ScanGallery: discovering images in {Path}", root.Path);
        var excludedPaths = await Roots.GetExcludedPathsForRootAsync(root.Id, cancellationToken);

        var dirGroups = await FileDiscovery.DiscoverFilesByDirectoryAsync(
            root.Path, MediaCategory.Image, root.Recursive, excludedPaths, cancellationToken);

        logger.LogInformation("ScanGallery: found {DirCount} directories with images in {Label}",
            dirGroups.Count, root.Label);

        var settings = await Roots.GetSettingsAsync(cancellationToken);
        if (!root.AutoIdentify) {
            // Honor this root's Auto Identify opt-out without touching other generation settings.
            settings = settings with { AutoIdentifyEnabled = false };
        }
        var allContainerPaths = ContainerPathsFor(root.Path, dirGroups.Keys);
        // A folder that directly holds exactly one image and has no nested gallery is "collapsed":
        // its single image is reparented to the nearest surviving ancestor gallery (or becomes a
        // loose image when no ancestor survives), and the folder itself never becomes a gallery.
        // This keeps single-file download folders from cluttering the library, and migrates a
        // previously persisted single-image gallery on re-scan because the dropped folder is removed
        // by stale cleanup after its image has been reparented.
        var collapsedTargets = ComputeCollapsedGalleries(root.Path, dirGroups, allContainerPaths);
        var collapsedFolders = collapsedTargets.Keys.ToHashSet(FileSystemPathComparison.Comparer);
        var validGalleryPaths = allContainerPaths
            .Where(path => !collapsedFolders.Contains(path))
            .ToArray();
        var galleryIdsByPath = new Dictionary<string, Guid>(FileSystemPathComparison.Comparer);
        var siblingSortOrders = SiblingSortOrders(validGalleryPaths);
        // Galleries and loose images are auto-identify candidates; resolution collapses nested
        // galleries to their top gallery so only the top-level container is queued.
        var autoIdentifyIds = new List<Guid>();

        foreach (var galleryLevel in validGalleryPaths
            .GroupBy(path => PathDepth(root.Path, path))
            .OrderBy(group => group.Key)) {
            var galleryItems = galleryLevel
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(dirPath => {
                    var parentPath = NearestSurvivingAncestor(dirPath, root.Path, collapsedFolders);
                    Guid? parentGalleryId = parentPath is not null ? galleryIdsByPath[parentPath] : null;
                    return new GalleryUpsertItem(
                        dirPath,
                        Path.GetFileName(dirPath),
                        root.Id,
                        parentGalleryId,
                        siblingSortOrders[dirPath],
                        root.IsNsfw);
                })
                .ToArray();

            var galleryIds = await images.UpsertGalleriesBatchAsync(galleryItems, cancellationToken);
            for (var i = 0; i < galleryItems.Length && i < galleryIds.Count; i++) {
                galleryIdsByPath[galleryItems[i].FolderPath] = galleryIds[i];
                autoIdentifyIds.Add(galleryIds[i]);
            }
        }

        var validLooseImagePaths = new HashSet<string>(FileSystemPathComparison.Comparer);
        var validImagePathsByGalleryPath = validGalleryPaths.ToDictionary(
            path => path,
            _ => new HashSet<string>(FileSystemPathComparison.Comparer),
            FileSystemPathComparison.Comparer);
        var orderedDirGroups = dirGroups
            .OrderBy(group => PathDepth(root.Path, group.Key))
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var processedDirs = 0;
        // Tracks the next sort order per surviving gallery so a lone image merged in from a collapsed
        // child folder appends after the gallery's own images instead of colliding at index 0.
        var nextSortOrderByGalleryPath = new Dictionary<string, int>(FileSystemPathComparison.Comparer);
        var imageItems = new List<ImageUpsertItem>();

        foreach (var (dirPath, imageFiles) in orderedDirGroups) {
            var isRootDirectory = SamePath(dirPath, root.Path);
            // Collapsed single-image folders route their lone image to a surviving ancestor gallery,
            // or to the loose-image bucket when no ancestor survives (target path is null).
            var targetGalleryPath = isRootDirectory
                ? null
                : collapsedTargets.TryGetValue(dirPath, out var survivor)
                    ? survivor
                    : dirPath;
            var validImagePaths = targetGalleryPath is null
                ? validLooseImagePaths
                : validImagePathsByGalleryPath[targetGalleryPath];
            var galleryId = targetGalleryPath is null ? (Guid?)null : galleryIdsByPath[targetGalleryPath];
            var orderedImageFiles = imageFiles.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();

            for (var i = 0; i < orderedImageFiles.Length; i++) {
                var filePath = orderedImageFiles[i];
                var title = Path.GetFileNameWithoutExtension(filePath);
                validImagePaths.Add(filePath);

                long? size = null;
                try { size = new FileInfo(filePath).Length; } catch { }

                var sortOrder = targetGalleryPath is null ? i : NextSortOrder(nextSortOrderByGalleryPath, targetGalleryPath);
                imageItems.Add(new ImageUpsertItem(filePath, title, galleryId, size, sortOrder, root.IsNsfw));
            }

            processedDirs++;

            if (processedDirs % 10 == 0) {
                await context.ReportProgressAsync(processedDirs * 80 / dirGroups.Count,
                    $"Processed {processedDirs}/{dirGroups.Count} directories", cancellationToken);
            }
        }

        var imageIds = await images.UpsertImagesBatchAsync(imageItems, cancellationToken);
        for (var i = 0; i < imageItems.Count && i < imageIds.Count; i++) {
            var item = imageItems[i];
            var imageId = imageIds[i];

            if (settings.AutoGeneratePreview && await NeedsPreviewAsync(imageId, item.FilePath, cancellationToken)) {
                await context.EnqueueIfNeededAsync(
                    EnqueueJobRequest.ForEntity(
                        JobType.GenerateImageThumbnail,
                        EntityKind.Image,
                        imageId.ToString(),
                        item.Title,
                        JobPriorities.Thumbnail),
                    cancellationToken);
            }

            if (await FingerprintGating.ShouldFingerprintAsync(downstreamNeeds, settings, imageId, cancellationToken)) {
                await context.EnqueueIfNeededAsync(
                    EnqueueJobRequest.ForEntity(
                        JobType.FingerprintImage,
                        EntityKind.Image,
                        imageId.ToString(),
                        item.Title,
                        JobPriorities.Fingerprint),
                    cancellationToken);
            }

            // Loose images (no owning gallery) are their own auto-identify roots; images inside a
            // gallery are covered by that gallery's identification.
            if (item.GalleryEntityId is null) {
                autoIdentifyIds.Add(imageId);
            }
        }

        await images.RemoveStaleLooseImagesInRootAsync(root.Id, validLooseImagePaths, cancellationToken);
        await Roots.RemoveEntitiesInExcludedPathsAsync(root.Id, cancellationToken);

        foreach (var galleryPath in validGalleryPaths) {
            await images.RemoveStaleImagesInGalleryAsync(
                galleryIdsByPath[galleryPath],
                validImagePathsByGalleryPath[galleryPath],
                cancellationToken);
        }

        await images.RemoveStaleGalleriesInRootAsync(
            root.Id,
            validGalleryPaths.ToHashSet(FileSystemPathComparison.Comparer),
            cancellationToken);

        await AutoIdentifyScanEnqueue.EnqueueRootsAsync(context, settings, downstreamNeeds, autoIdentifyIds, cancellationToken);

        return ScanRootOutcome.Success;
    }

    private static bool SamePath(string left, string right) =>
        FileSystemPathComparison.Equals(NormalizePath(left), NormalizePath(right));

    private static string NormalizePath(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private async Task<bool> NeedsPreviewAsync(Guid imageId, string filePath, CancellationToken cancellationToken) {
        if (!await downstreamNeeds.HasEntityFileAsync(imageId, EntityFileRole.Thumbnail, cancellationToken)) {
            return true;
        }

        return AnimatedImagePreviewPolicy.RequiresPreviewClip(filePath) &&
               !await downstreamNeeds.HasEntityFileAsync(imageId, EntityFileRole.Preview, cancellationToken);
    }

    /// <summary>
    /// Identifies folders that should be collapsed instead of becoming galleries: a folder that
    /// directly holds exactly one image and has no descendant folder that also holds images. Each
    /// collapsed folder maps to the path of the nearest surviving ancestor gallery, or null when the
    /// image should become a loose image at the root.
    /// </summary>
    private static Dictionary<string, string?> ComputeCollapsedGalleries(
        string rootPath,
        IReadOnlyDictionary<string, IReadOnlyList<string>> dirGroups,
        IReadOnlyList<string> containerPaths) {
        var collapsed = new HashSet<string>(FileSystemPathComparison.Comparer);

        foreach (var (dirPath, imageFiles) in dirGroups) {
            if (SamePath(dirPath, rootPath) || imageFiles.Count != 1) {
                continue;
            }

            if (HasDescendantImageDir(dirPath, dirGroups.Keys)) {
                continue;
            }

            collapsed.Add(dirPath);
        }

        var targets = new Dictionary<string, string?>(FileSystemPathComparison.Comparer);
        foreach (var folder in collapsed) {
            targets[folder] = NearestSurvivingAncestor(folder, rootPath, collapsed);
        }

        return targets;
    }

    private static bool HasDescendantImageDir(string folder, IEnumerable<string> dirsWithImages) {
        foreach (var dir in dirsWithImages) {
            if (!SamePath(dir, folder) && IsBelowRoot(folder, dir)) {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Walks up from <paramref name="path"/> toward the root, skipping any collapsed folders, and
    /// returns the first ancestor that survives as a gallery, or null when the root is reached.
    /// </summary>
    private static string? NearestSurvivingAncestor(string path, string rootPath, ISet<string> collapsedFolders) {
        var parent = Path.GetDirectoryName(path);
        while (parent is not null && !SamePath(parent, rootPath)) {
            if (!collapsedFolders.Contains(parent)) {
                return parent;
            }

            parent = Path.GetDirectoryName(parent);
        }

        return null;
    }

    private static int NextSortOrder(Dictionary<string, int> counters, string galleryPath) {
        var value = counters.TryGetValue(galleryPath, out var current) ? current : 0;
        counters[galleryPath] = value + 1;
        return value;
    }

    private static IReadOnlyList<string> ContainerPathsFor(string rootPath, IEnumerable<string> directoryPaths) {
        var containers = new HashSet<string>(FileSystemPathComparison.Comparer);

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
        var sortOrders = new Dictionary<string, int>(FileSystemPathComparison.Comparer);

        foreach (var siblings in folderPaths.GroupBy(
                     path => Path.GetDirectoryName(path) ?? string.Empty,
                     FileSystemPathComparison.Comparer)) {
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
