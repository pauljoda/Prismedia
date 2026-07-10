namespace Prismedia.Application.Entities;

/// <summary>
/// Serializes any mutation of an existing Entity subtree against durable destructive ownership. Request,
/// Identify, scan/import binding, and monitor workflows use this same boundary so the Entity—not a
/// medium-specific row—is the authoritative lifecycle anchor.
/// </summary>
public interface IEntityLifecycleMutationLease {
    /// <summary>
    /// Runs <paramref name="mutation"/> only when the target and its ancestors have no destructive Entity
    /// claim and no monitor lifecycle claim. Production implementations hold the relevant database locks
    /// through the callback. Returns false without invoking it when cleanup owns the Entity.
    /// </summary>
    Task<bool> ExecuteAsync(
        Guid entityId,
        Func<CancellationToken, Task> mutation,
        CancellationToken cancellationToken);

    /// <summary>
    /// Runs one atomic mutation against several existing Entities while acquiring the combined monitor
    /// and Entity ancestry in deterministic order. Batch scanners use this form so independently ordered
    /// rows cannot deadlock while a destructive lifecycle operation is being published.
    /// </summary>
    async Task<bool> ExecuteManyAsync(
        IReadOnlyCollection<Guid> entityIds,
        Func<CancellationToken, Task> mutation,
        CancellationToken cancellationToken) {
        var orderedEntityIds = entityIds.Distinct().Order().ToArray();

        async Task<bool> ExecuteAtAsync(int index, CancellationToken token) {
            if (index == orderedEntityIds.Length) {
                await mutation(token);
                return true;
            }

            var innerExecuted = false;
            var acquired = await ExecuteAsync(
                orderedEntityIds[index],
                async innerToken => innerExecuted = await ExecuteAtAsync(index + 1, innerToken),
                token);
            return acquired && innerExecuted;
        }

        return await ExecuteAtAsync(0, cancellationToken);
    }
}

/// <summary>
/// Signals that a background Entity mutation must retry because destructive lifecycle ownership won.
/// This is intentionally distinct from a missing Entity: callers must not fall through and create a
/// duplicate while the original is being reconciled.
/// </summary>
public sealed class EntityLifecycleMutationConflictException(Guid entityId)
    : InvalidOperationException($"Entity '{entityId}' is owned by destructive lifecycle cleanup.") {
    /// <summary>The stable Entity whose lifecycle rejected the mutation.</summary>
    public Guid EntityId { get; } = entityId;
}
