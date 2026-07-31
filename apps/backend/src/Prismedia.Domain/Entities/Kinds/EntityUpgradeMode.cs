namespace Prismedia.Domain.Entities;

/// <summary>
/// Declares how a completed upgrade is applied for an Entity kind. The owning definition makes the
/// routing choice so acquisition completion and destructive replacement cannot drift into parallel
/// kind switches.
/// </summary>
public enum EntityUpgradeMode {
    /// <summary>The kind's normal family import engine merges or places the completed payload.</summary>
    Import,

    /// <summary>A single book payload is replaced with book source/format dominance checks.</summary>
    AtomicBookFile,

    /// <summary>A single playable media payload is replaced with its media-quality ladder checks.</summary>
    AtomicMediaFile
}
