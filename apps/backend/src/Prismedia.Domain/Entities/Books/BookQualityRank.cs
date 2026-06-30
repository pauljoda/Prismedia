namespace Prismedia.Domain.Entities;

/// <summary>
/// The comparable quality of an owned or candidate book payload: a (<see cref="BookSourceTier"/>,
/// <see cref="BookFormatTier"/>) pair. Pure value object — the acquisition profile's cutoff, the engine's
/// acceptance gate, and the upgrade decision are all expressed in terms of this rank.
/// <para>
/// Acceptance for an upgrade is decided by <see cref="StrictlyDominates"/> — per-axis Pareto strict
/// dominance — NOT by the scalar <see cref="Value"/>. Folding the two axes into one number would let a
/// gain on one axis mask a loss on the other (a retail PDF would "outrank" a web EPUB and could replace it,
/// a format downgrade). <see cref="Value"/> exists only to order releases that have <em>already</em> been
/// accepted (a stable, source-major tie-break), never to decide whether one quality beats another.
/// </para>
/// </summary>
/// <param name="Source">Provenance/edition tier, parsed from the release title at grab time.</param>
/// <param name="Format">Format tier, derived from the imported file's concrete <see cref="BookFormat"/>.</param>
public readonly record struct BookQualityRank(BookSourceTier Source, BookFormatTier Format) {
    /// <summary>
    /// Monotonic composite (source major ×100, format minor) used ONLY as a stable ordering key among
    /// already-accepted candidates. Never use it as an acceptance or upgrade test — see
    /// <see cref="StrictlyDominates"/>.
    /// </summary>
    public int Value => ((int)Source * 100) + (int)Format;

    /// <summary>The rank when nothing is known: the lowest possible on both axes.</summary>
    public static BookQualityRank Floor { get; } = new(BookSourceTier.Unknown, BookFormatTier.Unknown);

    /// <summary>
    /// Maps a concrete <see cref="BookFormat"/> to its desirability tier. Unrecognized formats map to
    /// <see cref="BookFormatTier.Unknown"/> so an unimportable payload never claims a real tier.
    /// </summary>
    public static BookFormatTier TierFor(BookFormat format) => format switch {
        BookFormat.Epub => BookFormatTier.Reflowable,
        BookFormat.ImageArchive => BookFormatTier.Archive,
        BookFormat.Pdf => BookFormatTier.Fixed,
        _ => BookFormatTier.Unknown
    };

    /// <summary>
    /// True when this rank is a genuine upgrade over <paramref name="owned"/>: at least as good on BOTH axes
    /// and strictly better on at least one. This is the locked acceptance predicate for an upgrade — a
    /// candidate that improves one axis while regressing the other (e.g. better source but worse format) is
    /// rejected, so an auto-replacement can never downgrade what the user already has.
    /// </summary>
    /// <param name="owned">The quality currently owned, to improve upon.</param>
    public bool StrictlyDominates(BookQualityRank owned) =>
        Source >= owned.Source && Format >= owned.Format && (Source > owned.Source || Format > owned.Format);
}
