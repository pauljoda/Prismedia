using System.Text.Json.Serialization;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Acquisition;

/// <summary>Physical file facts captured before an atomic replacement is allowed to start.</summary>
public sealed record AtomicUpgradeFileSnapshot(long Length, DateTime LastWriteTimeUtc);

/// <summary>The exact owned and incoming files selected before any replacement staging or backup begins.</summary>
public sealed record AtomicUpgradeFilePlan(
    string OwnedPath,
    string IncomingPath,
    AtomicUpgradeFileSnapshot Owned,
    AtomicUpgradeFileSnapshot Incoming,
    BookFormatTier IncomingFormat,
    bool AllowFormatChange = false) {
    /// <summary>The original basename with the approved incoming extension, otherwise the original path.</summary>
    [JsonIgnore]
    public string InstallPath => AllowFormatChange ? Path.ChangeExtension(OwnedPath, Path.GetExtension(IncomingPath)) : OwnedPath;
}

/// <summary>
/// Durable preparation for one owned-file replacement. The preparation commit precedes filesystem
/// mutation; attempt-specific incoming evidence distinguishes an installed upgrade from its old target.
/// </summary>
public sealed record AtomicUpgradeCheckpoint(
    Guid AttemptId,
    Guid ClaimJobId,
    Guid ParentAcquisitionId,
    Guid ParentEntityId,
    Guid SourceFileId,
    EntityKind Kind,
    AtomicUpgradeFilePlan Files,
    string TransferContentPath,
    string? TransferClientItemId,
    SelectedRelease SelectedRelease) {
    /// <summary>Explicit discriminator separates replacement preparation from normal family placement checkpoints.</summary>
    public AcquisitionCheckpointProtocol Protocol { get; init; } = AcquisitionCheckpointProtocol.AtomicUpgrade;

    /// <summary>Retained original bytes belonging only to this replacement attempt.</summary>
    [JsonIgnore]
    public string BackupPath => OwnedFileReplacementArtifacts.CheckpointBackupPath(Files.OwnedPath, AttemptId);

    /// <summary>Retained incoming bytes used to prove that a swap completed before a database failure.</summary>
    [JsonIgnore]
    public string EvidencePath => OwnedFileReplacementArtifacts.CheckpointEvidencePath(Files.OwnedPath, AttemptId);
}
