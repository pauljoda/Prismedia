namespace Prismedia.Application.Files;

/// <summary>
/// Canonical parser for persisted Entity source paths. Archive members use one synthetic path form while
/// their physical owner remains the archive on disk; every scanner, reader, and destructive workflow must
/// agree on this boundary.
/// </summary>
public static class EntitySourcePath {
    /// <summary>Stable separator between a physical archive path and its member name.</summary>
    public const string ArchiveMemberSeparator = "::";

    /// <summary>Builds the persisted source path for one archive member.</summary>
    public static string ArchiveMember(string archivePath, string memberPath) =>
        $"{archivePath}{ArchiveMemberSeparator}{memberPath}";

    /// <summary>Returns the physical file/folder that owns a persisted source path.</summary>
    public static string PhysicalOwner(string path) =>
        TrySplitArchiveMember(path, out var archivePath, out _) ? archivePath : path;

    /// <summary>
    /// Maps a persisted source path through a physical file or folder move. Archive members retain
    /// their synthetic member suffix while their physical archive owner follows the move.
    /// </summary>
    public static bool TryMapPhysicalPrefix(
        string persistedPath,
        string sourcePath,
        string targetPath,
        out string mappedPath) {
        var archiveMember = TrySplitArchiveMember(persistedPath, out var archivePath, out var memberPath);
        var physicalOwner = archiveMember ? archivePath : persistedPath;
        var current = Path.TrimEndingDirectorySeparator(Path.GetFullPath(physicalOwner));
        var source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourcePath));
        var target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(targetPath));

        string mappedOwner;
        if (FileSystemPathComparison.Equals(current, source)) {
            mappedOwner = target;
        } else if (FileSystemPathComparison.IsSameOrDescendant(source, current)) {
            mappedOwner = target + current[source.Length..];
        } else {
            mappedPath = persistedPath;
            return false;
        }

        mappedPath = archiveMember
            ? ArchiveMember(mappedOwner, memberPath)
            : mappedOwner;
        return true;
    }

    /// <summary>Splits an archive-member source path into its physical archive and member name.</summary>
    public static bool TrySplitArchiveMember(
        string path,
        out string archivePath,
        out string memberPath) {
        var separator = path.IndexOf(ArchiveMemberSeparator, StringComparison.Ordinal);
        if (separator <= 0 || separator + ArchiveMemberSeparator.Length >= path.Length) {
            archivePath = string.Empty;
            memberPath = string.Empty;
            return false;
        }

        archivePath = path[..separator];
        memberPath = path[(separator + ArchiveMemberSeparator.Length)..];
        return !string.IsNullOrWhiteSpace(archivePath) && !string.IsNullOrWhiteSpace(memberPath);
    }
}
