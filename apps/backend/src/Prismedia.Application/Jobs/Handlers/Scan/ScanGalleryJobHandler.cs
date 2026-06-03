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
    ILibraryScanRootPersistence roots,
    IImageGalleryScanPersistence images,
    IDownstreamNeedsPersistence downstreamNeeds,
    IScanSnapshotStore? snapshots = null) : ScanJobHandler(logger, fileDiscovery, roots, snapshots) {
    public override JobType Type => JobType.ScanGallery;

    protected override bool IsEligibleRoot(LibraryRootData root) => root.ScanImages;

    protected override IReadOnlyList<MediaCategory> ScanCategories => [MediaCategory.Image];

    protected override async Task ScanRootCoreAsync(JobContext context, LibraryRootData root, CancellationToken cancellationToken) {
        logger.LogInformation("ScanGallery: discovering images in {Path}", root.Path);
        var excludedPaths = await Roots.GetExcludedPathsForRootAsync(root.Id, cancellationToken);

        var dirGroups = await FileDiscovery.DiscoverFilesByDirectoryAsync(
            root.Path, MediaCategory.Image, root.Recursive, excludedPaths, cancellationToken);

        logger.LogInformation("ScanGallery: found {DirCount} directories with images in {Label}",
            dirGroups.Count, root.Label);

        var settings = await Roots.GetSettingsAsync(cancellationToken);
        var allContainerPaths = ContainerPathsFor(root.Path, dirGroups.Keys);
        // A folder that directly holds exactly one image and has no nested gallery is "collapsed":
        // its single image is reparented to the nearest surviving ancestor gallery (or becomes a
        // loose image when no ancestor survives), and the folder itself never becomes a gallery.
        // This keeps single-file download folders from cluttering the library, and migrates a
        // previously persisted single-image gallery on re-scan because the dropped folder is removed
        // by stale cleanup after its image has been reparented.
        var collapsedTargets = ComputeCollapsedGalleries(root.Path, dirGroups, allContainerPaths);
        var collapsedFolders = collapsedTargets.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var validGalleryPaths = allContainerPaths
            .Where(path => !collapsedFolders.Contains(path))
            .ToArray();
        var galleryIdsByPath = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        var siblingSortOrders = SiblingSortOrders(validGalleryPaths);
        // Galleries and loose images are auto-identify candidates; resolution collapses nested
        // galleries to their top gallery so only the top-level container is queued.
        var autoIdentifyIds = new List<Guid>();

        foreach (var dirPath in validGalleryPaths) {
            var galleryTitle = Path.GetFileName(dirPath);
            var parentPath = NearestSurvivingAncestor(dirPath, root.Path, collapsedFolders);
            Guid? parentGalleryId = parentPath is not null ? galleryIdsByPath[parentPath] : null;

            var galleryId = await images.UpsertGalleryAsync(
                dirPath,
                galleryTitle,
                root.Id,
                parentGalleryId,
                siblingSortOrders[dirPath],
                root.IsNsfw,
                cancellationToken);
            galleryIdsByPath[dirPath] = galleryId;
            autoIdentifyIds.Add(galleryId);
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
        // Tracks the next sort order per surviving gallery so a lone image merged in from a collapsed
        // child folder appends after the gallery's own images instead of colliding at index 0.
        var nextSortOrderByGalleryPath = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

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
                var imageId = await images.UpsertImageAsync(filePath, title, galleryId, size, sortOrder, root.IsNsfw, cancellationToken);

                if (settings.AutoGeneratePreview && !await downstreamNeeds.HasEntityFileAsync(imageId, EntityFileRole.Thumbnail, cancellationToken)) {
                    await context.EnqueueIfNeededAsync(new EnqueueJobRequest(
                        JobType.GenerateImageThumbnail, TargetEntityKind: "image",
                        TargetEntityId: imageId.ToString(), TargetLabel: title), cancellationToken);
                }

                if (await FingerprintGating.ShouldFingerprintAsync(downstreamNeeds, settings, imageId, cancellationToken)) {
                    await context.EnqueueIfNeededAsync(new EnqueueJobRequest(
                        JobType.FingerprintImage, TargetEntityKind: "image",
                        TargetEntityId: imageId.ToString(), TargetLabel: title), cancellationToken);
                }

                // Loose images (no owning gallery) are their own auto-identify roots; images inside a
                // gallery are covered by that gallery's identification.
                if (galleryId is null) {
                    autoIdentifyIds.Add(imageId);
                }
            }

            processedDirs++;

            if (processedDirs % 10 == 0) {
                await context.ReportProgressAsync(processedDirs * 80 / dirGroups.Count,
                    $"Processed {processedDirs}/{dirGroups.Count} directories", cancellationToken);
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

        await images.RemoveStaleGalleriesInRootAsync(root.Id, validGalleryPaths.ToHashSet(StringComparer.OrdinalIgnoreCase), cancellationToken);

        await AutoIdentifyScanEnqueue.EnqueueRootsAsync(context, settings, downstreamNeeds, autoIdentifyIds, cancellationToken);
    }

    private static bool SamePath(string left, string right) =>
        string.Equals(NormalizePath(left), NormalizePath(right), StringComparison.OrdinalIgnoreCase);

    private static string NormalizePath(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

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
        var collapsed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (dirPath, imageFiles) in dirGroups) {
            if (SamePath(dirPath, rootPath) || imageFiles.Count != 1) {
                continue;
            }

            if (HasDescendantImageDir(dirPath, dirGroups.Keys)) {
                continue;
            }

            collapsed.Add(dirPath);
        }

        var targets = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
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
