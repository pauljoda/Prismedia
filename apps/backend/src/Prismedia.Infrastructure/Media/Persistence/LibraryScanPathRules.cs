using Prismedia.Application.Files;

namespace Prismedia.Infrastructure.Media.Persistence;

internal static class LibraryScanPathRules {
    public static bool IsDirectChildPath(string path, string parentPath) {
        var normalizedPath = NormalizePath(path);
        var normalizedParent = NormalizePath(parentPath);

        if (string.IsNullOrWhiteSpace(normalizedPath) || string.IsNullOrWhiteSpace(normalizedParent)) {
            return false;
        }

        var directory = Path.GetDirectoryName(normalizedPath)?.Replace('\\', '/').TrimEnd('/');
        return string.Equals(directory, normalizedParent, FileSystemPathComparison.Comparison);
    }

    public static bool IsPathUnderRoot(string path, string rootPath) {
        var normalizedPath = NormalizePath(path);
        var normalizedRoot = NormalizePath(rootPath);

        if (string.IsNullOrWhiteSpace(normalizedPath) || string.IsNullOrWhiteSpace(normalizedRoot)) {
            return false;
        }

        return normalizedPath.Equals(normalizedRoot, FileSystemPathComparison.Comparison) ||
            normalizedPath.StartsWith(normalizedRoot + "/", FileSystemPathComparison.Comparison);
    }

    public static bool IsPathCoveredByExclusion(string path, string excludedPath) {
        var normalizedPath = NormalizePath(path);
        var normalizedExcluded = NormalizePath(excludedPath);

        return normalizedPath.Equals(normalizedExcluded, FileSystemPathComparison.Comparison) ||
            normalizedPath.StartsWith(normalizedExcluded + "/", FileSystemPathComparison.Comparison) ||
            normalizedPath.StartsWith(
                normalizedExcluded + EntitySourcePath.ArchiveMemberSeparator,
                FileSystemPathComparison.Comparison);
    }

    private static string NormalizePath(string path) =>
        path.Replace('\\', '/').TrimEnd('/');
}
