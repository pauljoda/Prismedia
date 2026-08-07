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

    /// <summary>
    /// Gets only the reconciled queue lifecycle state needed by Entity detail actions, without
    /// candidates or the potentially large metadata proposal.
    /// </summary>
    async Task<IdentifyQueueItemStatus?> GetStatusAsync(
        Guid entityId,
        CancellationToken cancellationToken) =>
        await GetAsync(entityId, cancellationToken) is { } item
            ? new IdentifyQueueItemStatus(item.Id, item.EntityId, item.State, item.UpdatedAt)
            : null;

    /// <summary>
    /// Requests a provider search for the entity. The item enters the queued state and an interactive
    /// identify-search job runs the provider work; any search or cascade already in flight for the
    /// item is superseded.
    /// </summary>
    Task<IdentifyQueueItem> RequestSearchAsync(
        Guid entityId,
        IdentifyQueueSearchRequest request,
        bool hideNsfw,
        CancellationToken cancellationToken);

    /// <summary>
    /// Requests provider searches for a batch of entities, one identify-search job per entity.
    /// Entities that no longer exist are skipped.
    /// </summary>
    Task<IdentifyBulkAcceptedResponse> RequestSearchBatchAsync(
        IReadOnlyList<Guid> entityIds,
        IdentifyQueueSearchRequest request,
        bool hideNsfw,
        CancellationToken cancellationToken);

    /// <summary>
    /// Resolves a selected search candidate directly into a queue proposal without enqueueing a
    /// second search job or clearing the current candidate results while the provider lookup runs.
    /// </summary>
    Task<IdentifyQueueItem> ResolveCandidateAsync(
        Guid entityId,
        IdentifyQueueCandidateRequest request,
        bool hideNsfw,
        CancellationToken cancellationToken);

    /// <summary>Queues a reviewed proposal for application in the item's interactive graph.</summary>
    Task<IdentifyQueueItem> ApplyAsync(
        Guid entityId,
        ApplyIdentifyQueueItemRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Persists an in-progress proposal back onto the queued entity without applying it. Used while
    /// children are identified incrementally so resolved children survive navigation and refresh.
    /// The proposal must be the queue item's own root proposal (same proposal id and kind).
    /// </summary>
    Task<IdentifyQueueItem> SaveProposalAsync(
        Guid entityId,
        EntityMetadataProposal proposal,
        CancellationToken cancellationToken);

    /// <summary>Removes an entity from the active identify queue.</summary>
    Task<IdentifyQueueItem?> DeleteAsync(Guid entityId, CancellationToken cancellationToken);
}
