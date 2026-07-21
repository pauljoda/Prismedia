using Prismedia.Contracts.Acquisition;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Acquisition;

public sealed partial class AcquisitionService {
    /// <inheritdoc />
    public Task<bool> AnyOpenForEntityAsync(Guid entityId, CancellationToken cancellationToken) =>
        store.AnyOpenForEntityAsync(entityId, cancellationToken);

    /// <inheritdoc />
    public Task<bool> AnyOpenForEntityAsync(
        Guid entityId,
        BookRendition? bookRendition,
        CancellationToken cancellationToken) =>
        store.AnyOpenForEntityAsync(entityId, bookRendition, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlySet<Guid>> FilterOpenEntityIdsAsync(
        IReadOnlyCollection<Guid> entityIds,
        BookRendition? bookRendition,
        CancellationToken cancellationToken) =>
        store.FilterOpenEntityIdsAsync(entityIds, bookRendition, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<Guid>> ListIdsForEntityAsync(Guid entityId, CancellationToken cancellationToken) =>
        store.ListIdsForEntityAsync(entityId, cancellationToken);

    /// <summary>The durable acquisition activity log, newest-first.</summary>
    public Task<IReadOnlyList<AcquisitionHistoryView>> ListHistoryAsync(
        int limit,
        Guid? entityId,
        CancellationToken cancellationToken) =>
        history.ListAsync(limit, entityId, cancellationToken);

    /// <summary>Records a durable Removed event for an acquisition being cancelled or deleted. Best-effort.</summary>
    private Task RecordRemovedAsync(AcquisitionSummary summary, string message, CancellationToken cancellationToken) =>
        history.SafeAddAsync(logger, new AcquisitionHistoryEntry(
            summary.Id,
            summary.EntityId,
            summary.Kind,
            AcquisitionHistoryEvent.Removed,
            summary.Title,
            Message: message),
            cancellationToken);
}
