using Prismedia.Contracts.Plugins;

namespace Prismedia.Application.Jobs.Ports;

/// <summary>
/// Port for bulk identify operations that search a provider and persist results to the durable queue.
/// </summary>
public interface IBulkIdentifyProvider {
    Task SearchAndQueueAsync(Guid entityId, string provider, IdentifyQuery? query, bool hideNsfw, CancellationToken cancellationToken);

    /// <summary>
    /// Whether the entity's identify queue item was already resolved at or after the given instant —
    /// a search result from this provider landed, or the user accepted/rejected the item. Lets a
    /// retried bulk identify job resume after the entities it finished on earlier attempts instead
    /// of re-searching the whole batch from the start.
    /// </summary>
    /// <param name="entityId">Entity whose queue item is checked.</param>
    /// <param name="provider">Provider the bulk batch searches with.</param>
    /// <param name="since">Batch start instant; only results at or after it count.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> HasResultSinceAsync(Guid entityId, string provider, DateTimeOffset since, CancellationToken cancellationToken);
}
