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
