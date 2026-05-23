using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Ports;

/// <summary>
/// Port for collection membership operations during dynamic rule refresh.
/// </summary>
public interface ICollectionRefreshPersistence {
    /// <summary>
    /// Gets a dynamic collection's metadata by its entity ID.
    /// Returns null if the collection doesn't exist or is not dynamic/hybrid mode.
    /// </summary>
    Task<CollectionRefreshData?> GetDynamicCollectionAsync(Guid collectionEntityId, CancellationToken cancellationToken);

    /// <summary>
    /// Atomically replaces all dynamic items in a collection with the given resolved items,
    /// preserving any manual items, and updates the collection's item count and refresh timestamp.
    /// </summary>
    Task RefreshCollectionItemsAsync(
        Guid collectionEntityId,
        IReadOnlyList<CollectionRuleMatch> resolvedItems,
        CancellationToken cancellationToken);
}

/// <summary>
/// Snapshot of a dynamic collection's metadata needed for rule evaluation.
/// </summary>
/// <param name="EntityId">The collection entity's ID.</param>
/// <param name="Title">Collection title for logging.</param>
/// <param name="Mode">Collection mode (dynamic or hybrid).</param>
/// <param name="RuleTreeJson">The stored rule tree JSON.</param>
public sealed record CollectionRefreshData(
    Guid EntityId,
    string Title,
    CollectionMode Mode,
    string RuleTreeJson);
