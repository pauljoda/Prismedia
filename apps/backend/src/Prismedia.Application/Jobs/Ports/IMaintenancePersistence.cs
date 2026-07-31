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

    /// <summary>Returns active IDs across an entity-kind group in one persistence query.</summary>
    Task<IReadOnlyList<Guid>> GetActiveEntityIdsByKindsAsync(
        IReadOnlyCollection<EntityKind> kinds,
        CancellationToken cancellationToken);

    /// <summary>Validates conventional generated files owned by one asset family.</summary>
    Task<int> ValidateGeneratedAssetsAsync(
        GeneratedAssetFamily family,
        IReadOnlyCollection<Guid> activeEntityIds,
        CancellationToken cancellationToken);

    /// <summary>Removes conventional orphan cache entries for one asset family.</summary>
    Task<int> CleanupOrphanedGeneratedAssetsAsync(
        GeneratedAssetFamily family,
        IReadOnlyCollection<Guid> activeEntityIds,
        CancellationToken cancellationToken);

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

    /// <summary>
    /// Removes old app-owned subtitle cache files that no persisted subtitle row references.
    /// A grace period protects generations that an active extraction has published but not committed.
    /// </summary>
    Task<int> CleanupOrphanedSubtitleAssetsAsync(CancellationToken cancellationToken) =>
        Task.FromResult(0);
}
