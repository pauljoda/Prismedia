using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Capabilities;

/// <summary>
/// The exact last position of one consumption modality, kept independently of the work's shared
/// progress cursor. Instances are created only through
/// <see cref="ConsumptionModalityDefinition.Checkpoint"/>, which validates them against the
/// modality's rules.
/// </summary>
public sealed record ProgressCheckpoint {
    #region Variables

    /// <summary>Modality whose position this is.</summary>
    public ConsumptionModality Modality { get; }

    /// <summary>
    /// Entity addressed by the position: the work or a readable chapter for reading, the physical
    /// audio track for listening.
    /// </summary>
    public Guid PositionEntityId { get; }

    /// <summary>Unit counted by <see cref="Index"/> and <see cref="Total"/>.</summary>
    public ProgressUnit Unit { get; }

    /// <summary>Zero-based position within <see cref="Total"/>.</summary>
    public int Index { get; }

    /// <summary>Unit total addressed by <see cref="Index"/>.</summary>
    public int Total { get; }

    /// <summary>Exact time offset inside the position Entity, for offset-addressed modalities.</summary>
    public double? OffsetSeconds { get; }

    /// <summary>Embedded chapter marker that identified the position, for offset-addressed modalities.</summary>
    public Guid? MarkerId { get; }

    /// <summary>Reader layout active when the position was recorded, for reader modalities.</summary>
    public ReaderMode? Mode { get; }

    /// <summary>Opaque format locator (EPUB CFI or Readium locator), for reader modalities.</summary>
    public string? Location { get; }

    /// <summary>Server time at which the signal that produced this position was accepted.</summary>
    public DateTimeOffset UpdatedAt { get; }

    /// <summary>Behavior of <see cref="Modality"/>.</summary>
    public ConsumptionModalityDefinition Definition => ConsumptionModalityDefinition.For(Modality);

    #endregion

    #region Constructors

    internal ProgressCheckpoint(
        ConsumptionModality modality,
        Guid positionEntityId,
        ProgressUnit unit,
        int index,
        int total,
        double? offsetSeconds,
        Guid? markerId,
        ReaderMode? mode,
        string? location,
        DateTimeOffset updatedAt) {
        Modality = modality;
        PositionEntityId = positionEntityId;
        Unit = unit;
        Index = index;
        Total = total;
        OffsetSeconds = offsetSeconds;
        MarkerId = markerId;
        Mode = mode;
        Location = location;
        UpdatedAt = updatedAt;
    }

    #endregion
}
