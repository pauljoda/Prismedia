using Prismedia.Application.Files;
using Prismedia.Application.Jobs.Scanning;

namespace Prismedia.Infrastructure.Media.Processing;

/// <summary>
/// Discovers media files by walking directory trees and filtering by supported extensions.
/// </summary>
public sealed class FileDiscoveryService {
    /// <summary>
    /// Recursively walks a root path and returns all files whose extensions match the supplied set.
    /// Skips hidden directories (dot-prefixed) and files with generated suffixes.
    /// </summary>
    /// <param name="rootPath">Absolute directory path to scan.</param>
    /// <param name="extensions">Set of extensions including the leading dot.</param>
    /// <param name="recursive">Whether to recurse into subdirectories.</param>
    /// <param name="cancellationToken">Token used to cancel the walk.</param>
    /// <returns>Discovered file paths in sorted order.</returns>
    public Task<IReadOnlyList<string>> DiscoverFilesAsync(
        string rootPath,
        IReadOnlySet<string> extensions,
        bool recursive,
        IReadOnlySet<string>? excludedPaths,
        CancellationToken cancellationToken) {
        var results = new List<string>();

        if (!Directory.Exists(rootPath)) {
            return Task.FromResult<IReadOnlyList<string>>(results);
        }

        WalkDirectory(rootPath, extensions, recursive, NormalizeExcludedPaths(excludedPaths), results.Add, cancellationToken);
        results.Sort(StringComparer.OrdinalIgnoreCase);
        return Task.FromResult<IReadOnlyList<string>>(results);
    }

    /// <summary>
    /// Walks a root path like <see cref="DiscoverFilesAsync"/> but returns each matched file together
    /// with its size and last-write time (UTC ticks) for cheap change detection. Files that disappear
    /// between enumeration and stat are skipped. Results are sorted by path.
    /// </summary>
    public Task<IReadOnlyList<FileSignature>> DiscoverFileSignaturesAsync(
        string rootPath,
        IReadOnlySet<string> extensions,
        bool recursive,
        IReadOnlySet<string>? excludedPaths,
        CancellationToken cancellationToken) {
        var results = new List<FileSignature>();

        if (!Directory.Exists(rootPath)) {
            return Task.FromResult<IReadOnlyList<FileSignature>>(results);
        }

        WalkDirectory(rootPath, extensions, recursive, NormalizeExcludedPaths(excludedPaths), file => {
            try {
                var info = new FileInfo(file);
                results.Add(new FileSignature(file, info.Length, info.LastWriteTimeUtc.Ticks));
            } catch (FileNotFoundException) {
                // File was removed between enumeration and stat; treat as not present.
            } catch (IOException) {
                // Transient access issue; skip rather than fail the whole snapshot.
            }
        }, cancellationToken);
        results.Sort(static (left, right) => string.Compare(left.Path, right.Path, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult<IReadOnlyList<FileSignature>>(results);
    }

    /// <summary>
    /// Discovers files and groups them by their parent directory.
    /// Useful for gallery and audio library scanning where directories form logical containers.
    /// </summary>
    public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> DiscoverFilesByDirectoryAsync(
        string rootPath,
        IReadOnlySet<string> extensions,
        bool recursive,
        IReadOnlySet<string>? excludedPaths,
        CancellationToken cancellationToken) {
        var allFiles = new List<string>();

        if (!Directory.Exists(rootPath)) {
            return Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<string>>>(
                new Dictionary<string, IReadOnlyList<string>>(FileSystemPathComparison.Comparer));
        }

        WalkDirectory(rootPath, extensions, recursive, NormalizeExcludedPaths(excludedPaths), allFiles.Add, cancellationToken);

        var grouped = new Dictionary<string, IReadOnlyList<string>>(FileSystemPathComparison.Comparer);

        foreach (var group in allFiles.GroupBy(
                     f => Path.GetDirectoryName(f)!,
                     FileSystemPathComparison.Comparer)) {
            var sorted = group.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
            grouped[group.Key] = sorted;
        }

        return Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<string>>>(grouped);
    }

    private static void WalkDirectory(
        string directory,
        IReadOnlySet<string> extensions,
        bool recursive,
        IReadOnlySet<string> excludedPaths,
        Action<string> onFile,
        CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        if (IsExcluded(directory, excludedPaths)) {
            return;
        }

        try {
            foreach (var file in Directory.EnumerateFiles(directory)) {
                if (IsExcluded(file, excludedPaths)) {
                    continue;
                }

                var ext = Path.GetExtension(file);
                if (ext.Length == 0 || !extensions.Contains(ext))
                    continue;

                var nameWithoutExt = Path.GetFileNameWithoutExtension(file);
                if (SupportedExtensions.IsGeneratedSuffix(nameWithoutExt))
                    continue;

                onFile(file);
            }

            if (!recursive)
                return;

            foreach (var subDir in Directory.EnumerateDirectories(directory)) {
                if (IsExcluded(subDir, excludedPaths)) {
                    continue;
                }

                var dirName = Path.GetFileName(subDir);
                if (dirName.StartsWith('.'))
                    continue;

                WalkDirectory(subDir, extensions, recursive, excludedPaths, onFile, cancellationToken);
            }
        } catch (UnauthorizedAccessException) {
            // skip inaccessible directories silently
        } catch (DirectoryNotFoundException) {
            // directory was removed between enumeration and access
        }
    }

    private static IReadOnlySet<string> NormalizeExcludedPaths(IReadOnlySet<string>? excludedPaths) =>
        excludedPaths is null || excludedPaths.Count == 0
            ? new HashSet<string>(FileSystemPathComparison.Comparer)
            : excludedPaths
                .Select(path => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)))
                .ToHashSet(FileSystemPathComparison.Comparer);

    private static bool IsExcluded(string path, IReadOnlySet<string> excludedPaths) {
        if (excludedPaths.Count == 0) {
            return false;
        }

        var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        return excludedPaths.Any(excluded =>
            FileSystemPathComparison.Equals(normalized, excluded) ||
            normalized.StartsWith(
                excluded + Path.DirectorySeparatorChar,
                FileSystemPathComparison.Comparison));
    }
}
