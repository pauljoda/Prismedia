using Prismedia.Contracts.Plugins;

namespace Prismedia.Application.Plugins;

/// <summary>
/// Application-facing durable identify queue state machine.
/// </summary>
public interface IIdentifyQueueService {
    /// <summary>Lists active queue items, optionally including terminal history rows.</summary>
    Task<IReadOnlyList<IdentifyQueueItem>> ListAsync(
        bool includeCompleted,
        bool hideNsfw,
        CancellationToken cancellationToken);

    /// <summary>Adds an entity to the identify queue.</summary>
    Task<IdentifyQueueItem> AddAsync(Guid entityId, CancellationToken cancellationToken);

    /// <summary>Gets the queue item for an entity, or null when it is not queued.</summary>
    Task<IdentifyQueueItem?> GetAsync(Guid entityId, CancellationToken cancellationToken);

    /// <summary>Runs a provider search and persists candidates or a proposal.</summary>
    Task<IdentifyQueueItem> SearchAsync(
        Guid entityId,
        IdentifyQueueSearchRequest request,
        bool hideNsfw,
        CancellationToken cancellationToken);

    /// <summary>Applies a reviewed queue proposal and marks the item done.</summary>
    Task<IdentifyQueueItem> ApplyAsync(
        Guid entityId,
        ApplyIdentifyQueueItemRequest request,
        CancellationToken cancellationToken);

    /// <summary>Removes an entity from the active identify queue.</summary>
    Task<IdentifyQueueItem?> DeleteAsync(Guid entityId, CancellationToken cancellationToken);
}
