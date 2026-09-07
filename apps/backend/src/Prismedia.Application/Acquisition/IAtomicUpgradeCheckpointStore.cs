using Prismedia.Domain.Entities;

namespace Prismedia.Application.Acquisition;

/// <summary>Physical and release facts proposed under the owning Entity's lifecycle lease.</summary>
public sealed record AtomicUpgradePreparation(
    Guid ParentAcquisitionId,
    Guid ParentEntityId,
    EntityKind Kind,
    AtomicUpgradeFilePlan Files,
    string TransferContentPath,
    string? TransferClientItemId,
    SelectedRelease SelectedRelease);

/// <summary>Native acquisition checkpoint persistence for atomic replacements, sharing the acquisition's recovery slot.</summary>
public interface IAtomicUpgradeCheckpointStore {
    /// <summary>Reads an existing atomic preparation; malformed or incompatible evidence must raise an error.</summary>
    Task<AtomicUpgradeCheckpoint?> GetAsync(Guid acquisitionId, CancellationToken cancellationToken);

    /// <summary>Captures exact current Source ownership and transfer identity. The caller must hold the Entity lifecycle lease and commit before mutating files.</summary>
    Task<AtomicUpgradeCheckpoint?> TryPrepareAsync(Guid acquisitionId, Guid claimJobId,
        AtomicUpgradePreparation preparation, CancellationToken cancellationToken);

    /// <summary>Claims the same immutable preparation for a retry without stealing another running job's claim.</summary>
    Task<bool> TryClaimAsync(Guid acquisitionId, AtomicUpgradeCheckpoint checkpoint, Guid claimJobId, CancellationToken cancellationToken);

    /// <summary>Checks the exact claim and Source binding immediately before executing or recovering its file mutation.</summary>
    Task<bool> IsCurrentAsync(Guid acquisitionId, AtomicUpgradeCheckpoint checkpoint, CancellationToken cancellationToken);

    /// <summary>Rebinds the exclusively owned Source to an approved installed path in the installation transaction, preserving its identifier.</summary>
    Task<bool> TryRebindSourceAsync(Guid acquisitionId, AtomicUpgradeCheckpoint checkpoint, CancellationToken cancellationToken);

    /// <summary>Clears this exact preparation under the lifecycle lease after installation is recorded or an untouched plan is abandoned.</summary>
    Task<bool> TryClearAsync(Guid acquisitionId, AtomicUpgradeCheckpoint checkpoint, CancellationToken cancellationToken);
}
