namespace Prismedia.Domain.Entities;

/// <summary>
/// How Prismedia treats a release's revision (PROPER / REPACK / RERIP / anime v2+) when ranking and
/// upgrading. The tri-state mirrors Sonarr's <c>ProperDownloadTypes</c> "Download Propers and Repacks"
/// setting: one knob governs both the ranking boost a revision earns and whether a same-quality higher
/// revision can trigger an upgrade of an owned copy.
/// </summary>
public enum ProperDownloadPolicy {
    /// <summary>
    /// The default, actively-preferring state (Sonarr <c>PreferAndUpgrade</c>): revisions boost a
    /// candidate's ranking, AND a same-quality release with a higher revision than the owned copy
    /// counts as an upgrade. This is the only policy under which a proper/repack alone (no quality
    /// change) will replace an owned file.
    /// </summary>
    [Code("prefer-and-upgrade")]
    PreferAndUpgrade,

    /// <summary>
    /// Revisions still boost ranking (a proper outranks a plain release at equal quality, so it wins
    /// a first grab) but never trigger an upgrade of an already-owned copy (Sonarr <c>DoNotUpgrade</c>):
    /// Prismedia will not swap an owned file purely to acquire a better revision at the same quality.
    /// </summary>
    [Code("do-not-upgrade")]
    DoNotUpgrade,

    /// <summary>
    /// Revisions are ignored entirely (Sonarr <c>DoNotPrefer</c>): they carry no ranking weight and are
    /// never an upgrade reason. A same-quality higher revision is treated as neither better nor worse
    /// than a plain release; only the quality ladder decides.
    /// </summary>
    [Code("do-not-prefer")]
    DoNotPrefer
}
