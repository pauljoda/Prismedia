using Prismedia.Contracts.Collections;

namespace Prismedia.Application.Collections;

/// <summary>
/// Reads ordered collection membership for display and shell-level playback.
/// </summary>
public interface ICollectionItemReadService {
    /// <summary>
    /// Lists active items in a collection, preserving collection sort order and applying
    /// the requested visibility filter to hidden entities.
    /// </summary>
    /// <param name="collectionId">Collection entity identifier.</param>
    /// <param name="hideNsfw">When true, NSFW item entities are omitted.</param>
    /// <param name="cancellationToken">Cancellation token for the read operation.</param>
    /// <returns>Ordered collection items, or an empty list when the collection has no visible items.</returns>
    Task<CollectionItemsResponse> ListItemsAsync(
        Guid collectionId,
        bool hideNsfw,
        CancellationToken cancellationToken);
}
