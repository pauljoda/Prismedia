using Prismedia.Contracts.Collections;

namespace Prismedia.Application.Collections;

/// <summary>Outcome category for collection command use cases.</summary>
public enum CollectionCommandStatus {
    /// <summary>The command completed successfully.</summary>
    Succeeded,

    /// <summary>The target collection or entity was not found.</summary>
    NotFound,

    /// <summary>The request body was structurally valid JSON but invalid for the collection domain.</summary>
    Invalid
}

/// <summary>Result wrapper for commands that return a collection detail contract.</summary>
/// <param name="Status">Command outcome.</param>
/// <param name="Collection">Updated collection detail when successful.</param>
/// <param name="Message">Human-readable error detail when unsuccessful.</param>
public sealed record CollectionWriteResult(
    CollectionCommandStatus Status,
    CollectionDetail? Collection = null,
    string? Message = null);

/// <summary>Result wrapper for commands that return an affected row count.</summary>
/// <param name="Status">Command outcome.</param>
/// <param name="Count">Affected row count when successful.</param>
/// <param name="Message">Human-readable error detail when unsuccessful.</param>
public sealed record CollectionCountResult(
    CollectionCommandStatus Status,
    int Count = 0,
    string? Message = null);

/// <summary>Result wrapper for commands without a collection detail body.</summary>
/// <param name="Status">Command outcome.</param>
/// <param name="Message">Human-readable error detail when unsuccessful.</param>
public sealed record CollectionCommandResult(
    CollectionCommandStatus Status,
    string? Message = null);

/// <summary>
/// Application port for collection write use cases. Implementations own persistence
/// details while keeping HTTP endpoints thin and collection-specific.
/// </summary>
public interface ICollectionCommandService {
    /// <summary>Creates a user collection and returns its detail read model.</summary>
    Task<CollectionWriteResult> CreateAsync(
        CollectionWriteRequest request,
        CancellationToken cancellationToken);

    /// <summary>Fully updates collection settings and returns the updated detail read model.</summary>
    Task<CollectionWriteResult> UpdateAsync(
        Guid collectionId,
        CollectionWriteRequest request,
        CancellationToken cancellationToken);

    /// <summary>Soft-deletes a collection entity.</summary>
    Task<CollectionCommandResult> DeleteAsync(
        Guid collectionId,
        CancellationToken cancellationToken);

    /// <summary>Adds manual item references to a collection, skipping existing entries.</summary>
    Task<CollectionCountResult> AddItemsAsync(
        Guid collectionId,
        CollectionAddItemsRequest request,
        CancellationToken cancellationToken);

    /// <summary>Removes collection item rows from a collection.</summary>
    Task<CollectionCountResult> RemoveItemsAsync(
        Guid collectionId,
        CollectionRemoveItemsRequest request,
        CancellationToken cancellationToken);

    /// <summary>Reorders collection item rows inside a collection.</summary>
    Task<CollectionCountResult> ReorderItemsAsync(
        Guid collectionId,
        CollectionReorderItemsRequest request,
        CancellationToken cancellationToken);

    /// <summary>Evaluates a dynamic rule tree without persisting membership changes.</summary>
    Task<CollectionRulePreviewResponse?> PreviewRulesAsync(
        CollectionRulePreviewRequest request,
        bool hideNsfw,
        CancellationToken cancellationToken);

    /// <summary>Refreshes persisted dynamic membership for a collection.</summary>
    Task<CollectionRefreshResponse?> RefreshAsync(
        Guid collectionId,
        CancellationToken cancellationToken);
}
