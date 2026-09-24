namespace Prismedia.Domain.Entities;

/// <summary>How a returned reading or listening position was derived.</summary>
public enum AlignmentBasis {
    /// <summary>The recorded position itself, or no destination was derived (see the gap).</summary>
    [Code("exact")]
    Exact,

    /// <summary>The start of the aligned chapter, because the position within it is unknown or zero.</summary>
    [Code("chapter_start")]
    ChapterStart,

    /// <summary>The same relative position inside the aligned chapter window.</summary>
    [Code("interpolated")]
    Interpolated,

    /// <summary>The first paired chapter, because no position has been recorded to resume.</summary>
    [Code("fresh_start")]
    FreshStart
}
