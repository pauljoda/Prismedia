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
    /// Ensures static thumbnails for several changed entities in one coalesced pass. Implementations
    /// may fold shared descendants and ancestors together so a parent with many changed children is
    /// rendered only after the child thumbnails are current.
    /// </summary>
    /// <param name="entityIds">Changed entities whose thumbnail chains should be refreshed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    async Task EnsureManyAsync(
        IReadOnlyCollection<Guid> entityIds,
        CancellationToken cancellationToken) {
        foreach (var entityId in entityIds.Distinct()) {
            await EnsureAsync(entityId, cancellationToken);
        }
    }

    /// <summary>
    /// Lists the highest roots of thumbnail chains whose static variants are missing,
    /// stale, or gone from disk. Sources include own artwork, a persisted reader cover
    /// page, and structural-child artwork, so one returned root can repair its subtree
    /// bottom-up without repeated parent work.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<Guid>> ListEntitiesNeedingRefreshAsync(CancellationToken cancellationToken);
}
