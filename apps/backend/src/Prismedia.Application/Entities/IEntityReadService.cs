using Prismedia.Contracts.Entities;

namespace Prismedia.Application.Entities;

/// <summary>
/// Application port for entity read use cases. Infrastructure implements this with a
/// row-optimized browse/thumbnail projection plus a domain-hydration projection for
/// card and detail reads.
/// </summary>
public interface IEntityReadService {
    /// <summary>
    /// Lists active entities as thumbnail read models, optionally scoped by kind,
    /// search text, NSFW visibility, and cursor.
    /// </summary>
    Task<EntityListResponse> ListAsync(
        string? kind,
        string? query,
        string? cursor,
        bool? hideNsfw,
        int? limit,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets one active entity as the shared entity card read model.
    /// </summary>
    Task<EntityCard?> GetAsync(Guid id, bool hideNsfw, CancellationToken cancellationToken);

    /// <summary>
    /// Gets thumbnails for the requested identifiers while preserving the caller's
    /// requested order.
    /// </summary>
    Task<EntityThumbnailBatchResponse> GetThumbnailsAsync(
        IReadOnlyList<Guid> ids,
        bool hideNsfw,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets one active entity as its kind-specific detail contract. Returns null when
    /// the entity does not exist or does not match the requested kind.
    /// </summary>
    Task<IEntityCard?> GetDetailAsync(Guid id, string kind, bool hideNsfw, CancellationToken cancellationToken);
}
