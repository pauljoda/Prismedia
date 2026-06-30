namespace Prismedia.Domain.Entities;

/// <summary>
/// Ranked desirability of a book payload's <em>format</em> for upgrade comparison. Higher members are
/// strictly better, and the backing integers are the rank (distinct on purpose, so the codec round-trips and
/// comparisons are unambiguous). This is distinct from <see cref="BookFormat"/>, which is a reader-mechanic
/// discriminator: a tier is <em>derived</em> from a <see cref="BookFormat"/> via
/// <see cref="BookQualityRank.TierFor"/>, never overloaded onto it. The set covers only formats Prismedia can
/// actually import and scan, so a tier is never assigned to a payload that cannot be imported.
/// </summary>
public enum BookFormatTier {
    /// <summary>Unknown or undetected format — the floor; any recognized format beats it.</summary>
    [Code("unknown")]
    Unknown = 0,

    /// <summary>Fixed-layout payload (PDF). Readable but not reflowable.</summary>
    [Code("fixed")]
    Fixed = 10,

    /// <summary>Reflowable device-native ebook (EPUB). Preferred for ebooks.</summary>
    [Code("reflowable")]
    Reflowable = 20,

    /// <summary>Lossless image-archive comic payload (CBZ/ZIP). Top tier for comics.</summary>
    [Code("archive")]
    Archive = 30
}
