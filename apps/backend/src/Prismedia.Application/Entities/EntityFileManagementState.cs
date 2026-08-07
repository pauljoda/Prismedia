namespace Prismedia.Application.Entities;

/// <summary>
/// Shared projection state for managed file actions. Physical source ownership remains the truth for
/// <c>HasSourceMedia</c>, while a durable in-progress deletion keeps the same action available solely so
/// the user can resume a crash-interrupted workflow.
/// </summary>
/// <param name="HasSourceBackedSubtree">Whether this Entity or a structural descendant owns source media.</param>
/// <param name="HasRecoverableDeletion">Whether durable deletion state allows the shared delete action to resume.</param>
public readonly record struct EntityFileManagementState(
    bool HasSourceBackedSubtree,
    bool HasRecoverableDeletion) {
    /// <summary>Whether the shared managed delete-files action is currently valid.</summary>
    public bool CanDeleteFiles => HasSourceBackedSubtree || HasRecoverableDeletion;
}

/// <summary>
/// Resolves Entity roots whose durable lifecycle state represents a resumable managed-file deletion.
/// This is intentionally separate from source ownership: a crash may leave recovery state after every
/// physical source row has already been removed.
/// </summary>
public interface IEntityFileDeletionRecoveryReader {
    /// <summary>Returns the requested Entity ids that can resume an interrupted file deletion.</summary>
    Task<IReadOnlySet<Guid>> ResolveAsync(
        IReadOnlyCollection<Guid> entityIds,
        CancellationToken cancellationToken);
}

/// <summary>
/// Durable discovery and compare-and-delete operations used by the worker that resumes lifecycle
/// claims left behind by process termination. Candidates are deliberately bounded so one bad record
/// cannot monopolize the worker.
/// </summary>
public interface IEntityLifecycleRecoveryStore {
    /// <summary>Lists the oldest recoverable claims, bounded by <paramref name="limit"/> in total.</summary>
    Task<EntityLifecycleRecoveryBatch> ListAsync(int limit, CancellationToken cancellationToken);

    /// <summary>
    /// Removes one orphaned delete-files monitor only when it is still claimed and no referenced Entity
    /// or acquisition exists. Returns false if the row changed or regained a live owner.
    /// </summary>
    Task<bool> CompleteOrphanedDeletionAsync(Guid monitorId, CancellationToken cancellationToken);
}

/// <summary>One bounded set of independently resumable lifecycle claims.</summary>
public sealed record EntityLifecycleRecoveryBatch(
    IReadOnlyList<Guid> DeletingEntityIds,
    IReadOnlyList<Guid> OrphanedDeletingMonitorIds,
    IReadOnlyList<Guid> StoppingMonitorIds) {
    /// <summary>Total candidates in the batch.</summary>
    public int Count => DeletingEntityIds.Count + OrphanedDeletingMonitorIds.Count + StoppingMonitorIds.Count;
}
