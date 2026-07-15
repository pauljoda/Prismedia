using Prismedia.Domain.Entities;

namespace Prismedia.Contracts.Files;

/// <summary>
/// Watched library root exposed to the Files page.
/// </summary>
/// <param name="Id">Library root identifier.</param>
/// <param name="Label">User-facing root label.</param>
/// <param name="Path">Absolute root path on the server.</param>
/// <param name="Enabled">Whether the root participates in scans.</param>
public sealed record FileRoot(Guid Id, string Label, string Path, bool Enabled);

/// <summary>
/// Child file or directory entry under a watched root.
/// </summary>
/// <param name="RootId">Owning library root identifier.</param>
/// <param name="Path">Root-relative path. Empty string represents the root.</param>
/// <param name="Name">Display basename.</param>
/// <param name="Kind">Whether the entry is a directory or a leaf file.</param>
/// <param name="SizeBytes">File size when known.</param>
/// <param name="MimeType">Detected content type for files.</param>
/// <param name="ModifiedAt">Last modification timestamp when known.</param>
/// <param name="Excluded">Whether this path is excluded from library scans.</param>
public sealed record FileEntry(
    Guid RootId,
    string Path,
    string Name,
    FileEntryKind Kind,
    long? SizeBytes,
    string? MimeType,
    DateTimeOffset? ModifiedAt,
    bool Excluded = false);

/// <summary>
/// Files page root list response.
/// </summary>
/// <param name="Roots">Configured watched roots.</param>
public sealed record FileRootsResponse(IReadOnlyList<FileRoot> Roots);

/// <summary>
/// Request for direct children under a watched root path.
/// </summary>
/// <param name="RootId">Library root identifier.</param>
/// <param name="Path">Root-relative directory path. Empty string represents the root.</param>
public sealed record FileChildrenRequest(Guid RootId, string? Path);

/// <summary>
/// Direct children under a watched root directory.
/// </summary>
/// <param name="RootId">Library root identifier.</param>
/// <param name="Path">Root-relative directory path.</param>
/// <param name="Entries">Visible direct children.</param>
public sealed record FileChildrenResponse(Guid RootId, string Path, IReadOnlyList<FileEntry> Entries);

/// <summary>
/// Request for metadata about one watched-root path.
/// </summary>
/// <param name="RootId">Library root identifier.</param>
/// <param name="Path">Root-relative file or directory path. Empty string represents the root.</param>
public sealed record FileDetailRequest(Guid RootId, string? Path);

/// <summary>
/// Entity currently linked to a filesystem path.
/// </summary>
/// <param name="EntityId">Entity identifier.</param>
/// <param name="Kind">Entity kind.</param>
/// <param name="Title">Entity title.</param>
/// <param name="CoverUrl">Thumbnail or poster artwork path for display; null when the entity has no artwork.</param>
public sealed record FileLinkedEntity(Guid EntityId, EntityKind Kind, string Title, string? CoverUrl = null);

/// <summary>
/// Detailed metadata for a watched-root file or directory.
/// </summary>
/// <param name="Entry">File entry metadata.</param>
/// <param name="AbsolutePath">Server absolute path for trusted local display.</param>
/// <param name="CreatedAt">Creation timestamp when known.</param>
/// <param name="LinkedEntities">Known Prismedia entities whose source paths match this path.</param>
/// <param name="CanPreview">Whether the content endpoint can be used for an inline preview.</param>
/// <param name="DirectoryFileCount">Total file count when the entry is a directory; null for files.</param>
/// <param name="DirectoryTotalSizeBytes">Recursive size in bytes when the entry is a directory; null for files.</param>
public sealed record FileDetail(
    FileEntry Entry,
    string AbsolutePath,
    DateTimeOffset? CreatedAt,
    IReadOnlyList<FileLinkedEntity> LinkedEntities,
    bool CanPreview,
    long? DirectoryFileCount = null,
    long? DirectoryTotalSizeBytes = null);

/// <summary>
/// Request to create one folder under a watched root.
/// </summary>
/// <param name="RootId">Library root identifier.</param>
/// <param name="ParentPath">Root-relative parent directory path.</param>
/// <param name="Name">New folder basename.</param>
public sealed record FileCreateFolderRequest(Guid RootId, string? ParentPath, string Name);

/// <summary>
/// One uploaded file and its relative path within the dropped batch.
/// </summary>
/// <param name="RelativePath">Relative path to recreate under the target folder.</param>
/// <param name="Content">Readable file content stream.</param>
public sealed record FileUploadItem(string RelativePath, Stream Content);

/// <summary>
/// Request to upload files under a watched-root folder.
/// </summary>
/// <param name="RootId">Library root identifier.</param>
/// <param name="TargetPath">Root-relative target folder path.</param>
/// <param name="Items">Files to upload.</param>
public sealed record FileUploadRequest(Guid RootId, string? TargetPath, IReadOnlyList<FileUploadItem> Items);

/// <summary>
/// Request to rename one file or folder in place.
/// </summary>
/// <param name="RootId">Library root identifier.</param>
/// <param name="Path">Root-relative source path.</param>
/// <param name="Name">New basename in the same parent directory.</param>
public sealed record FileRenameRequest(Guid RootId, string Path, string Name);

/// <summary>
/// Request to move one file or folder to another watched-root path.
/// </summary>
/// <param name="SourceRootId">Source library root identifier.</param>
/// <param name="SourcePath">Source root-relative path.</param>
/// <param name="TargetRootId">Target library root identifier.</param>
/// <param name="TargetPath">Target root-relative destination path including basename.</param>
public sealed record FileMoveRequest(Guid SourceRootId, string SourcePath, Guid TargetRootId, string TargetPath);

/// <summary>
/// Request to permanently delete one file or directory.
/// </summary>
/// <param name="RootId">Library root identifier.</param>
/// <param name="Path">Root-relative path to delete.</param>
public sealed record FileDeleteRequest(Guid RootId, string Path);

/// <summary>
/// Request to add or remove a scan exclusion for one watched-root file or directory.
/// </summary>
/// <param name="RootId">Library root identifier.</param>
/// <param name="Path">Root-relative file or directory path. The root itself cannot be excluded.</param>
public sealed record FileExclusionRequest(Guid RootId, string Path);

/// <summary>
/// Request to rescan one watched root after filesystem changes.
/// </summary>
/// <param name="RootId">Library root identifier.</param>
/// <param name="Path">Optional root-relative path that triggered the rescan.</param>
public sealed record FileRescanRequest(Guid RootId, string? Path);

/// <summary>
/// Request to prepare one watched-root directory as a downloadable ZIP archive.
/// </summary>
/// <param name="RootId">Library root identifier.</param>
/// <param name="Path">Root-relative directory path. Empty string represents the root.</param>
public sealed record FileArchiveRequest(Guid RootId, string? Path);

/// <summary>
/// Progress snapshot for an asynchronously prepared folder archive.
/// </summary>
/// <param name="Id">Ephemeral archive preparation identifier.</param>
/// <param name="FileName">Download filename that will be used once ready.</param>
/// <param name="Ready">Whether the archive is ready to download.</param>
/// <param name="ProgressPercent">Compression progress from zero through one hundred.</param>
/// <param name="ProcessedFiles">Number of files already added to the archive.</param>
/// <param name="TotalFiles">Total number of visible files scheduled for the archive.</param>
/// <param name="Error">Preparation failure message, or null while preparing or ready.</param>
public sealed record FileArchivePreparation(
    Guid Id,
    string FileName,
    bool Ready,
    int ProgressPercent,
    int ProcessedFiles,
    int TotalFiles,
    string? Error);

/// <summary>
/// Result returned after a successful filesystem mutation.
/// </summary>
/// <param name="ScansQueued">Number of scan jobs queued for the affected root or roots.</param>
public sealed record FileOperationResponse(int ScansQueued);
