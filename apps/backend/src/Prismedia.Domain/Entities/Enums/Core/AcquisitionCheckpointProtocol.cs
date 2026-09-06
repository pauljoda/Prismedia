namespace Prismedia.Domain.Entities;

/// <summary>
/// Durable checkpoint shape used to resume or safely abandon an interrupted import or replacement.
/// Profile definitions select normal placement shapes; atomic replacements elect preparation separately.
/// </summary>
public enum AcquisitionCheckpointProtocol {
    /// <summary>Kind-neutral exact file-placement plan used by books, movies, and music.</summary>
    [Code("placement")]
    Placement,

    /// <summary>Episode-aware plan used by television imports.</summary>
    [Code("television")]
    Television,

    /// <summary>Preparation for replacing an owned file, committed before any filesystem mutation.</summary>
    [Code("atomic-upgrade")]
    AtomicUpgrade
}
