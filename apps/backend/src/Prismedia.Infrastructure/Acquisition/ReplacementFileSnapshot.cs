using Prismedia.Application.Files;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>Detects changes to replacement inputs and destinations across a potentially long decoder wait.</summary>
internal sealed class ReplacementFileSnapshot {
    private readonly IReadOnlyDictionary<string, FileState> _files;

    private ReplacementFileSnapshot(IReadOnlyDictionary<string, FileState> files) => _files = files;

    /// <summary>Captures current file facts, including absent destinations; null means inspection could not finish.</summary>
    public static ReplacementFileSnapshot? Capture(params string?[] paths) {
        try {
            return new(paths.OfType<string>().Distinct(FileSystemPathComparison.Comparer)
                .ToDictionary(path => path, Read, FileSystemPathComparison.Comparer));
        } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
            return null;
        }
    }

    /// <summary>Requires every captured path to retain its existence, type, size, and modification time.</summary>
    public bool IsCurrent() {
        try {
            return _files.All(file => Read(file.Key) == file.Value);
        } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
            return false;
        }
    }

    private static FileState Read(string path) {
        var info = new FileInfo(path);
        return new(Directory.Exists(path), info.Exists ? info.Length : null,
            info.Exists ? info.LastWriteTimeUtc : null);
    }

    private sealed record FileState(bool Directory, long? Length, DateTime? Modified);
}
