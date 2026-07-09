namespace Prismedia.Application.Entities;

/// <summary>Outcome of deleting one media entity (and optionally its files on disk).</summary>
/// <param name="Deleted">True when the entity (and its descendants) were removed from the library.</param>
/// <param name="Message">Human-readable failure detail when <paramref name="Deleted"/> is false.</param>
/// <param name="FilesDeleted">How many on-disk paths (files or folders) were removed.</param>
public sealed record MediaEntityDeleteResult(bool Deleted, string? Message = null, int FilesDeleted = 0);

/// <summary>
/// Application port for deleting file-backed media entities — the destructive counterpart to the
/// taxonomy-only <see cref="IEntityManagementService"/>. Deleting a media entity removes the entity and
/// its entire descendant tree (a series takes its seasons and episodes) from the library, tears down any
/// monitors and acquisitions targeting them (removing in-flight downloads from the download client),
/// suppresses the entity's provider identities so a monitored parent container never re-requests it, and —
/// when asked — permanently deletes the entity's source files/folders from disk. Hard delete throughout:
/// there is no soft-delete or undo; disk deletion is only guarded by the watched library roots.
/// </summary>
public interface IMediaEntityDeletionService {
    /// <summary>
    /// Deletes one media entity and its descendants. <paramref name="deleteFiles"/> also permanently
    /// removes their source files/folders from disk (paths outside every watched library root are
    /// skipped, never deleted). Without it the rows are removed but files stay — the next scan will
    /// re-import them, so callers should pass true for library kinds unless the files are being handled
    /// separately.
    /// </summary>
    Task<MediaEntityDeleteResult> DeleteAsync(Guid id, bool deleteFiles, CancellationToken cancellationToken);
}
