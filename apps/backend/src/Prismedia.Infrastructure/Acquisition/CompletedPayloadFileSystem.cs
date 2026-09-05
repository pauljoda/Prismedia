using System.Security.Cryptography;
using System.Text;
using Prismedia.Application.Files;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>Deletes an owned completed payload while preserving library paths and avoiding traversal through payload links.</summary>
public static class CompletedPayloadFileSystem {
    /// <summary>
    /// Removes one exact payload after checking every required library source. A deterministic sibling
    /// staging path lets a retry finish an interrupted removal without touching a newly created payload.
    /// </summary>
    public static void Delete(string payloadPath, string ownershipKey, IReadOnlyList<string> protectedPaths,
        IReadOnlyList<string> requiredSources, CancellationToken cancellationToken) {
        var original = CanonicalPath(payloadPath, rejectLeafLink: true);
        if (FileSystemPathComparison.Equals(original, Path.GetPathRoot(original)!)) {
            throw new IOException("A filesystem root cannot be a completed download payload.");
        }
        var staged = StagingPath(original, ownershipKey);
        var protectedCanonical = protectedPaths.Concat(requiredSources).Select(path => CanonicalPath(path)).ToArray();
        if (protectedCanonical.Any(path => Overlaps(original, path) || Overlaps(staged, path))) {
            throw new IOException("The completed payload overlaps a protected library path; its files were preserved.");
        }
        var originalExists = Exists(original);
        var stagedExists = Exists(staged);
        if (!originalExists && !stagedExists) return;
        if (originalExists && stagedExists) {
            throw new IOException("A new payload exists beside an interrupted cleanup; both were preserved.");
        }
        foreach (var source in requiredSources) {
            cancellationToken.ThrowIfCancellationRequested();
            var file = new FileInfo(CanonicalPath(source));
            if (!file.Exists || file.Length <= 0) {
                throw new IOException("An imported library source is missing or empty; the retained download was preserved.");
            }
        }
        var working = stagedExists ? staged : original;
        var before = ReadTree(working, cancellationToken);
        if (!stagedExists) {
            if (Directory.Exists(original)) Directory.Move(original, staged);
            else File.Move(original, staged);
        }
        var after = ReadTree(staged, cancellationToken);
        if (!before.Files.SequenceEqual(after.Files) || !before.Directories.SequenceEqual(after.Directories)) {
            throw new IOException("The completed payload changed during cleanup; its remaining files were preserved.");
        }
        // Explicit deletes avoid recursive traversal if a directory link appears after enumeration.
        foreach (var file in after.Files) {
            cancellationToken.ThrowIfCancellationRequested();
            var path = file.RelativePath.Length == 0 ? staged : Path.Combine(staged, file.RelativePath);
            EnsureUnlinkedAncestors(staged, path);
            var current = new FileInfo(path);
            if (!current.Exists || current.Length != file.Length || current.LastWriteTimeUtc != file.LastWriteUtc) {
                throw new IOException("A completed file changed during cleanup; its remaining payload was preserved.");
            }
            File.Delete(path);
        }
        foreach (var directory in after.Directories.OrderByDescending(path => path.Length)) {
            var path = directory.Length == 0 ? staged : Path.Combine(staged, directory);
            EnsureUnlinkedAncestors(staged, path);
            Directory.Delete(path, recursive: false);
        }
    }

    /// <summary>Resolves existing ancestor links so equivalent library and download paths compare consistently.</summary>
    public static string CanonicalPath(string path, bool rejectLeafLink = false) {
        if (!Path.IsPathFullyQualified(path)) throw new IOException("Completed cleanup requires an absolute local path.");
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (rejectLeafLink) EnsureNotLink(full);
        var current = Path.GetPathRoot(full)!;
        foreach (var part in full[current.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)) {
            current = Path.Combine(current, part);
            var info = Info(current);
            if (info.LinkTarget is not null) {
                current = info.ResolveLinkTarget(returnFinalTarget: true)?.FullName
                    ?? throw new IOException("A cleanup path contains an unresolved filesystem link.");
            }
        }
        return Path.TrimEndingDirectorySeparator(current);
    }

    /// <summary>Whether either normalized path contains the other at a directory boundary.</summary>
    public static bool Overlaps(string left, string right) =>
        FileSystemPathComparison.IsSameOrDescendant(left, right) || FileSystemPathComparison.IsSameOrDescendant(right, left);

    /// <summary>Whether the exact payload or its interrupted cleanup still exists; links remain a protected error.</summary>
    public static bool HasPayload(string path, string ownershipKey) => Exists(path) || Exists(StagingPath(path, ownershipKey));

    private static string StagingPath(string path, string ownershipKey) => path + ".prismedia-cleanup-"
        + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ownershipKey)))[..16];

    private static (FileSnapshot[] Files, string[] Directories) ReadTree(string root, CancellationToken token) {
        var files = new List<FileSnapshot>();
        var directories = new List<string>();
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var path)) {
            token.ThrowIfCancellationRequested();
            EnsureNotLink(path);
            var relative = path == root ? string.Empty : Path.GetRelativePath(root, path);
            if (Directory.Exists(path)) {
                directories.Add(relative);
                foreach (var child in Directory.EnumerateFileSystemEntries(path)) pending.Push(child);
            } else {
                var file = new FileInfo(path);
                files.Add(new(relative, file.Length, file.LastWriteTimeUtc));
            }
        }
        return (files.OrderBy(file => file.RelativePath, FileSystemPathComparison.Comparer).ToArray(),
            directories.OrderBy(path => path, FileSystemPathComparison.Comparer).ToArray());
    }

    private static FileSystemInfo Info(string path) => Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
    private static bool Exists(string path) { EnsureNotLink(path); return File.Exists(path) || Directory.Exists(path); }
    private static void EnsureNotLink(string path) {
        if (Info(path).LinkTarget is not null) throw new IOException("A completed payload contains a filesystem link; its files were preserved.");
    }
    private static void EnsureUnlinkedAncestors(string root, string path) {
        for (var current = path; current is not null && FileSystemPathComparison.IsSameOrDescendant(root, current);
            current = Path.GetDirectoryName(current)) EnsureNotLink(current);
    }
    private sealed record FileSnapshot(string RelativePath, long Length, DateTime LastWriteUtc);
}
