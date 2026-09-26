using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Persistence.Entities;

/// <summary>
/// One user's exact last position in one consumption modality of one work. Rows belong to the
/// user's <see cref="UserEntityStateRow"/> and are replaced by the progress capability mapper;
/// deleting the position Entity (a chapter or audio track) removes the checkpoint with it.
/// </summary>
public sealed class UserProgressCheckpointRow {
    #region Variables

    /// <summary>User owning the position.</summary>
    public Guid UserId { get; set; }

    /// <summary>Work whose progress the checkpoint belongs to.</summary>
    public Guid EntityId { get; set; }

    /// <summary>Modality whose position this is; one row per modality.</summary>
    public ConsumptionModality Modality { get; set; }

    /// <summary>Entity addressed by the position (work, readable chapter, or audio track).</summary>
    public Guid PositionEntityId { get; set; }

    /// <summary>Unit counted by <see cref="Index"/>.</summary>
    public ProgressUnit Unit { get; set; }

    /// <summary>Zero-based position within <see cref="Total"/>.</summary>
    public int Index { get; set; }

    /// <summary>Unit total addressed by the index.</summary>
    public int Total { get; set; }

    /// <summary>Exact time offset for offset-addressed modalities.</summary>
    public double? OffsetSeconds { get; set; }

    /// <summary>Embedded chapter marker that identified an offset position; cleared if the marker goes away.</summary>
    public Guid? MarkerId { get; set; }

    /// <summary>Reader layout for reader modalities.</summary>
    public ReaderMode? Mode { get; set; }

    /// <summary>Opaque format locator for reader modalities.</summary>
    public string? Location { get; set; }

    /// <summary>Server time of the accepted signal.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    #endregion
}
