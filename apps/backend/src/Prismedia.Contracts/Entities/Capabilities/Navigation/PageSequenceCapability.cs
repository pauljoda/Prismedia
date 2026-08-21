using Prismedia.Domain.Entities;

namespace Prismedia.Contracts.Entities;

/// <summary>Summary of an Entity-owned ordered image-page manifest.</summary>
/// <param name="PageCount">Number of addressable pages.</param>
/// <param name="Direction">Canonical page progression direction.</param>
/// <param name="DefaultMode">Preferred initial layout when the user has no saved choice.</param>
/// <param name="CoverOrdinal">Cover page ordinal when known.</param>
[CapabilityKind("page-sequence")]
public sealed record PageSequenceCapability(
    int PageCount,
    PageReadingDirection Direction,
    ReaderMode DefaultMode,
    int? CoverOrdinal) : EntityCapability;
