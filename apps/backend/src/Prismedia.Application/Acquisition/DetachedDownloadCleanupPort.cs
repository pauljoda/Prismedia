namespace Prismedia.Application.Acquisition;

/// <summary>
/// Persists prior download-client ownership independently from the acquisition's single active transfer
/// pointer, allowing an explicit replacement to proceed without abandoning exact cleanup provenance.
/// </summary>
public interface IDetachedDownloadCleanupStore {
    /// <summary>
    /// Atomically moves the exact current transfer into detached cleanup. False means the pointer changed,
    /// the recorded owner is unavailable, or another lifecycle operation won the race.
    /// </summary>
    Task<bool> DetachAsync(
        Guid acquisitionId,
        Guid downloadClientConfigId,
        string clientItemId,
        CancellationToken cancellationToken);

    /// <summary>Lists detached client items awaiting confirmed removal.</summary>
    Task<IReadOnlyList<DetachedDownloadCleanup>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Completes one cleanup after the exact item is confirmed absent from its recorded client.</summary>
    Task CompleteAsync(Guid cleanupId, CancellationToken cancellationToken);
}
