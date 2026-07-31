namespace Prismedia.Domain.Entities;

/// <summary>
/// Durable checkpoint shape an acquisition profile uses to resume or safely abandon an interrupted import.
/// The profile definition selects this independently from its release naming/rendering behavior.
/// </summary>
public enum AcquisitionCheckpointProtocol {
    /// <summary>Kind-neutral exact file-placement plan used by books, movies, and music.</summary>
    [Code("placement")]
    Placement,

    /// <summary>Episode-aware plan used by television imports.</summary>
    [Code("television")]
    Television
}
