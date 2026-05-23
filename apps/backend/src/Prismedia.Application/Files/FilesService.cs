using Prismedia.Application.Jobs;
using Prismedia.Contracts.Files;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Files;

/// <summary>
/// Application service for watched-root filesystem management. It validates root-relative
/// paths, coordinates disk operations through <see cref="IManagedFileStorage"/>, rewrites
/// known catalog source paths after moves, and queues scans for affected roots.
/// </summary>
public sealed class FilesService(
    IFilesPersistence persistence,
    IManagedFileStorage storage,
    IJobQueueService jobs) {
    /// <summary>Lists watched roots available to the Files page.</summary>
    public async Task<FileRootsResponse> ListRootsAsync(bool hideNsfw, CancellationToken cancellationToken) {
        var roots = await persistence.ListRootsAsync(cancellationToken);
        return new FileRootsResponse(roots
            .Where(root => !hideNsfw || !root.IsNsfw)
            .OrderBy(root => root.Label, StringComparer.OrdinalIgnoreCase)
            .Select(root => new FileRoot(root.Id, root.Label, root.Path, root.Enabled))
            .ToArray());
    }

    /// <summary>Lists direct children under a root-relative directory.</summary>
    public async Task<FileChildrenResponse> ListChildrenAsync(
        FileChildrenRequest request,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        var directory = await ResolveAsync(request.RootId, request.Path, hideNsfw, cancellationToken);
        var entries = await storage.ListChildrenAsync(directory, cancellationToken);
        if (hideNsfw) {
            entries = await FilterVisibleEntriesAsync(directory, entries, cancellationToken);
        }

        return new FileChildrenResponse(request.RootId, directory.RelativePath, entries);
    }

    /// <summary>Gets metadata for one watched-root path.</summary>
    public async Task<FileDetail> GetDetailAsync(
        FileDetailRequest request,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        var path = await ResolveAsync(request.RootId, request.Path, hideNsfw, cancellationToken);
        await EnsureVisiblePathAsync(path, hideNsfw, cancellationToken);
        var linked = await persistence.ListLinkedEntitiesAsync(path.AbsolutePath, hideNsfw, cancellationToken);
        return await storage.GetDetailAsync(path, linked, cancellationToken);
    }

    /// <summary>Gets content metadata for one previewable file.</summary>
    public async Task<FileContentInfo> GetContentInfoAsync(
        FileDetailRequest request,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        var path = await ResolveAsync(request.RootId, request.Path, hideNsfw, cancellationToken);
        await EnsureVisiblePathAsync(path, hideNsfw, cancellationToken);
        return await storage.GetContentInfoAsync(path, cancellationToken);
    }

    /// <summary>Creates a folder and queues scans for the affected root.</summary>
    public async Task<FileOperationResponse> CreateFolderAsync(
        FileCreateFolderRequest request,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        var name = CleanSegment(request.Name);
        var relativePath = CombineRelative(NormalizeRelativePath(request.ParentPath), name);
        var target = await ResolveAsync(request.RootId, relativePath, hideNsfw, cancellationToken);
        await EnsureVisiblePathAsync(target, hideNsfw, cancellationToken);
        await storage.CreateDirectoryAsync(target, cancellationToken);
        return new FileOperationResponse(await QueueScansAsync([target.Root], cancellationToken));
    }

    /// <summary>Uploads files to a watched-root directory while preserving nested relative paths.</summary>
    public async Task<FileOperationResponse> UploadAsync(
        FileUploadRequest request,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        if (request.Items.Count == 0) {
            return new FileOperationResponse(0);
        }

        var targetPath = NormalizeRelativePath(request.TargetPath);
        FileLibraryRoot? root = null;
        foreach (var item in request.Items) {
            var relativeItemPath = NormalizeRelativePath(item.RelativePath);
            if (string.IsNullOrWhiteSpace(relativeItemPath)) {
                throw new FileOperationException("invalid_path", "Uploaded files must include a relative path.");
            }

            var destination = await ResolveAsync(
                request.RootId,
                CombineRelative(targetPath, relativeItemPath),
                hideNsfw,
                cancellationToken);
            await EnsureVisiblePathAsync(destination, hideNsfw, cancellationToken);
            root = destination.Root;
            await storage.WriteFileAsync(destination, item.Content, cancellationToken);
        }

        return new FileOperationResponse(root is null ? 0 : await QueueScansAsync([root], cancellationToken));
    }

    /// <summary>Renames a file or folder within its current parent directory.</summary>
    public async Task<FileOperationResponse> RenameAsync(
        FileRenameRequest request,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        var source = await ResolveAsync(request.RootId, request.Path, hideNsfw, cancellationToken);
        await EnsureVisiblePathAsync(source, hideNsfw, cancellationToken);
        var parent = Path.GetDirectoryName(source.RelativePath)?.Replace('\\', '/') ?? string.Empty;
        var target = await ResolveAsync(
            request.RootId,
            CombineRelative(parent, CleanSegment(request.Name)),
            hideNsfw,
            cancellationToken);
        await EnsureVisiblePathAsync(target, hideNsfw, cancellationToken);
        await MoveResolvedAsync(source, target, cancellationToken);
        return new FileOperationResponse(await QueueScansAsync([source.Root], cancellationToken));
    }

    /// <summary>Moves a file or folder between watched-root locations.</summary>
    public async Task<FileOperationResponse> MoveAsync(
        FileMoveRequest request,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        var source = await ResolveAsync(request.SourceRootId, request.SourcePath, hideNsfw, cancellationToken);
        await EnsureVisiblePathAsync(source, hideNsfw, cancellationToken);
        var target = await ResolveAsync(request.TargetRootId, request.TargetPath, hideNsfw, cancellationToken);
        await EnsureVisiblePathAsync(target, hideNsfw, cancellationToken);
        await MoveResolvedAsync(source, target, cancellationToken);
        return new FileOperationResponse(await QueueScansAsync([source.Root, target.Root], cancellationToken));
    }

    /// <summary>Permanently deletes one watched-root file or directory and queues scans.</summary>
    public async Task<FileOperationResponse> DeleteAsync(
        FileDeleteRequest request,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(request.Path)) {
            throw new FileOperationException("invalid_path", "Deleting a library root from Files is not supported.");
        }

        var target = await ResolveAsync(request.RootId, request.Path, hideNsfw, cancellationToken);
        await EnsureVisiblePathAsync(target, hideNsfw, cancellationToken);
        await storage.DeleteAsync(target, cancellationToken);
        return new FileOperationResponse(await QueueScansAsync([target.Root], cancellationToken));
    }

    /// <summary>Queues scans for one watched root.</summary>
    public async Task<FileOperationResponse> RescanAsync(
        FileRescanRequest request,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        var root = await GetRootAsync(request.RootId, hideNsfw, cancellationToken);
        return new FileOperationResponse(await QueueScansAsync([root], cancellationToken));
    }

    private async Task MoveResolvedAsync(
        ResolvedFilePath source,
        ResolvedFilePath target,
        CancellationToken cancellationToken) {
        await storage.MoveAsync(source, target, cancellationToken);
        await persistence.ApplyPathPrefixRewriteAsync(
            source.AbsolutePath,
            target.AbsolutePath,
            cancellationToken);
    }

    private async Task<ResolvedFilePath> ResolveAsync(
        Guid rootId,
        string? relativePath,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        var root = await GetRootAsync(rootId, hideNsfw, cancellationToken);
        var normalizedRoot = Path.GetFullPath(root.Path);
        var normalizedRelative = NormalizeRelativePath(relativePath);
        if (Path.IsPathRooted(normalizedRelative)) {
            throw new FileOperationException("invalid_path", "Files paths must be relative to a library root.");
        }

        var absolute = string.IsNullOrWhiteSpace(normalizedRelative)
            ? normalizedRoot
            : Path.GetFullPath(Path.Combine(normalizedRoot, normalizedRelative));
        var trimmedRoot = Path.TrimEndingDirectorySeparator(normalizedRoot);
        var trimmedAbsolute = Path.TrimEndingDirectorySeparator(absolute);
        var inside = string.Equals(trimmedRoot, trimmedAbsolute, StringComparison.OrdinalIgnoreCase) ||
            trimmedAbsolute.StartsWith(trimmedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        if (!inside) {
            throw new FileOperationException("invalid_path", "Files paths cannot escape the selected library root.");
        }

        return new ResolvedFilePath(root, ToRelativePath(normalizedRelative), absolute);
    }

    private async Task<FileLibraryRoot> GetRootAsync(Guid rootId, bool hideNsfw, CancellationToken cancellationToken) {
        var root = await persistence.GetRootAsync(rootId, cancellationToken);
        if (root is null || (hideNsfw && root.IsNsfw)) {
            throw new FileOperationException("root_not_found", "Library root was not found.");
        }

        return root;
    }

    private async Task<IReadOnlyList<FileEntry>> FilterVisibleEntriesAsync(
        ResolvedFilePath directory,
        IReadOnlyList<FileEntry> entries,
        CancellationToken cancellationToken) {
        if (entries.Count == 0) {
            return entries;
        }

        var absolutePaths = entries
            .Select(entry => AbsolutePathForEntry(directory.Root.Path, entry.Path))
            .ToArray();
        var hidden = await persistence.ListHiddenPathsAsync(absolutePaths, cancellationToken);
        if (hidden.Count == 0) {
            return entries;
        }

        return entries
            .Where(entry => !hidden.Contains(AbsolutePathForEntry(directory.Root.Path, entry.Path)))
            .ToArray();
    }

    private async Task EnsureVisiblePathAsync(
        ResolvedFilePath path,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        if (!hideNsfw) {
            return;
        }

        var hidden = await persistence.ListHiddenPathsAsync([path.AbsolutePath], cancellationToken);
        if (hidden.Contains(path.AbsolutePath)) {
            throw new FileOperationException("not_found", "File or folder was not found.");
        }
    }

    private static string AbsolutePathForEntry(string rootPath, string entryPath) =>
        Path.GetFullPath(Path.Combine(rootPath, entryPath));

    private async Task<int> QueueScansAsync(
        IEnumerable<FileLibraryRoot> roots,
        CancellationToken cancellationToken) {
        var uniqueRoots = roots
            .GroupBy(root => root.Id)
            .Select(group => group.First())
            .Where(root => root.Enabled)
            .ToArray();
        var queued = 0;
        foreach (var root in uniqueRoots) {
            foreach (var type in ScanTypes(root)) {
                var targetId = root.Id.ToString();
                if (await jobs.HasPendingAsync(type, targetId, cancellationToken)) {
                    continue;
                }

                await jobs.EnqueueAsync(new EnqueueJobRequest(
                    Type: type,
                    PayloadJson: new ScanRootPayload(root.Id).ToJson(),
                    TargetEntityKind: "library-root",
                    TargetEntityId: targetId,
                    TargetLabel: root.Label), cancellationToken);
                queued++;
            }
        }

        return queued;
    }

    private static IEnumerable<JobType> ScanTypes(FileLibraryRoot root) {
        if (root.ScanVideos) yield return JobType.ScanLibrary;
        if (root.ScanImages) yield return JobType.ScanGallery;
        if (root.ScanAudio) yield return JobType.ScanAudio;
        if (root.ScanBooks) yield return JobType.ScanBook;
    }

    private static string CleanSegment(string name) {
        var trimmed = name.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) ||
            trimmed is "." or ".." ||
            trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            trimmed.Contains('/') ||
            trimmed.Contains('\\')) {
            throw new FileOperationException("invalid_path", "File or folder names must be valid path segments.");
        }

        return trimmed;
    }

    private static string CombineRelative(string parent, string child) =>
        ToRelativePath(string.IsNullOrWhiteSpace(parent)
            ? child
            : Path.Combine(parent, child));

    private static string NormalizeRelativePath(string? path) =>
        ToRelativePath(path?.Trim() ?? string.Empty);

    private static string ToRelativePath(string path) =>
        path.Replace('\\', '/').Trim('/');
}
