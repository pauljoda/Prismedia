namespace Prismedia.Application.Entities;

/// <summary>
/// Repairs the persisted Entity availability read projection from authoritative acquisition,
/// hierarchy, and source-file state.
/// </summary>
public interface IEntityAvailabilityReconciler {
    /// <summary>Rebuilds drifted availability rows and returns the number repaired.</summary>
    Task<int> ReconcileAsync(CancellationToken cancellationToken);
}
