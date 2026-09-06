namespace Prismedia.Application.Acquisition;

/// <summary>Recovery either proves an installed replacement, leaves an untouched candidate ready, or explains a hold.</summary>
public sealed record AtomicUpgradeFileRecovery(OwnedFileReplaceResult? Installed = null, string? HoldReason = null);

/// <summary>Physical preparation and evidence checks for a database-checkpointed, same-path replacement.</summary>
public interface IAtomicUpgradeFiles {
    /// <summary>Selects exact files and records their facts without mutating either file. Invalid or ambiguous inputs raise an IO error.</summary>
    Task<AtomicUpgradeFilePlan> PrepareAsync(UpgradeReplaceTarget target, CancellationToken cancellationToken);

    /// <summary>Proves an installed swap or restores a provable staged candidate for validation; ambiguous evidence is retained for review.</summary>
    Task<AtomicUpgradeFileRecovery> RecoverAsync(AtomicUpgradeCheckpoint checkpoint, CancellationToken cancellationToken);

    /// <summary>Swaps only the prepared files, retaining both the old bytes and incoming evidence until the database commit.</summary>
    Task<OwnedFileReplaceResult> ReplaceAsync(AtomicUpgradeCheckpoint checkpoint, CancellationToken cancellationToken);

    /// <summary>Releases incoming-byte evidence only after the installation transaction committed; the original backup remains recoverable.</summary>
    Task CompleteAsync(AtomicUpgradeCheckpoint checkpoint, CancellationToken cancellationToken);
}
