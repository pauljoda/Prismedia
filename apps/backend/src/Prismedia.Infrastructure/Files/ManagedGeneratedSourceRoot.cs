using Prismedia.Application.Files;

namespace Prismedia.Infrastructure.Files;

/// <summary>
/// Defines the application-owned storage boundary for generated canonical source files. It provides
/// the same rooted path contract as a watched library without exposing generated storage in Files UI.
/// </summary>
public sealed class ManagedGeneratedSourceRoot(string dataDirectory) {
    /// <summary>Canonical child area used by normalized comic archives.</summary>
    public const string ComicsArea = "comics";

    /// <summary>Absolute root containing every managed generated-source area.</summary>
    public string RootPath { get; } = Path.Combine(
        Path.GetFullPath(dataDirectory),
        "generated-sources");

    /// <summary>Returns an absolute directory for one canonical generated-source area.</summary>
    public string AreaPath(string area) {
        ArgumentException.ThrowIfNullOrWhiteSpace(area);
        if (area.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0 ||
            area is "." or "..") {
            throw new ArgumentException("A generated-source area must be one path segment.", nameof(area));
        }
        return Path.Combine(RootPath, area);
    }

    /// <summary>
    /// Resolves an exact generated source beneath the owned boundary. The broad storage root itself
    /// is never a valid deletion target.
    /// </summary>
    public bool TryResolve(string path, out ResolvedFilePath resolved) {
        resolved = default!;
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path)) {
            return false;
        }

        string fullPath;
        try {
            fullPath = Path.GetFullPath(path);
        } catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) {
            return false;
        }
        if (FileSystemPathComparison.Equals(fullPath, RootPath) ||
            !FileSystemPathComparison.IsSameOrDescendant(RootPath, fullPath)) {
            return false;
        }

        var root = new FileLibraryRoot(
            Guid.Empty,
            RootPath,
            "Managed generated sources",
            Enabled: true,
            ScanVideos: false,
            ScanImages: false,
            ScanAudio: false,
            ScanBooks: false,
            IsNsfw: false);
        resolved = new ResolvedFilePath(root, Path.GetRelativePath(RootPath, fullPath), fullPath);
        return true;
    }
}
