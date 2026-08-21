namespace Prismedia.Domain.Entities;

/// <summary>Canonical progression direction for an ordered image-page sequence.</summary>
public enum PageReadingDirection {
    /// <summary>Advance horizontally from left to right.</summary>
    [Code("left-to-right")]
    LeftToRight,

    /// <summary>Advance horizontally from right to left, as in most manga.</summary>
    [Code("right-to-left")]
    RightToLeft,

    /// <summary>Advance vertically from top to bottom, as in long-strip comics.</summary>
    [Code("top-to-bottom")]
    TopToBottom
}
