using Prismedia.Application.Files;
using Prismedia.Contracts.Files;
using Prismedia.Contracts.Media;
using Prismedia.Contracts.System;

namespace Prismedia.Infrastructure.Files;

/// <summary>
/// Local disk adapter for watched-root filesystem operations. It assumes paths have already
/// been root-normalized by the application service, and adds disk-specific protections such
/// as symlink checks before writes and deletes.
/// </summary>
public sealed class LocalManagedFileStorage : IManagedFileStorage {
    /// <inheritdoc />
    public Task<IReadOnlyList<FileEntry>> ListChildrenAsync(
        ResolvedFilePath directory,
        CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        var info = new DirectoryInfo(directory.AbsolutePath);
        if (!info.Exists) {
            throw new FileOperationException(ApiProblemCodes.NotFound, "Directory was not found.");
        }

        var entries = new List<FileEntry>();
        foreach (var child in SafeEnumerate(info)) {
            cancellationToken.ThrowIfCancellationRequested();
            if (child.Attributes.HasFlag(FileAttributes.Hidden)) {
                continue;
            }

            entries.Add(ToEntry(directory.Root.Id, directory.Root.Path, child));
        }

        return Task.FromResult<IReadOnlyList<FileEntry>>(entries
            .OrderByDescending(entry => entry.Kind == "directory")
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray());
    }

    /// <inheritdoc />
    public Task<FileDetail> GetDetailAsync(
        ResolvedFilePath path,
        IReadOnlyList<FileLinkedEntity> linkedEntities,
        CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        var info = GetExistingInfo(path.AbsolutePath);
        var entry = ToEntry(path.Root.Id, path.Root.Path, info);

        long? dirFileCount = null;
        long? dirTotalSize = null;
        if (info is DirectoryInfo dir) {
            try {
                var files = dir.EnumerateFiles("*", SearchOption.AllDirectories);
                long count = 0;
                long size = 0;
                foreach (var f in files) {
                    count++;
                    size += f.Length;
                }
                dirFileCount = count;
                dirTotalSize = size;
            } catch {
                // Permission or I/O errors — leave nulls.
            }
        }

        return Task.FromResult(new FileDetail(
            entry,
            path.AbsolutePath,
            info.CreationTimeUtc == DateTime.MinValue
                ? null
                : new DateTimeOffset(info.CreationTimeUtc, TimeSpan.Zero),
            linkedEntities,
            entry.Kind == "file" && IsPreviewable(entry.MimeType),
            dirFileCount,
            dirTotalSize));
    }

    /// <inheritdoc />
    public Task<FileContentInfo> GetContentInfoAsync(
        ResolvedFilePath path,
        CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        var file = new FileInfo(path.AbsolutePath);
        if (!file.Exists) {
            throw new FileOperationException(ApiProblemCodes.NotFound, "File was not found.");
        }

        return Task.FromResult(new FileContentInfo(
            file.FullName,
            DetectMimeType(file.Name),
            new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero),
            file.Length));
    }

    /// <inheritdoc />
    public Task CreateDirectoryAsync(ResolvedFilePath path, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureNoSymlinkAncestor(path);
        if (File.Exists(path.AbsolutePath) || Directory.Exists(path.AbsolutePath)) {
            throw new FileConflictException($"A file or folder already exists at {path.RelativePath}.");
        }

        Directory.CreateDirectory(path.AbsolutePath);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task WriteFileAsync(
        ResolvedFilePath path,
        Stream content,
        CancellationToken cancellationToken) {
        EnsureNoSymlinkAncestor(path);
        if (File.Exists(path.AbsolutePath) || Directory.Exists(path.AbsolutePath)) {
            throw new FileConflictException($"A file or folder already exists at {path.RelativePath}.");
        }

        var parent = Path.GetDirectoryName(path.AbsolutePath);
        if (!string.IsNullOrWhiteSpace(parent)) {
            Directory.CreateDirectory(parent);
        }

        await using var output = new FileStream(path.AbsolutePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await content.CopyToAsync(output, cancellationToken);
    }

    /// <inheritdoc />
    public Task MoveAsync(
        ResolvedFilePath source,
        ResolvedFilePath target,
        CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureNoSymlinkAncestor(source);
        EnsureNoSymlinkAncestor(target);
        if (File.Exists(target.AbsolutePath) || Directory.Exists(target.AbsolutePath)) {
            throw new FileConflictException($"A file or folder already exists at {target.RelativePath}.");
        }

        var parent = Path.GetDirectoryName(target.AbsolutePath);
        if (!string.IsNullOrWhiteSpace(parent)) {
            Directory.CreateDirectory(parent);
        }

        if (Directory.Exists(source.AbsolutePath)) {
            Directory.Move(source.AbsolutePath, target.AbsolutePath);
            return Task.CompletedTask;
        }

        if (File.Exists(source.AbsolutePath)) {
            File.Move(source.AbsolutePath, target.AbsolutePath);
            return Task.CompletedTask;
        }

        throw new FileOperationException(ApiProblemCodes.NotFound, "Source path was not found.");
    }

    /// <inheritdoc />
    public Task DeleteAsync(ResolvedFilePath path, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureNoSymlinkAncestor(path);
        if (Directory.Exists(path.AbsolutePath)) {
            Directory.Delete(path.AbsolutePath, recursive: true);
            return Task.CompletedTask;
        }

        if (File.Exists(path.AbsolutePath)) {
            File.Delete(path.AbsolutePath);
            return Task.CompletedTask;
        }

        throw new FileOperationException(ApiProblemCodes.NotFound, "Path was not found.");
    }

    private static IEnumerable<FileSystemInfo> SafeEnumerate(DirectoryInfo directory) {
        try {
            return directory.EnumerateFileSystemInfos();
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            return [];
        }
    }

    private static FileSystemInfo GetExistingInfo(string path) {
        if (Directory.Exists(path)) {
            return new DirectoryInfo(path);
        }

        if (File.Exists(path)) {
            return new FileInfo(path);
        }

        throw new FileOperationException(ApiProblemCodes.NotFound, "Path was not found.");
    }

    private static FileEntry ToEntry(Guid rootId, string rootPath, FileSystemInfo info) {
        var isDirectory = info.Attributes.HasFlag(FileAttributes.Directory);
        var path = Relative(rootPath, info.FullName);
        long? size = isDirectory ? null : ((FileInfo)info).Length;
        var mime = isDirectory ? null : DetectMimeType(info.Name);
        return new FileEntry(
            rootId,
            path,
            info.Name,
            isDirectory ? "directory" : "file",
            size,
            mime,
            new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero));
    }

    private static string Relative(string rootPath, string absolutePath) =>
        Path.GetRelativePath(rootPath, absolutePath).Replace('\\', '/');

    private static string DetectMimeType(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() switch {
            ".jpg" or ".jpeg" => MediaContentTypes.ImageJpeg,
            ".png" => MediaContentTypes.ImagePng,
            ".gif" => MediaContentTypes.ImageGif,
            ".webp" => MediaContentTypes.ImageWebp,
            ".avif" => MediaContentTypes.ImageAvif,
            ".mp4" or ".m4v" => MediaContentTypes.VideoMp4,
            ".webm" => MediaContentTypes.VideoWebm,
            ".mp3" => MediaContentTypes.AudioMpeg,
            ".m4a" => MediaContentTypes.AudioMp4,
            ".flac" => MediaContentTypes.AudioFlac,
            ".wav" => MediaContentTypes.AudioWav,
            ".txt" or ".log" or ".md" => "text/plain",
            ".json" => "application/json",
            ".xml" => "application/xml",
            ".vtt" => MediaContentTypes.Vtt,
            ".srt" => "text/plain",
            _ => MediaContentTypes.OctetStream,
        };

    private static bool IsPreviewable(string? mimeType) =>
        mimeType is not null &&
        (mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ||
         mimeType.StartsWith("video/", StringComparison.OrdinalIgnoreCase) ||
         mimeType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) ||
         mimeType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ||
         mimeType is "application/json" or "application/xml");

    private static void EnsureNoSymlinkAncestor(ResolvedFilePath path) {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Root.Path));
        var current = Path.GetFullPath(path.AbsolutePath);
        var probe = Directory.Exists(current) || File.Exists(current)
            ? current
            : Path.GetDirectoryName(current);

        while (!string.IsNullOrWhiteSpace(probe)) {
            var trimmed = Path.TrimEndingDirectorySeparator(probe);
            if (trimmed.Length < root.Length ||
                !trimmed.StartsWith(root, StringComparison.OrdinalIgnoreCase)) {
                break;
            }

            if ((Directory.Exists(trimmed) || File.Exists(trimmed)) &&
                File.GetAttributes(trimmed).HasFlag(FileAttributes.ReparsePoint)) {
                throw new FileOperationException(ApiProblemCodes.InvalidPath, "Filesystem operations cannot traverse symlinks.");
            }

            if (string.Equals(trimmed, root, StringComparison.OrdinalIgnoreCase)) {
                break;
            }

            probe = Path.GetDirectoryName(trimmed);
        }
    }
}
