using Prismedia.Contracts.Entities;
using Prismedia.Domain.Media;

namespace Prismedia.Application.Entities;

/// <summary>Entity-agnostic read boundary for ordered image-page manifests and page resources.</summary>
public interface IEntityReaderService {
    /// <summary>Gets the visible Entity's complete reader manifest, or null when unavailable.</summary>
    Task<EntityReaderManifestResponse?> GetManifestAsync(
        Guid entityId,
        CancellationToken cancellationToken);

    /// <summary>Resolves one visible manifest page to an internal source path, or null when unavailable.</summary>
    Task<EntityReaderPageSource?> GetPageAsync(
        Guid entityId,
        int ordinal,
        CancellationToken cancellationToken);
}

/// <summary>Internal file source resolved for one reader page.</summary>
/// <param name="Path">Plain file or synthetic archive-member path consumed by the shared file streamer.</param>
/// <param name="MimeType">Content type returned to the client.</param>
public sealed record EntityReaderPageSource(string Path, string MimeType);

/// <summary>Persistence boundary for transactional Entity page-manifest replacement.</summary>
public interface IEntityPageManifestStore {
    /// <summary>
    /// Replaces the complete manifest. Returns false when the persisted source signature already
    /// matches and no write was needed.
    /// </summary>
    Task<bool> ReplaceAsync(EntityPageManifest manifest, CancellationToken cancellationToken);

    /// <summary>Removes a manifest and all of its pages. Returns whether one existed.</summary>
    Task<bool> RemoveAsync(Guid entityId, CancellationToken cancellationToken);
}
