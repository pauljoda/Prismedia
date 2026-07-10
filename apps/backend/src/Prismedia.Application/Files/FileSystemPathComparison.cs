namespace Prismedia.Application.Files;

/// <summary>
/// Canonical comparison semantics for physical filesystem paths. Windows paths are case-insensitive;
/// Unix paths are case-sensitive, so case-distinct media can never collapse into one import unit.
/// </summary>
public static class FileSystemPathComparison {
    /// <summary>Comparer for path-keyed sets and dictionaries.</summary>
    public static StringComparer Comparer { get; } =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>Comparison for exact equality and rooted-prefix checks.</summary>
    public static StringComparison Comparison { get; } =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>Whether two normalized paths identify the same platform path.</summary>
    public static bool Equals(string first, string second) =>
        string.Equals(first, second, Comparison);

    /// <summary>
    /// Whether <paramref name="path"/> is the same physical path as <paramref name="parent"/> or is
    /// contained below it, using the host platform's case semantics and a directory-segment boundary.
    /// </summary>
    public static bool IsSameOrDescendant(string parent, string path) {
        var normalizedParent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));
        var normalizedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        return Equals(normalizedPath, normalizedParent)
            || normalizedPath.StartsWith(
                normalizedParent + Path.DirectorySeparatorChar,
                Comparison);
    }
}
