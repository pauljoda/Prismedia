using Prismedia.Contracts.Files;
using Prismedia.Contracts.System;

namespace Prismedia.Application.Files;

/// <summary>
/// Application-layer watched root with scan policy flags needed after filesystem mutations.
/// </summary>
public sealed record FileLibraryRoot(
    Guid Id,
    string Path,
    string Label,
    bool Enabled,
    bool ScanVideos,
    bool ScanImages,
    bool ScanAudio,
    bool ScanBooks,
    bool IsNsfw);

/// <summary>
/// Absolute path resolved under a watched root.
/// </summary>
public sealed record ResolvedFilePath(FileLibraryRoot Root, string RelativePath, string AbsolutePath);

/// <summary>
/// Content metadata used by streaming endpoints.
/// </summary>
public sealed record FileContentInfo(
    string AbsolutePath,
    string MimeType,
    DateTimeOffset? LastModified,
    long SizeBytes);

/// <summary>
/// Persistence port for root metadata and catalog path rewrites used by the Files page.
/// </summary>
public interface IFilesPersistence {
    /// <summary>Lists watched roots that may be displayed by the Files page.</summary>
    Task<IReadOnlyList<FileLibraryRoot>> ListRootsAsync(CancellationToken cancellationToken);

    /// <summary>Gets one watched root by id.</summary>
    Task<FileLibraryRoot?> GetRootAsync(Guid rootId, CancellationToken cancellationToken);

    /// <summary>Lists entities linked to an absolute filesystem path.</summary>
    Task<IReadOnlyList<FileLinkedEntity>> ListLinkedEntitiesAsync(
        string absolutePath,
        bool hideNsfw,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns candidate paths that are hidden because an NSFW source entity owns or contains them.
    /// </summary>
    /// <param name="scopeDirectory">
    /// Absolute path bounding the lookup: only source files at or under this directory are
    /// considered. Every candidate must live within it, which lets the query load just the sources
    /// in the viewed subtree instead of the whole library.
    /// </param>
    /// <param name="absolutePaths">Candidate absolute paths to test for visibility.</param>
    Task<IReadOnlySet<string>> ListHiddenPathsAsync(
        string scopeDirectory,
        IReadOnlyList<string> absolutePaths,
        CancellationToken cancellationToken);

    /// <summary>Returns the requested root-relative paths covered by scan exclusions.</summary>
    Task<IReadOnlySet<string>> ListExcludedRelativePathsAsync(
        Guid rootId,
        IReadOnlyList<string> relativePaths,
        CancellationToken cancellationToken);

    /// <summary>Adds or updates a scan exclusion for one root-relative path.</summary>
    Task UpsertExclusionAsync(
        Guid rootId,
        string relativePath,
        string kind,
        CancellationToken cancellationToken);

    /// <summary>Removes a scan exclusion for one root-relative path.</summary>
    Task RemoveExclusionAsync(
        Guid rootId,
        string relativePath,
        CancellationToken cancellationToken);

    /// <summary>Rewrites catalog source paths that match a moved filesystem path prefix.</summary>
    Task ApplyPathPrefixRewriteAsync(
        string sourcePath,
        string targetPath,
        CancellationToken cancellationToken);
}

/// <summary>
/// Infrastructure port for real filesystem reads and writes.
/// </summary>
public interface IManagedFileStorage {
    /// <summary>Lists direct visible children under a directory.</summary>
    Task<IReadOnlyList<FileEntry>> ListChildrenAsync(
        ResolvedFilePath directory,
        CancellationToken cancellationToken);

    /// <summary>Gets metadata for one resolved path.</summary>
    Task<FileDetail> GetDetailAsync(
        ResolvedFilePath path,
        IReadOnlyList<FileLinkedEntity> linkedEntities,
        CancellationToken cancellationToken);

    /// <summary>Gets content metadata for one file path.</summary>
    Task<FileContentInfo> GetContentInfoAsync(
        ResolvedFilePath path,
        CancellationToken cancellationToken);

    /// <summary>Creates one directory.</summary>
    Task CreateDirectoryAsync(ResolvedFilePath path, CancellationToken cancellationToken);

    /// <summary>Writes a stream to a file path.</summary>
    Task WriteFileAsync(ResolvedFilePath path, Stream content, CancellationToken cancellationToken);

    /// <summary>Moves one file or directory.</summary>
    Task MoveAsync(
        ResolvedFilePath source,
        ResolvedFilePath target,
        CancellationToken cancellationToken);

    /// <summary>Permanently deletes one file or directory.</summary>
    Task DeleteAsync(ResolvedFilePath path, CancellationToken cancellationToken);
}

/// <summary>
/// Expected filesystem operation failure surfaced to API endpoints as a stable problem.
/// </summary>
public class FileOperationException : Exception {
    public FileOperationException(string code, string message) : base(message) {
        Code = code;
    }

    /// <summary>Stable problem code.</summary>
    public string Code { get; }
}

/// <summary>
/// Filesystem operation failure caused by an existing target path.
/// </summary>
public sealed class FileConflictException : FileOperationException {
    public FileConflictException(string message) : base(ApiProblemCodes.FileConflict, message) {
    }
}
