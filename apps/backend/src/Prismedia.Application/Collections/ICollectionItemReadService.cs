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

    /// <summary>
    /// Resolves a representative cover image path for each requested collection so clients that
    /// only render an entity's own artwork (e.g. Jellyfin/Infuse) still show a poster. Each
    /// collection prefers its configured cover item, falling back to its first visible member's
    /// cover. Collections with no resolvable cover are omitted from the result.
    /// </summary>
    /// <param name="collectionIds">Collection entity identifiers to resolve covers for.</param>
    /// <param name="hideNsfw">When true, NSFW cover items and members are skipped.</param>
    /// <param name="cancellationToken">Cancellation token for the read operation.</param>
    /// <returns>Map of collection id to a representative cover asset path.</returns>
    Task<IReadOnlyDictionary<Guid, string>> ResolveCoverPathsAsync(
        IReadOnlyList<Guid> collectionIds,
        bool hideNsfw,
        CancellationToken cancellationToken);
}
