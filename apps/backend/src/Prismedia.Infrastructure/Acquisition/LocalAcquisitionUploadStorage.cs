using System.IO.Compression;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Files;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>Filesystem location for browser-uploaded acquisition payloads.</summary>
public sealed record AcquisitionUploadStorageOptions(string RootPath);

/// <summary>
/// Stages browser uploads under Prismedia's data directory. A ZIP that wraps recognized media is safely
/// expanded before import; an image-only ZIP remains intact because it is itself a supported comic payload.
/// </summary>
public sealed class LocalAcquisitionUploadStorage(AcquisitionUploadStorageOptions options) : IAcquisitionUploadStorage {
    private const string ClientItemPrefix = "upload:";
    private const int MaxArchiveEntries = 10_000;
    private const long MaxExpandedBytes = 250L * 1024 * 1024 * 1024;
    private static readonly HashSet<string> WrappedMediaExtensions = new(StringComparer.OrdinalIgnoreCase) {
        ".mkv", ".mp4", ".m4v", ".mov", ".avi", ".webm",
        ".epub", ".pdf", ".cbz", ".m4b", ".m4a", ".mp3", ".flac", ".ogg", ".opus"
    };

    public async Task<CompletedAcquisitionUpload> StageAsync(
        Guid acquisitionId,
        IReadOnlyList<AcquisitionUploadItem> items,
        CancellationToken cancellationToken) {
        if (items.Count == 0) {
            throw new InvalidDataException("At least one file is required for an acquisition upload.");
        }

        var token = Guid.NewGuid();
        var itemRoot = Path.GetFullPath(Path.Combine(options.RootPath, token.ToString("N")));
        var payloadRoot = Path.Combine(itemRoot, "payload");
        Directory.CreateDirectory(payloadRoot);
        try {
            foreach (var item in items) {
                var target = ResolveRelativePath(payloadRoot, item.RelativePath);
                if (File.Exists(target)) {
                    throw new InvalidDataException("More than one uploaded file has the same relative path.");
                }
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await using var output = new FileStream(
                    target,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 1024 * 1024,
                    useAsync: true);
                await item.Content.CopyToAsync(output, cancellationToken);
            }

            await ExpandWrappedZipAsync(payloadRoot, cancellationToken);
            return new CompletedAcquisitionUpload(
                ClientItemPrefix + token.ToString("N"),
                payloadRoot,
                items.Count == 1 ? Path.GetFileName(items[0].RelativePath) : $"{items.Count} uploaded files");
        } catch {
            TryDelete(itemRoot);
            throw;
        }
    }

    public bool Owns(string? clientItemId) =>
        clientItemId?.StartsWith(ClientItemPrefix, StringComparison.Ordinal) == true;

    public Task DeleteAsync(string clientItemId, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryParseToken(clientItemId, out var token)) {
            return Task.CompletedTask;
        }

        var root = Path.GetFullPath(options.RootPath);
        var itemRoot = Path.GetFullPath(Path.Combine(root, token.ToString("N")));
        if (!IsInside(root, itemRoot)) {
            throw new InvalidDataException("The upload staging path escaped its configured boundary.");
        }
        TryDelete(itemRoot);
        return Task.CompletedTask;
    }

    private static async Task ExpandWrappedZipAsync(string payloadRoot, CancellationToken cancellationToken) {
        var files = Directory.EnumerateFiles(payloadRoot, "*", SearchOption.AllDirectories).ToArray();
        if (files.Length != 1 || !string.Equals(Path.GetExtension(files[0]), ".zip", StringComparison.OrdinalIgnoreCase)) {
            return;
        }

        using var archive = ZipFile.OpenRead(files[0]);
        if (archive.Entries.Count > MaxArchiveEntries) {
            throw new InvalidDataException($"The uploaded ZIP contains more than {MaxArchiveEntries:N0} entries.");
        }
        if (!archive.Entries.Any(entry => WrappedMediaExtensions.Contains(Path.GetExtension(entry.FullName)))) {
            return;
        }

        var expandedRoot = Path.Combine(payloadRoot, ".expanded");
        Directory.CreateDirectory(expandedRoot);
        long expandedBytes = 0;
        foreach (var entry in archive.Entries) {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(entry.Name)) {
                continue;
            }
            if (entry.Length > MaxExpandedBytes - expandedBytes) {
                throw new InvalidDataException("The uploaded ZIP expands beyond the acquisition upload limit.");
            }
            expandedBytes += entry.Length;

            var target = ResolveRelativePath(expandedRoot, entry.FullName);
            if (File.Exists(target)) {
                throw new InvalidDataException("The uploaded ZIP contains duplicate file paths.");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var source = entry.Open();
            await using var output = new FileStream(
                target,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1024 * 1024,
                useAsync: true);
            await source.CopyToAsync(output, cancellationToken);
        }

        File.Delete(files[0]);
        foreach (var path in Directory.EnumerateFileSystemEntries(expandedRoot)) {
            var target = Path.Combine(payloadRoot, Path.GetFileName(path));
            if (File.Exists(path)) File.Move(path, target);
            else Directory.Move(path, target);
        }
        Directory.Delete(expandedRoot);
    }

    private static string ResolveRelativePath(string root, string relativePath) {
        var normalized = relativePath.Replace('\\', '/').TrimStart('/');
        if (string.IsNullOrWhiteSpace(normalized) || Path.IsPathFullyQualified(normalized)) {
            throw new InvalidDataException("An uploaded file has an invalid relative path.");
        }
        var target = Path.GetFullPath(Path.Combine(root, normalized));
        if (!IsInside(Path.GetFullPath(root), target)) {
            throw new InvalidDataException("An uploaded file escaped the acquisition upload boundary.");
        }
        return target;
    }

    private static bool IsInside(string root, string path) =>
        path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, FileSystemPathComparison.Comparison);

    private static bool TryParseToken(string clientItemId, out Guid token) {
        token = default;
        return clientItemId.StartsWith(ClientItemPrefix, StringComparison.Ordinal)
            && Guid.TryParseExact(clientItemId[ClientItemPrefix.Length..], "N", out token);
    }

    private static void TryDelete(string path) {
        try {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        } catch {
            // A later maintenance pass can remove an abandoned staging directory.
        }
    }
}
