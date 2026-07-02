namespace Prismedia.Application.Acquisition;

/// <summary>
/// Computes the ranking score for an accepted book release. Extracted from the decision engine so the same
/// definition is reused everywhere a release is ranked (initial grab and, later, upgrade searches). The score
/// orders releases that have <em>already</em> passed the acceptance specifications; it never decides accept
/// vs. reject. Deterministic and independent of wall-clock time.
/// </summary>
public static class BookReleaseScore {
    /// <summary>
    /// The detected quality rank's composite value is multiplied by this — large enough that a higher quality
    /// always outranks any number of preferred-term matches, so among accepted releases the best quality wins
    /// and preferred terms only break ties within the same quality (Sonarr-style "best quality, then prefer").
    /// </summary>
    public const double QualityRankBoost = 100_000;

    /// <summary>
    /// Preference points (see <see cref="MediaReleaseEvaluation.PreferenceScore"/> — a preferred term is
    /// 100 points, a custom weighted term its own weight, language up to 50 per preference step) are
    /// multiplied by this, so one preferred term (1000) outranks any seeder difference — preference
    /// behaves like a sub-tier while seeders order within it.
    /// </summary>
    public const double PreferenceBoost = 10;

    /// <summary>
    /// Composite ranking score: detected quality dominates, then profile preference (preferred terms,
    /// custom weighted terms, preferred languages), then seeders (log-scaled so a 1000-seed release does
    /// not bury a healthy 50-seed one), with peers as a small tiebreak. Quality is detected from the
    /// title, so an untagged release scores at the quality floor and is ordered purely by
    /// preference/seeders — never rejected here (acceptance is the engine's job).
    /// </summary>
    public static double Of(IndexerRelease release, BookAcquisitionRules rules) {
        var quality = BookFormatDetection.DetectQuality(release.Title).Value;
        var seeders = Math.Max(release.Seeders ?? 0, 0);
        var peers = Math.Max(release.Peers ?? 0, 0);
        return (quality * QualityRankBoost)
            + (MediaReleaseEvaluation.PreferenceScore(release, rules) * PreferenceBoost)
            + (Math.Log10(seeders + 1) * 100)
            + (Math.Min(peers, 100) * 0.25);
    }
}
