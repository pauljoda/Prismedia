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
        CancellationToken cancellationToken) {
        var results = new List<string>();

        if (!Directory.Exists(rootPath)) {
            return Task.FromResult<IReadOnlyList<string>>(results);
        }

        WalkDirectory(rootPath, extensions, recursive, results, cancellationToken);
        results.Sort(StringComparer.OrdinalIgnoreCase);
        return Task.FromResult<IReadOnlyList<string>>(results);
    }

    /// <summary>
    /// Discovers files and groups them by their parent directory.
    /// Useful for gallery and audio library scanning where directories form logical containers.
    /// </summary>
    public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> DiscoverFilesByDirectoryAsync(
        string rootPath,
        IReadOnlySet<string> extensions,
        bool recursive,
        CancellationToken cancellationToken) {
        var allFiles = new List<string>();

        if (!Directory.Exists(rootPath)) {
            return Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<string>>>(
                new Dictionary<string, IReadOnlyList<string>>());
        }

        WalkDirectory(rootPath, extensions, recursive, allFiles, cancellationToken);

        var grouped = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in allFiles.GroupBy(f => Path.GetDirectoryName(f)!, StringComparer.OrdinalIgnoreCase)) {
            var sorted = group.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
            grouped[group.Key] = sorted;
        }

        return Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<string>>>(grouped);
    }

    private static void WalkDirectory(
        string directory,
        IReadOnlySet<string> extensions,
        bool recursive,
        List<string> results,
        CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        try {
            foreach (var file in Directory.EnumerateFiles(directory)) {
                var ext = Path.GetExtension(file);
                if (ext.Length == 0 || !extensions.Contains(ext))
                    continue;

                var nameWithoutExt = Path.GetFileNameWithoutExtension(file);
                if (SupportedExtensions.IsGeneratedSuffix(nameWithoutExt))
                    continue;

                results.Add(file);
            }

            if (!recursive)
                return;

            foreach (var subDir in Directory.EnumerateDirectories(directory)) {
                var dirName = Path.GetFileName(subDir);
                if (dirName.StartsWith('.'))
                    continue;

                WalkDirectory(subDir, extensions, recursive, results, cancellationToken);
            }
        } catch (UnauthorizedAccessException) {
            // skip inaccessible directories silently
        } catch (DirectoryNotFoundException) {
            // directory was removed between enumeration and access
        }
    }
}
