using Prismedia.Domain.Entities;

namespace Prismedia.Contracts.Entities;

/// <summary>The current user's exact last position in one consumption modality of a work.</summary>
/// <param name="Modality">Modality whose position this is.</param>
/// <param name="PositionEntityId">Work or chapter for reading; physical audio track for listening.</param>
/// <param name="Unit">Unit counted by <paramref name="Index"/>.</param>
/// <param name="Index">Zero-based position within <paramref name="Total"/>.</param>
/// <param name="Total">Unit total addressed by the index.</param>
/// <param name="OffsetSeconds">Exact track offset, for listening.</param>
/// <param name="MarkerId">Embedded chapter marker holding the offset, for listening.</param>
/// <param name="Mode">Reader layout, for reading.</param>
/// <param name="Location">Opaque reader locator (CFI or Readium locator), for reading.</param>
/// <param name="UpdatedAt">When the position was recorded.</param>
/// <param name="WorkIndex">Absolute position across the work, when a chapter-local reading position resolves to one.</param>
/// <param name="WorkTotal">Absolute unit total across the work, alongside <paramref name="WorkIndex"/>.</param>
public sealed record ModalityProgress(
    ConsumptionModality Modality,
    Guid PositionEntityId,
    ProgressUnit Unit,
    int Index,
    int Total,
    double? OffsetSeconds,
    Guid? MarkerId,
    ReaderMode? Mode,
    string? Location,
    DateTimeOffset UpdatedAt,
    int? WorkIndex = null,
    int? WorkTotal = null);
