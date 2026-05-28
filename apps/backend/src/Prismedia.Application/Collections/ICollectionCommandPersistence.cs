using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Media;

namespace Prismedia.Application.Collections;

/// <summary>Persistence port used by collection command orchestration.</summary>
public interface ICollectionCommandPersistence {
    /// <summary>Persists a new collection aggregate and returns its identifier.</summary>
    Task<Guid> CreateAsync(Collection collection, string? description, CancellationToken cancellationToken);

    /// <summary>Persists collection aggregate settings and description for an existing collection.</summary>
    Task<bool> UpdateAsync(Collection collection, string? description, CancellationToken cancellationToken);

    /// <summary>Soft-deletes a collection entity.</summary>
    Task<bool> DeleteAsync(Guid collectionId, CancellationToken cancellationToken);

    /// <summary>Returns the current collection mode for an active collection.</summary>
    Task<CollectionMode?> GetModeAsync(Guid collectionId, CancellationToken cancellationToken);

    /// <summary>Loads active entities by id so the application can validate collection membership.</summary>
    Task<IReadOnlyDictionary<Guid, CollectionItemCandidate>> GetActiveItemsAsync(
        IReadOnlyList<Guid> entityIds,
        CancellationToken cancellationToken);

    /// <summary>Adds manual collection item rows, skipping already-present entity ids.</summary>
    Task<int> AddManualItemsAsync(Guid collectionId, IReadOnlyList<Guid> entityIds, CancellationToken cancellationToken);

    /// <summary>Removes collection item rows by collection item id.</summary>
    Task<int> RemoveItemsAsync(Guid collectionId, IReadOnlyList<Guid> itemIds, CancellationToken cancellationToken);

    /// <summary>Reorders existing collection item rows by preferred item id order.</summary>
    Task<int> ReorderItemsAsync(Guid collectionId, IReadOnlyList<Guid> itemIds, CancellationToken cancellationToken);

    /// <summary>Returns whether an active collection exists.</summary>
    Task<bool> ExistsAsync(Guid collectionId, CancellationToken cancellationToken);

    /// <summary>Counts persisted collection item rows.</summary>
    Task<int> CountItemsAsync(Guid collectionId, CancellationToken cancellationToken);

    /// <summary>Filters rule matches to active, visible entities while preserving rule-order uniqueness.</summary>
    Task<IReadOnlyList<CollectionVisibleRuleMatch>> FilterVisibleRuleMatchesAsync(
        IReadOnlyList<CollectionRuleMatch> matches,
        bool hideNsfw,
        CancellationToken cancellationToken);
}

/// <summary>Active entity candidate used to validate manual collection membership requests.</summary>
public sealed record CollectionItemCandidate(Guid EntityId, EntityKind EntityKind);

/// <summary>Visible rule match after persistence-level visibility filtering.</summary>
public sealed record CollectionVisibleRuleMatch(string EntityType, Guid EntityId);
