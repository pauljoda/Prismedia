using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Ports;

/// <summary>
/// Port for library maintenance operations — querying entity IDs for cache validation
/// and cleaning up orphaned cache entries.
/// </summary>
public interface IMaintenancePersistence {
    /// <summary>
    /// Returns all non-deleted entity IDs for the given entity kind.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetActiveEntityIdsByKindAsync(EntityKind kind, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the base cache directory path (e.g. /data/cache).
    /// </summary>
    string GetCacheBasePath();

    /// <summary>
    /// Removes generated preview/cache records and files for an entity so a rebuild job
    /// creates fresh derived media instead of reusing stale thumbnails, previews,
    /// adaptive streams, trickplay sheets, or waveforms.
    /// </summary>
    /// <param name="kind">Entity kind whose generated asset paths should be invalidated.</param>
    /// <param name="entityId">Entity identifier to invalidate.</param>
    /// <param name="cancellationToken">Cancellation token for database work.</param>
    Task ClearGeneratedPreviewAssetsAsync(
        EntityKind kind,
        Guid entityId,
        CancellationToken cancellationToken);
}
