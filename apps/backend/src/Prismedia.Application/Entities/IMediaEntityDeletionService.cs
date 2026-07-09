namespace Prismedia.Application.Entities;

/// <summary>Outcome of deleting one media entity (and optionally its files on disk).</summary>
/// <param name="Deleted">True when the delete was carried out (files removed; entities removed or reverted).</param>
/// <param name="Message">Human-readable failure detail when <paramref name="Deleted"/> is false.</param>
/// <param name="FilesDeleted">How many on-disk paths (files or folders) were removed.</param>
/// <param name="Reverted">
/// True when the entity was under active monitoring and therefore reverted to a wanted placeholder
/// (files deleted, library entry kept) instead of being removed from the library.
/// </param>
public sealed record MediaEntityDeleteResult(bool Deleted, string? Message = null, int FilesDeleted = 0, bool Reverted = false);

/// <summary>
/// Application port for deleting file-backed media entities — the destructive counterpart to the
/// taxonomy-only <see cref="IEntityManagementService"/>. Delete manages DISK state; monitoring is managed
/// separately and is never changed by a delete:
/// <list type="bullet">
/// <item>An entity under ACTIVE monitoring (itself or any ancestor container) REVERTS: its files are
/// permanently deleted and the entity tree becomes wanted placeholders again. Any acquisition that
/// imported those files is replaced with a clean retry and starts searching immediately — the "downloaded
/// the wrong season, delete it and let it re-find" flow. No provider identity is suppressed.</item>
/// <item>An unmonitored entity is REMOVED: the entity and its descendant tree leave the library, its
/// provider identities are suppressed so a future container sweep never re-requests it, and its files
/// are deleted when asked.</item>
/// </list>
/// Both paths remove in-flight download-client data. Full removal hard-deletes acquisition state; revert
/// replaces it with a clean search. There is no soft-delete or undo, and disk deletion is only guarded by
/// the watched library roots.
/// </summary>
public interface IMediaEntityDeletionService {
    /// <summary>
    /// Deletes one media entity's disk presence per the monitor-aware semantics above.
    /// <paramref name="deleteFiles"/> permanently removes source files/folders from disk (paths outside
    /// every watched library root are skipped, never deleted); without it, only the library state changes.
    /// </summary>
    Task<MediaEntityDeleteResult> DeleteAsync(Guid id, bool deleteFiles, CancellationToken cancellationToken);
}
