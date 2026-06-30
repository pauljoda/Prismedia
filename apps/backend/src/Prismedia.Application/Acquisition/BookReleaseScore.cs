namespace Prismedia.Application.Acquisition;

/// <summary>
/// Computes the ranking score for an accepted book release. Extracted from the decision engine so the same
/// definition is reused everywhere a release is ranked (initial grab and, later, upgrade searches). The score
/// orders releases that have <em>already</em> passed the acceptance specifications; it never decides accept
/// vs. reject. Deterministic and independent of wall-clock time.
/// </summary>
public static class BookReleaseScore {
    /// <summary>
    /// Each preferred term found in a release title adds this much — large enough that any preferred match
    /// outranks any seeder difference, so preference behaves like a tier while seeders order within a tier.
    /// </summary>
    public const double PreferredTermBoost = 1000;

    /// <summary>
    /// Composite ranking score: preferred-term matches dominate, then seeders (log-scaled so a 1000-seed
    /// release does not bury a healthy 50-seed one), with peers as a small tiebreak.
    /// </summary>
    public static double Of(IndexerRelease release, BookAcquisitionRules rules) {
        var seeders = Math.Max(release.Seeders ?? 0, 0);
        var peers = Math.Max(release.Peers ?? 0, 0);
        var preferred = rules.PreferredTerms.Count(term => release.Title.Contains(term, StringComparison.OrdinalIgnoreCase));
        return (preferred * PreferredTermBoost) + (Math.Log10(seeders + 1) * 100) + (Math.Min(peers, 100) * 0.25);
    }
}
