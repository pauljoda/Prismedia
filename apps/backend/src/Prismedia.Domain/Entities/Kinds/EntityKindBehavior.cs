namespace Prismedia.Domain.Entities;

/// <summary>
/// Complete opt-in behavior contract for one Entity kind. Every definition supplies exactly one
/// instance, keeping cross-cutting kind policy together while preserving focused immutable policy
/// objects for acquisition, identification, processing, and engagement.
/// </summary>
public sealed record EntityKindBehavior {
    /// <summary>Explicit behavior contract for a kind with no opt-in behavior.</summary>
    public static EntityKindBehavior None { get; } = new();

    /// <summary>Creates one validated Entity-kind behavior contract.</summary>
    public EntityKindBehavior(
        EntityIdentificationPolicy? identification = null,
        EntityManualAcquisitionPolicy? manualAcquisition = null,
        EntityProcessingPolicy? processing = null,
        EntityEngagementPolicy? engagement = null,
        EntityBrowsePolicy? browse = null,
        EntityLibraryVisibilityPolicy? libraryVisibility = null,
        bool supportsFileDeletion = false,
        bool supportsManualManagement = false,
        bool prunesWhenEmpty = false,
        EntityMediaQualityFamily mediaQualityFamily = EntityMediaQualityFamily.None,
        EntityUpgradeMode upgradeMode = EntityUpgradeMode.Import) {
        if (upgradeMode == EntityUpgradeMode.AtomicMediaFile &&
            mediaQualityFamily == EntityMediaQualityFamily.None) {
            throw new ArgumentException(
                "Atomic media upgrades require a media quality family.",
                nameof(upgradeMode));
        }

        Identification = identification ?? EntityIdentificationPolicy.None;
        ManualAcquisition = manualAcquisition ?? EntityManualAcquisitionPolicy.None;
        Processing = processing ?? EntityProcessingPolicy.None;
        Engagement = engagement ?? EntityEngagementPolicy.None;
        Browse = browse ?? EntityBrowsePolicy.Default;
        LibraryVisibility = libraryVisibility ?? EntityLibraryVisibilityPolicy.Unscoped;
        SupportsFileDeletion = supportsFileDeletion;
        SupportsManualManagement = supportsManualManagement;
        PrunesWhenEmpty = prunesWhenEmpty;
        MediaQualityFamily = mediaQualityFamily;
        UpgradeMode = upgradeMode;
    }

    /// <summary>Identification and provider-compatibility behavior.</summary>
    public EntityIdentificationPolicy Identification { get; }

    /// <summary>Browser upload and reviewed-replacement behavior.</summary>
    public EntityManualAcquisitionPolicy ManualAcquisition { get; }

    /// <summary>Derived-media processing behavior.</summary>
    public EntityProcessingPolicy Processing { get; }

    /// <summary>Completion/filter behavior.</summary>
    public EntityEngagementPolicy Engagement { get; }

    /// <summary>List hierarchy and aggregate visibility behavior.</summary>
    public EntityBrowsePolicy Browse { get; }

    /// <summary>Library-root visibility topology.</summary>
    public EntityLibraryVisibilityPolicy LibraryVisibility { get; }

    /// <summary>Whether this kind can safely root managed file deletion.</summary>
    public bool SupportsFileDeletion { get; }

    /// <summary>Whether users may create and delete this kind directly.</summary>
    public bool SupportsManualManagement { get; }

    /// <summary>
    /// Whether an unrequested, unmonitored instance is a derived structural shell that should be
    /// removed after its last child disappears.
    /// </summary>
    public bool PrunesWhenEmpty { get; }

    /// <summary>Quality ladder used to rank acquisition releases.</summary>
    public EntityMediaQualityFamily MediaQualityFamily { get; }

    /// <summary>How a completed upgrade is applied for this kind.</summary>
    public EntityUpgradeMode UpgradeMode { get; }

    /// <summary>Whether one owned media file uses the media-quality atomic replacement path.</summary>
    public bool SupportsAtomicMediaUpgrade => UpgradeMode == EntityUpgradeMode.AtomicMediaFile;
}
