namespace Prismedia.Domain.Entities;

/// <summary>
/// Durable destructive ownership recorded on the stable Entity itself. Unlike monitor and acquisition
/// states, this claim also protects source-backed or monitorless Entity trees, so every explicit intent
/// mutation can serialize against the same core identity boundary.
/// </summary>
public enum EntityLifecycleClaimKind {
    /// <summary>Managed source-file deletion is reconciling this Entity subtree.</summary>
    [Code("deleting-files")]
    DeletingFiles
}
