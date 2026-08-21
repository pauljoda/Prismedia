using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Capabilities;

/// <summary>Hydrated summary proving that an Entity owns a valid ordered image-page manifest.</summary>
public sealed class CapabilityPageSequence : EntityCapability {
    /// <summary>Creates a validated manifest summary for capability projection.</summary>
    public CapabilityPageSequence(
        int pageCount,
        PageReadingDirection direction,
        ReaderMode defaultMode,
        int? coverOrdinal) {
        if (pageCount <= 0) {
            throw new ArgumentOutOfRangeException(nameof(pageCount));
        }
        if (!Enum.IsDefined(direction)) {
            throw new ArgumentOutOfRangeException(nameof(direction));
        }
        if (defaultMode is not (ReaderMode.Paged or ReaderMode.Webtoon)) {
            throw new ArgumentException("A page sequence must use paged or webtoon mode.", nameof(defaultMode));
        }
        if (coverOrdinal is < 0 || coverOrdinal >= pageCount) {
            throw new ArgumentOutOfRangeException(nameof(coverOrdinal));
        }

        PageCount = pageCount;
        Direction = direction;
        DefaultMode = defaultMode;
        CoverOrdinal = coverOrdinal;
    }

    /// <summary>Number of addressable page resources.</summary>
    public int PageCount { get; }

    /// <summary>Canonical page progression direction.</summary>
    public PageReadingDirection Direction { get; }

    /// <summary>Preferred initial reader layout.</summary>
    public ReaderMode DefaultMode { get; }

    /// <summary>Cover page ordinal when known.</summary>
    public int? CoverOrdinal { get; }
}
