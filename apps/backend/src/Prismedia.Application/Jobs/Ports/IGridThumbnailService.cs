namespace Prismedia.Application.Jobs.Ports;

/// <summary>
/// Port for generating (or refreshing) the small grid-card cover variants that the
/// entity grid serves instead of the full-resolution cover for responsive images.
/// </summary>
public interface IGridThumbnailService {
    /// <summary>
    /// Ensures grid-sized cover variants (standard and double-density) exist for the
    /// entity, derived from its currently resolved best cover. No-op when the entity
    /// has no cover image.
    /// </summary>
    /// <param name="entityId">Entity whose grid thumbnails should be (re)generated.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task EnsureAsync(Guid entityId, CancellationToken cancellationToken);

    /// <summary>
    /// Lists entities whose grid variants are missing, stale (older than the cover
    /// they derive from), or gone from disk — the work list for the sweep job.
    /// Only includes entities whose resolved cover actually exists on disk, so the
    /// sweep converges instead of retrying covers it can never generate from.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<Guid>> ListEntitiesNeedingRefreshAsync(CancellationToken cancellationToken);
}
