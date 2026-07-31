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
    /// Lists collections owned by the current caller whose manual membership can be changed. Shared
    /// collections owned by another user and rule-only dynamic collections are intentionally omitted.
    /// </summary>
    /// <param name="hideNsfw">When true, NSFW collections are omitted.</param>
    /// <param name="cancellationToken">Cancellation token for the read operation.</param>
    /// <returns>Small collection options suitable for add-to-collection controls.</returns>
    Task<CollectionMembershipOptionsResponse> ListMembershipOptionsAsync(
        bool hideNsfw,
        CancellationToken cancellationToken) =>
        Task.FromResult(new CollectionMembershipOptionsResponse([]));

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

    /// <summary>
    /// Batched list-row context for many collections at once: the visible member count and whether any
    /// member is an audio kind (which projects the box set as an audio playlist). ONE grouped query for
    /// the whole batch — list surfaces must never hydrate per-collection membership per row (the
    /// original Jellyfin views N+1 took a minute on large libraries). Collections with no visible
    /// members are absent from the result.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, CollectionListContext>> GetListContextsAsync(
        IReadOnlyList<Guid> collectionIds,
        bool hideNsfw,
        CancellationToken cancellationToken);
}

/// <summary>One collection's cheap list-row context: visible member count and audio capability.</summary>
public sealed record CollectionListContext(int ChildCount, bool HasAudio);
