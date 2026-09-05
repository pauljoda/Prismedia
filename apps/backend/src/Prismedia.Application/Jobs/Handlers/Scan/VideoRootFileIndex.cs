using Prismedia.Application.Files;

namespace Prismedia.Application.Jobs.Handlers.Scan;

/// <summary>
/// One-pass index over a reconcile's classification file list: direct files per directory
/// (sorted by filename for deterministic sibling positions) and which directories have files
/// somewhere below their direct children. Classification rules read it in O(directory)
/// instead of scanning the whole root's file list once per file.
/// </summary>
internal sealed class VideoRootFileIndex {
    private readonly Dictionary<string, List<string>> _filesByDirectory;
    private readonly HashSet<string> _directoriesWithDeepFiles;

    private VideoRootFileIndex(
        Dictionary<string, List<string>> filesByDirectory,
        HashSet<string> directoriesWithDeepFiles) {
        _filesByDirectory = filesByDirectory;
        _directoriesWithDeepFiles = directoriesWithDeepFiles;
    }

    public static VideoRootFileIndex Build(IReadOnlyList<string> allFiles) {
        var byDirectory = new Dictionary<string, List<string>>(FileSystemPathComparison.Comparer);
        var deep = new HashSet<string>(FileSystemPathComparison.Comparer);
        foreach (var file in allFiles) {
            var parent = Path.GetDirectoryName(file);
            if (string.IsNullOrWhiteSpace(parent)) {
                continue;
            }

            parent = Path.TrimEndingDirectorySeparator(parent);
            if (!byDirectory.TryGetValue(parent, out var list)) {
                list = [];
                byDirectory[parent] = list;
            }

            list.Add(file);

            // Every ancestor above the parent has this file strictly below its direct
            // children. Stop climbing at the first already-marked ancestor: its own
            // ancestors were marked by the walk that marked it.
            var ancestor = Path.GetDirectoryName(parent);
            while (!string.IsNullOrWhiteSpace(ancestor)) {
                if (!deep.Add(Path.TrimEndingDirectorySeparator(ancestor))) {
                    break;
                }

                ancestor = Path.GetDirectoryName(ancestor);
            }
        }

        foreach (var list in byDirectory.Values) {
            list.Sort(static (left, right) => string.Compare(
                Path.GetFileName(left), Path.GetFileName(right), StringComparison.OrdinalIgnoreCase));
        }

        return new VideoRootFileIndex(byDirectory, deep);
    }

    /// <summary>Direct files of a directory, sorted by filename; empty when none.</summary>
    public IReadOnlyList<string> DirectFiles(string directory) =>
        _filesByDirectory.GetValueOrDefault(Path.TrimEndingDirectorySeparator(directory)) ?? [];

    /// <summary>Whether any indexed file lives below the directory's direct children.</summary>
    public bool HasDeepFiles(string directory) =>
        _directoriesWithDeepFiles.Contains(Path.TrimEndingDirectorySeparator(directory));
}

