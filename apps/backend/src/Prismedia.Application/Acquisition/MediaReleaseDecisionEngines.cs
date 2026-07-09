using Prismedia.Domain.Entities;

namespace Prismedia.Application.Acquisition;

/// <summary>
/// Movie decision engine: the generic acceptance gates (protocol, link, seeders, size, terms,
/// language, blocklist) plus a resolution/source-aware ranking parsed from the release title. Runs on
/// the shared rules record — the format/quality gates are book vocabulary and don't apply here; a
/// movie-specific profile (cutoffs, resolution floors) lands with the movie import workstream.
/// </summary>
public sealed class MovieReleaseDecisionEngine : IAcquisitionDecisionEngine {
    public EntityKind Kind => EntityKind.Movie;

    private static readonly IReleaseSpecification[] Specifications = [
        new DangerousContentSpecification(),
        new TitleIdentitySpecification(),
        new MediaYearSpecification(),
        new ProtocolSpecification(),
        new DownloadLinkSpecification(),
        new MinSeedersSpecification(),
        new SizeSpecification(),
        new RequiredTermsSpecification(),
        new IgnoredTermsSpecification(),
        new LanguageSpecification(),
        new MediaQualityAllowedSpecification(EntityKind.Movie),
        new MinFormatScoreSpecification(),
        new MediaUpgradeSpecification(EntityKind.Movie)
    ];

    public IReadOnlyList<ScoredRelease> Evaluate(
        IReadOnlyList<(IndexerRelease Release, Guid? IndexerConfigId, string IndexerName)> releases,
        BookAcquisitionRules rules,
        IReadOnlySet<string>? blocklistedIdentities = null) =>
        MediaReleaseEvaluation.Evaluate(releases, rules, blocklistedIdentities, Specifications, MovieScore);

    /// <summary>Profile preference (terms, custom weights, language) outranks everything; then resolution, then source provenance, then seeders.</summary>
    private static double MovieScore(IndexerRelease release, BookAcquisitionRules rules) =>
        MediaReleaseEvaluation.VideoReleaseScore(release, rules);
}

/// <summary>
/// Music decision engine: the generic acceptance gates plus a codec-quality ranking parsed from the
/// release title (lossless above high-bitrate lossy above the rest). The music policy binds one engine
/// to each supported music kind while sharing this release vocabulary.
/// </summary>
public sealed class MusicReleaseDecisionEngine(EntityKind kind = EntityKind.AudioLibrary) : IAcquisitionDecisionEngine {
    public EntityKind Kind => kind;

    private readonly IReleaseSpecification[] _specifications = [
        new DangerousContentSpecification(),
        // The same leading-title identity gate the video kinds use: music scene naming leads
        // "Artist - Album (Year) [FLAC]" and the music target is "Artist Album", so the strict
        // prefix walk fits. NO year gate on purpose — remaster/reissue years legitimately diverge
        // from the album's release year and would reject wanted releases.
        new TitleIdentitySpecification(),
        new ProtocolSpecification(),
        new DownloadLinkSpecification(),
        new MinSeedersSpecification(),
        new SizeSpecification(),
        new RequiredTermsSpecification(),
        new IgnoredTermsSpecification(),
        new LanguageSpecification(),
        new MediaQualityAllowedSpecification(kind),
        new MinFormatScoreSpecification(),
        new MediaUpgradeSpecification(kind)
    ];

    public IReadOnlyList<ScoredRelease> Evaluate(
        IReadOnlyList<(IndexerRelease Release, Guid? IndexerConfigId, string IndexerName)> releases,
        BookAcquisitionRules rules,
        IReadOnlySet<string>? blocklistedIdentities = null) =>
        MediaReleaseEvaluation.Evaluate(releases, rules, blocklistedIdentities, _specifications, MusicScore);

    /// <summary>Profile preference (terms, custom weights, language) outranks everything; then the codec-quality ladder (hi-res and lossless first), then the revision boost (a proper/repack at the same quality outranks a plain release unless propers are not preferred), then seeders.</summary>
    private static double MusicScore(IndexerRelease release, BookAcquisitionRules rules) {
        var quality = (int)AudioQualityDetection.Detect(release.Title);
        return MediaReleaseEvaluation.PreferenceScore(release, rules) * 10_000
            + ReleaseTitleRelevance.Score(release, rules)
            + quality * 100_000
            + MediaReleaseEvaluation.RevisionBoost(release.Title, rules)
            + Math.Min(release.Seeders ?? 0, 9_999);
    }
}

/// <summary>
/// Rejects releases whose leading title tokens do not name exactly the sought work — the automatic-pick
/// guard against sequels, spin-offs, and different titles sharing a prefix ("Dune Part Two" against
/// "Dune", "Clifford's Puppy Days" against "Clifford the Big Red Dog"). Digit-preserving comparison per
/// <see cref="ReleaseTitleIdentity"/>, so a year inside a title is significant. No-op without a target
/// title (ad-hoc evaluations); manual picks bypass rejection by design.
/// </summary>
public sealed class TitleIdentitySpecification : IReleaseSpecification {
    public ReleaseRejectionReason Reason => ReleaseRejectionReason.TitleMismatch;

    public ReleaseRejectionReason? Evaluate(IndexerRelease release, BookAcquisitionRules rules) =>
        ReleaseTitleIdentity.Match(release.Title, rules.TargetTitle).TitleMatched ? null : Reason;
}

/// <summary>
/// Rejects releases whose title-adjacent year conflicts with the sought work's year — THE guard against
/// same-name remakes and reboots ("Clifford.the.Big.Red.Dog.2019.S01" against the 2000 series, whose
/// 1080p reboot packs would otherwise outrank the 480p original on quality alone). A ±1 tolerance
/// absorbs regional release-date offsets; releases naming no year pass this gate (the title gate and
/// import-time validation carry those). No-op without a known target year.
/// </summary>
public sealed class MediaYearSpecification : IReleaseSpecification {
    private const int ToleranceYears = 1;

    public ReleaseRejectionReason Reason => ReleaseRejectionReason.WrongYear;

    public ReleaseRejectionReason? Evaluate(IndexerRelease release, BookAcquisitionRules rules) {
        if (rules.TargetYear is not { } targetYear) {
            return null;
        }

        var identity = ReleaseTitleIdentity.Match(release.Title, rules.TargetTitle);
        return identity.TitleYear is { } year && Math.Abs(year - targetYear) > ToleranceYears ? Reason : null;
    }
}

/// <summary>
/// Rejects TV releases that name a different unit than the acquisition seeks. An episode search only
/// accepts releases declaring its exact SxxEyy (or 1x05); a season-pack search rejects single-episode
/// releases and other seasons' packs, while accepting complete-series packs (they contain the season)
/// and marker-less titles (judged by the query match alone, mirroring the format rule for books).
/// No-op outside TV searches — the unit fields are set per search by the runner, never by a profile.
/// </summary>
public sealed class TvUnitSpecification : IReleaseSpecification {
    public ReleaseRejectionReason Reason => ReleaseRejectionReason.WrongTvUnit;

    public ReleaseRejectionReason? Evaluate(IndexerRelease release, BookAcquisitionRules rules) {
        if (rules.SeasonNumber is not { } season) {
            return null;
        }

        var declaredEpisodes = TvReleaseTokens.ParseEpisodes(release.Title);
        if (rules.EpisodeNumber is { } episode) {
            // Single episode sought: the release must declare the exact unit — including a multi-episode
            // release that contains it (a double-episode S01E41E42 file fulfils an E42 search).
            return declaredEpisodes is { } unit && unit.Season == season && unit.Episodes.Contains(episode)
                ? null
                : Reason;
        }

        // Season pack sought: an episode-scoped release can never fulfil it.
        if (declaredEpisodes is not null) {
            return Reason;
        }

        if (TvReleaseTokens.NamesCompleteSeries(release.Title)) {
            return null;
        }

        var declaredSeason = TvReleaseTokens.ParseSeason(release.Title);
        return declaredSeason is null || declaredSeason == season ? null : Reason;
    }
}

/// <summary>
/// Rejects releases whose detected ladder quality falls outside the profile's allowed set. An empty
/// allowed set accepts everything (including titles the detector can't place), mirroring the book
/// format rule's permissive default.
/// </summary>
public sealed class MediaQualityAllowedSpecification(EntityKind kind) : IReleaseSpecification {
    public ReleaseRejectionReason Reason => ReleaseRejectionReason.QualityNotAllowed;

    public ReleaseRejectionReason? Evaluate(IndexerRelease release, BookAcquisitionRules rules) {
        if (rules.AllowedQualities.Count == 0) {
            return null;
        }

        var (code, _) = MediaQualityLadder.Detect(kind, release.Title);
        return rules.AllowedQualities.Contains(code, StringComparer.Ordinal) ? null : Reason;
    }
}

/// <summary>
/// Upgrade-search gate for ladder kinds. A candidate is an upgrade when it sits strictly above the owned
/// ladder position, OR — at the same ladder position and same revision — its custom-format score is
/// strictly higher than the owned copy's while the owned score is below the profile's
/// <see cref="BookAcquisitionRules.CutoffFormatScore"/>, OR — only under
/// <see cref="ProperDownloadPolicy.PreferAndUpgrade"/> — it sits at the same ladder position but carries a
/// strictly higher revision than the owned copy (a proper/repack of the same quality). Under
/// <see cref="ProperDownloadPolicy.DoNotUpgrade"/> / <see cref="ProperDownloadPolicy.DoNotPrefer"/> a
/// same-quality higher revision is not an upgrade. Ladder position always dominates: a lower position is
/// never rescued by a revision or a format-score gain. No-op on ordinary first-grab searches.
/// </summary>
public sealed class MediaUpgradeSpecification(EntityKind kind) : IReleaseSpecification {
    public ReleaseRejectionReason Reason => ReleaseRejectionReason.NotAnUpgrade;

    public ReleaseRejectionReason? Evaluate(IndexerRelease release, BookAcquisitionRules rules) {
        if (!rules.IsUpgradeSearch || rules.OwnedMediaQuality is null) {
            return null;
        }

        var owned = MediaQualityLadder.PositionOf(kind, rules.OwnedMediaQuality);
        var (_, candidate) = MediaQualityLadder.Detect(kind, release.Title);
        if (candidate > owned) {
            return null;
        }

        if (candidate == owned) {
            // Same-quality format-score upgrade: a strictly-better-scoring release counts only while a
            // cutoff format score is configured and the owned score is still below it. Independent of the
            // proper policy (custom-format scoring is not the PROPER/REPACK axis).
            if (rules.CutoffFormatScore is { } cutoff
                && rules.OwnedFormatScore < cutoff
                && CustomFormatEvaluation.Score(release.Title, rules, release.Language) > rules.OwnedFormatScore) {
                return null;
            }

            // Same-quality revision upgrade: a strictly-better proper/repack counts only when propers are
            // preferred-and-upgradeable.
            if (rules.ProperPolicy == ProperDownloadPolicy.PreferAndUpgrade
                && ReleaseRevisionDetection.Detect(release.Title) > rules.OwnedMediaRevision) {
                return null;
            }
        }

        return Reason;
    }
}

/// <summary>
/// Rejects releases whose total custom-format score is below the profile's minimum-format-score floor
/// (Sonarr's minimum custom format score gate). Off unless the profile has custom formats configured, so a
/// profile with no formats never rejects on this axis. A negative-scoring release (a format tuned as a soft
/// ban) is rejected when the floor is 0 or above.
/// </summary>
public sealed class MinFormatScoreSpecification : IReleaseSpecification {
    public ReleaseRejectionReason Reason => ReleaseRejectionReason.BelowMinFormatScore;

    public ReleaseRejectionReason? Evaluate(IndexerRelease release, BookAcquisitionRules rules) {
        if (rules.CustomFormats.Count == 0) {
            return null;
        }

        return CustomFormatEvaluation.Score(release.Title, rules, release.Language) >= rules.MinFormatScore ? null : Reason;
    }
}

/// <summary>
/// TV decision engine: the generic acceptance gates plus the unit-match rule, ranked like movies
/// (resolution, then source provenance) with a dominant unit-precision tier on top — a release that
/// NAMES the sought season/episode outranks a complete-series pack, which outranks a marker-less
/// title, regardless of their detected qualities. One engine class serves series, season packs, and
/// single episodes, bound once per kind by the TV policy because the release vocabulary is shared.
/// </summary>
public sealed class TvReleaseDecisionEngine(EntityKind kind) : IAcquisitionDecisionEngine {
    /// <summary>
    /// Unit-precision score tiers. Sized to dominate every quality/revision/seeder difference (quality
    /// tops out around 1.5M) while staying below deliberate heavy user weighted-terms (±10_000 weight →
    /// ±100M), so unit precision decides auto-grabs unless the user explicitly tuned something stronger.
    /// A marker-less title (no season/episode token at all) is the last resort, never the winner while a
    /// unit-named or complete-series candidate exists — the fix for one marker-less DVD rip winning four
    /// different season searches and being queued as the same torrent four times.
    /// </summary>
    private const double ExactUnitBoost = 10_000_000;
    private const double CompleteSeriesBoost = 5_000_000;

    public EntityKind Kind => kind;

    private readonly IReleaseSpecification[] _specifications = [
        new DangerousContentSpecification(),
        new TitleIdentitySpecification(),
        new MediaYearSpecification(),
        new ProtocolSpecification(),
        new DownloadLinkSpecification(),
        new MinSeedersSpecification(),
        new SizeSpecification(),
        new RequiredTermsSpecification(),
        new IgnoredTermsSpecification(),
        new LanguageSpecification(),
        new TvUnitSpecification(),
        new MediaQualityAllowedSpecification(kind),
        new MinFormatScoreSpecification(),
        new MediaUpgradeSpecification(kind)
    ];

    public IReadOnlyList<ScoredRelease> Evaluate(
        IReadOnlyList<(IndexerRelease Release, Guid? IndexerConfigId, string IndexerName)> releases,
        BookAcquisitionRules rules,
        IReadOnlySet<string>? blocklistedIdentities = null) =>
        MediaReleaseEvaluation.Evaluate(
            releases, rules, blocklistedIdentities, _specifications,
            static (release, rules) => MediaReleaseEvaluation.VideoReleaseScore(release, rules) + UnitPrecisionBoost(release, rules));

    /// <summary>The unit-precision tier of one candidate for the sought TV unit; 0 outside TV searches.</summary>
    internal static double UnitPrecisionBoost(IndexerRelease release, BookAcquisitionRules rules) {
        if (rules.SeasonNumber is not { } season) {
            return 0;
        }

        if (rules.EpisodeNumber is { } episode) {
            // Single episode sought: a release declaring the exact unit — alone or within a multi-episode
            // span — earns the boost (other units are rejected by the unit specification; a marker-less
            // single file ranks last).
            return TvReleaseTokens.ParseEpisodes(release.Title) is { } unit
                && unit.Season == season && unit.Episodes.Contains(episode)
                    ? ExactUnitBoost
                    : 0;
        }

        if (TvReleaseTokens.ParseSeason(release.Title) == season) {
            return ExactUnitBoost;
        }

        return TvReleaseTokens.NamesCompleteSeries(release.Title) ? CompleteSeriesBoost : 0;
    }
}

/// <summary>
/// The shared evaluate loop for per-kind engines: run the acceptance specifications, apply the
/// blocklist gate, score with the kind's ranking function, and order accepted-best-first — identical
/// shape to the book engine so every kind's verdicts read the same in the release picker.
/// </summary>
internal static class MediaReleaseEvaluation {
    public static IReadOnlyList<ScoredRelease> Evaluate(
        IReadOnlyList<(IndexerRelease Release, Guid? IndexerConfigId, string IndexerName)> releases,
        BookAcquisitionRules rules,
        IReadOnlySet<string>? blocklistedIdentities,
        IReadOnlyList<IReleaseSpecification> specifications,
        Func<IndexerRelease, BookAcquisitionRules, double> score) {
        var scored = new List<ScoredRelease>(releases.Count);
        foreach (var (release, indexerConfigId, indexerName) in releases) {
            var rejections = new List<ReleaseRejectionReason>();
            foreach (var specification in specifications) {
                if (specification.Evaluate(release, rules) is { } reason) {
                    rejections.Add(reason);
                }
            }

            if (ReleaseIdentity.IsListed(blocklistedIdentities, release.InfoHash, indexerName, release.Title)) {
                rejections.Add(ReleaseRejectionReason.Blocklisted);
            }

            scored.Add(new ScoredRelease(
                release, indexerConfigId, indexerName, rejections.Count == 0, score(release, rules), rejections));
        }

        return scored
            .OrderByDescending(candidate => candidate.Accepted)
            .ThenByDescending(candidate => candidate.Score)
            .ToArray();
    }

    /// <summary>Case-insensitive whole-ish token match against a release title.</summary>
    public static bool TitleHasAny(string title, params string[] tokens) =>
        tokens.Any(token => ReleaseTitleText.ContainsTerm(title, token));

    /// <summary>
    /// The shared video release ranking (movies and TV alike): profile preference outranks everything,
    /// then the position on the combined source × resolution quality ladder, then the revision boost
    /// (a proper/repack outranks a plain release at the same quality, unless propers are not preferred),
    /// then seeders.
    /// </summary>
    public static double VideoReleaseScore(IndexerRelease release, BookAcquisitionRules rules) {
        var quality = (int)VideoQualityDetection.Detect(release.Title);
        return PreferenceScore(release, rules) * 10_000
            + ReleaseTitleRelevance.Score(release, rules)
            + quality * 100_000
            + RevisionBoost(release.Title, rules)
            + Math.Min(release.Seeders ?? 0, 9_999);
    }

    /// <summary>
    /// The revision component of a release's score, in raw score points. When the policy is not
    /// <see cref="ProperDownloadPolicy.DoNotPrefer"/>, each revision step above a plain release is worth
    /// 10_000 — below one ladder step (100_000, so it can never outrank a genuinely higher quality) and
    /// above the seeders tie-break (max 9_999, so a proper always beats a plain release of the same
    /// quality regardless of seed counts). Under <c>DoNotPrefer</c> revisions carry no weight at all.
    /// </summary>
    public static double RevisionBoost(string title, BookAcquisitionRules rules) {
        if (rules.ProperPolicy == ProperDownloadPolicy.DoNotPrefer) {
            return 0;
        }

        return (ReleaseRevisionDetection.Detect(title) - 1) * 10_000;
    }

    /// <summary>How many of the profile's preferred terms the title matches.</summary>
    public static int PreferredTermMatches(string title, BookAcquisitionRules rules) =>
        rules.PreferredTerms.Count(term => ReleaseTitleText.ContainsTerm(title, term));

    /// <summary>
    /// The profile-preference component of a release's score, in shared preference points: each preferred
    /// term is worth 100, each matched custom weighted term contributes its own weight (so 100 equals one
    /// preferred term, negatives push down), the release's language earns up to 50 per step of the ordered
    /// preferred-language list (an unmarked or multi release counts as the top preference), and every
    /// matching custom format adds its score directly (so 100 custom-format points equal one preferred term).
    /// Every engine multiplies this by its own boost so preference always compares the same way across kinds.
    /// </summary>
    public static double PreferenceScore(IndexerRelease release, BookAcquisitionRules rules) {
        var title = release.Title;
        double score = PreferredTermMatches(title, rules) * 100;
        foreach (var term in rules.WeightedTerms) {
            if (ReleaseTitleText.ContainsTerm(title, term.Term)) {
                score += term.Weight;
            }
        }

        return score
            + LanguagePreferenceBonus(release, rules)
            + CustomFormatEvaluation.Score(title, rules, release.Language);
    }

    /// <summary>
    /// Ranks a release by where its declared language sits in the ordered preferred-language list:
    /// the first preference earns <c>50 × list length</c>, each later preference 50 less. Unmarked and
    /// multi releases count as the first preference (the acceptance gate has already filtered
    /// declared-but-unpreferred languages).
    /// </summary>
    private static double LanguagePreferenceBonus(IndexerRelease release, BookAcquisitionRules rules) {
        var preferred = rules.PreferredLanguages;
        if (preferred.Count == 0) {
            return 0;
        }

        var declared = ReleaseLanguageDetection.Detect(release.Title, release.Language);
        if (declared.Count == 0 || declared.Contains(ReleaseLanguageDetection.Multi)) {
            return preferred.Count * 50;
        }

        for (var i = 0; i < preferred.Count; i++) {
            if (declared.Contains(ReleaseLanguageDetection.Canonicalize(preferred[i]))) {
                return (preferred.Count - i) * 50;
            }
        }

        // Declares only unpreferred languages — no bonus (the acceptance gate rejects it anyway).
        return 0;
    }
}
