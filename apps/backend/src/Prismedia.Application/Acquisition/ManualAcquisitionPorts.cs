namespace Prismedia.Application.Acquisition;

/// <summary>
/// Persistence boundary for reviewed, user-triggered replacements. Searching is transient; this store is
/// touched only after a candidate is chosen, when it atomically creates the upgrade child and its picker rows.
/// </summary>
public interface IManualReplacementStore {
    /// <summary>
    /// Resolves an on-disk single-file entity into its search input and currently owned quality without
    /// creating acquisition state. Returns null when the entity is missing, off disk, or not replaceable.
    /// </summary>
    Task<ManualReplacementSearchTarget?> GetSearchTargetAsync(
        Guid entityId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Revalidates the entity, materializes an imported baseline when a scanned item has no acquisition,
    /// creates one upgrade child, persists the reviewed candidates with their transient ids, and leaves the
    /// child awaiting selection. Returns null if another replacement already owns the entity's upgrade slot.
    /// </summary>
    Task<Guid?> CreateReviewedReplacementAsync(
        Guid entityId,
        IReadOnlyList<ReviewedReleaseCandidate> candidates,
        CancellationToken cancellationToken);
}

/// <summary>Persistence boundary for turning a completed browser upload into ordinary acquisition work.</summary>
public interface IAcquisitionUploadStore {
    /// <summary>
    /// Returns the existing off-disk acquisition, or creates the ordinary upgrade child for an on-disk
    /// entity. This is called when upload begins, so durable state starts only once bytes are submitted.
    /// </summary>
    Task<Guid?> PrepareAsync(Guid entityId, CancellationToken cancellationToken);

    /// <summary>
    /// Records the local adapter's completed item and payload path, marks the exact release as a manual pick,
    /// and publishes the durable Downloaded completion ticket.
    /// </summary>
    Task<bool> CompleteAsync(
        Guid acquisitionId,
        CompletedAcquisitionUpload upload,
        CancellationToken cancellationToken);
}
