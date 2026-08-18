namespace Prismedia.Application.Entities;

/// <summary>
/// Repairs the persisted Entity rollup projections (inherited context, descendant counts,
/// reference counts, collection membership counts) from authoritative hierarchy and link state.
/// </summary>
public interface IEntityRollupReconciler {
    /// <summary>Rebuilds drifted rollup rows and returns the number repaired.</summary>
    Task<int> ReconcileAsync(CancellationToken cancellationToken);
}
