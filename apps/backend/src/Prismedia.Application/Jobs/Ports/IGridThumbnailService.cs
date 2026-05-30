namespace Prismedia.Application.Jobs.Ports;

/// <summary>
/// Port for generating (or refreshing) the small grid-card cover variant that the
/// entity grid serves alongside the full-resolution cover for responsive images.
/// </summary>
public interface IGridThumbnailService {
    /// <summary>
    /// Ensures a grid-sized cover variant exists for the entity, derived from its
    /// currently resolved best cover. No-op when the entity has no cover image.
    /// </summary>
    /// <param name="entityId">Entity whose grid thumbnail should be (re)generated.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task EnsureAsync(Guid entityId, CancellationToken cancellationToken);
}
