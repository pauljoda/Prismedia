using Prismedia.Application.Jobs.Handlers;
using Microsoft.Extensions.Logging;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers.Scan;

/// <summary>
/// Discovers audio files organized by directory, creates audio library and track entities,
/// and chains downstream probe/fingerprint jobs.
/// </summary>
public sealed class ScanAudioJobHandler(
    ILogger<ScanAudioJobHandler> logger,
    IFileDiscovery fileDiscovery,
    ILibraryScanPersistence persistence) : ScanJobHandler(logger, fileDiscovery, persistence) {
    public override JobType Type => JobType.ScanAudio;

    protected override bool IsEligibleRoot(LibraryRootData root) => root.ScanAudio;

    protected override async Task ScanRootAsync(JobContext context, LibraryRootData root, CancellationToken cancellationToken) {
        logger.LogInformation("ScanAudio: discovering audio files in {Path}", root.Path);

        var dirGroups = await FileDiscovery.DiscoverFilesByDirectoryAsync(
            root.Path, MediaCategory.Audio, root.Recursive, cancellationToken);

        logger.LogInformation("ScanAudio: found {DirCount} directories with audio in {Label}",
            dirGroups.Count, root.Label);

        var settings = await Persistence.GetSettingsAsync(cancellationToken);
        var validLibraryPaths = ContainerPathsFor(root.Path, dirGroups.Keys);
        var libraryIdsByPath = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        var siblingSortOrders = SiblingSortOrders(validLibraryPaths);

        foreach (var dirPath in validLibraryPaths) {
            var libraryTitle = Path.GetFileName(dirPath);
            var parentPath = Path.GetDirectoryName(dirPath);
            Guid? parentLibraryId = parentPath is not null && !SamePath(parentPath, root.Path)
                ? libraryIdsByPath[parentPath]
                : null;

            var libraryId = await Persistence.UpsertAudioLibraryAsync(
                dirPath,
                libraryTitle,
                root.Id,
                parentLibraryId,
                siblingSortOrders[dirPath],
                root.IsNsfw,
                cancellationToken);
            libraryIdsByPath[dirPath] = libraryId;
        }

        var validLooseTrackPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var validTrackPathsByLibraryPath = validLibraryPaths.ToDictionary(
            path => path,
            _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
        var orderedDirGroups = dirGroups
            .OrderBy(group => PathDepth(root.Path, group.Key))
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var processedDirs = 0;

        foreach (var (dirPath, audioFiles) in orderedDirGroups) {
            var isRootDirectory = SamePath(dirPath, root.Path);
            var validTrackPaths = isRootDirectory
                ? validLooseTrackPaths
                : validTrackPathsByLibraryPath[dirPath];
            var libraryId = isRootDirectory ? (Guid?)null : libraryIdsByPath[dirPath];
            var orderedAudioFiles = audioFiles.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();

            for (var i = 0; i < orderedAudioFiles.Length; i++) {
                var filePath = orderedAudioFiles[i];
                var title = Path.GetFileNameWithoutExtension(filePath);
                validTrackPaths.Add(filePath);

                var trackId = await Persistence.UpsertAudioTrackAsync(filePath, title, libraryId, i, root.IsNsfw, cancellationToken);

                if (settings.AutoGenerateMetadata && !await Persistence.HasEntityTechnicalAsync(trackId, cancellationToken)) {
                    await context.EnqueueIfNeededAsync(new EnqueueJobRequest(
                        JobType.ProbeAudio, TargetEntityKind: "audio-track",
                        TargetEntityId: trackId.ToString(), TargetLabel: title), cancellationToken);
                }

                if (settings.AutoGenerateFingerprints && !await Persistence.HasEntityFingerprintAsync(trackId, FingerprintAlgorithm.Md5, cancellationToken)) {
                    await context.EnqueueIfNeededAsync(new EnqueueJobRequest(
                        JobType.FingerprintAudio, TargetEntityKind: "audio-track",
                        TargetEntityId: trackId.ToString(), TargetLabel: title), cancellationToken);
                }
            }

            processedDirs++;

            if (processedDirs % 10 == 0) {
                await context.ReportProgressAsync(processedDirs * 80 / dirGroups.Count,
                    $"Processed {processedDirs}/{dirGroups.Count} directories", cancellationToken);
            }
        }

        await Persistence.RemoveStaleLooseAudioTracksInRootAsync(root.Id, validLooseTrackPaths, cancellationToken);

        foreach (var libraryPath in validLibraryPaths) {
            await Persistence.RemoveStaleAudioTracksInLibraryAsync(
                libraryIdsByPath[libraryPath],
                validTrackPathsByLibraryPath[libraryPath],
                cancellationToken);
        }

        await Persistence.RemoveStaleAudioLibrariesInRootAsync(root.Id, validLibraryPaths.ToHashSet(StringComparer.OrdinalIgnoreCase), cancellationToken);
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
