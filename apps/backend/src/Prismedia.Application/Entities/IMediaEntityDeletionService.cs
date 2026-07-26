namespace Prismedia.Application.Entities;

/// <summary>Why a managed media deletion was refused.</summary>
public enum MediaEntityDeleteFailureKind {
    /// <summary>The requested Entity does not exist.</summary>
    NotFound,

    /// <summary>The Entity kind cannot safely root managed file deletion.</summary>
    NotDeletable,

    /// <summary>Transient lifecycle state prevents a safe delete or reacquisition.</summary>
    Conflict
}

/// <summary>Outcome of deleting one media entity together with its managed files on disk.</summary>
/// <param name="Deleted">True when the Entity subtree and its files were removed.</param>
/// <param name="Message">Human-readable failure detail when <paramref name="Deleted"/> is false.</param>
/// <param name="FilesDeleted">How many on-disk paths (files or folders) were removed.</param>
/// <param name="Reverted">Compatibility field retained for older clients; full deletion always returns false.</param>
/// <param name="FailureKind">Structured refusal reason when <paramref name="Deleted"/> is false.</param>
public sealed record MediaEntityDeleteResult(
    bool Deleted,
    string? Message = null,
    int FilesDeleted = 0,
    bool Reverted = false,
    MediaEntityDeleteFailureKind? FailureKind = null);

/// <summary>One selected bulk-delete root that could not be processed.</summary>
public sealed record MediaEntityBulkDeleteFailure(Guid EntityId, string Message);

/// <summary>
/// Outcome of one hierarchy-normalized bulk delete. Selected descendants covered by a successfully
/// processed selected ancestor count as deleted without running a second destructive lifecycle.
/// </summary>
public sealed record MediaEntityBulkDeleteResult(
    int Deleted,
    int FilesDeleted,
    IReadOnlyList<MediaEntityBulkDeleteFailure> Failures,
    int Reverted);

/// <summary>
/// Application port for fully deleting file-backed media entities — the destructive counterpart to the
/// taxonomy-only <see cref="IEntityManagementService"/>. Delete removes the selected Entity subtree,
/// monitored or unmonitored, together with its monitor/acquisition state, generated assets, and managed
/// source files. Provider identities are suppressed so a surviving monitored ancestor cannot rediscover the
/// removed branch. There is no soft-delete, reacquisition, or undo, and disk deletion is guarded by watched
/// library roots.
/// </summary>
public interface IMediaEntityDeletionService {
    /// <summary>
    /// Deletes one media Entity and its complete descendant tree per the full-removal semantics above.
    /// <paramref name="deleteFiles"/> must be true and permanently removes source files/folders from disk. Every source path
    /// must resolve inside a watched library root and delete successfully (already-missing counts as done),
    /// otherwise the operation is refused without changing library rows. Library-only removal is deliberately
    /// unsupported because the next scan would rediscover media that remains on disk; Remove wanted and
    /// Unmonitor own the non-file-destructive lifecycle flows.
    /// </summary>
    Task<MediaEntityDeleteResult> DeleteAsync(Guid id, bool deleteFiles, CancellationToken cancellationToken);

    /// <summary>
    /// Snapshots the requested hierarchy, collapses overlapping selections to their top-most roots, and
    /// deletes each root exactly once. This makes input ordering irrelevant and prevents a child-first
    /// request from deleting source files before its selected wrapper is processed.
    /// </summary>
    Task<MediaEntityBulkDeleteResult> DeleteManyAsync(
        IReadOnlyList<Guid> ids,
        bool deleteFiles,
        CancellationToken cancellationToken);
}
