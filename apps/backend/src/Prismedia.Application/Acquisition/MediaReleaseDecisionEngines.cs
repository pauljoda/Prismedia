using Prismedia.Domain.Entities;

namespace Prismedia.Application.Acquisition;

/// <summary>
/// Torznab category routing per media kind. Indexer configs carry a global category list; a search
/// narrows it to the acquisition kind's category range (falling back to the range's top-level category
/// when the config declares none in range), so a movie search never queries book categories just
/// because the indexer was configured for books first.
/// </summary>
public static class TorznabCategories {
    // Torznab top-level numeric category ranges. prism-vocab: external (Torznab category standard).
    private const int Movies = 2000;
    private const int Audio = 3000;
    private const int Tv = 5000;
    private const int Books = 7000;
    private const int Other = 8000;
    private const int RangeSize = 1000;

    /// <summary>The top of the Torznab category range for a media kind, or null for kinds with no mapping.</summary>
    private static int? RangeStartFor(EntityKind kind) => kind switch {
        EntityKind.Book => Books,
        EntityKind.Movie => Movies,
        EntityKind.AudioLibrary or EntityKind.AudioTrack or EntityKind.MusicArtist => Audio,
        EntityKind.VideoSeries or EntityKind.VideoSeason or EntityKind.Video => Tv,
        _ => null
    };

    /// <summary>
    /// The effective search categories for a kind: the configured categories that fall inside the
    /// kind's range (preserving the user's narrower picks, e.g. 7030 comics), else the range's
    /// top-level category. Configured categories in the kind-neutral Other range (8000s) always
    /// pass through — indexers file e-books and misc payloads there. Kinds with no mapping keep
    /// the configured list unchanged.
    /// </summary>
    public static IReadOnlyList<int> ForKind(EntityKind kind, IReadOnlyList<int> configured) {
        if (RangeStartFor(kind) is not { } start) {
            return configured;
        }

        var kindPicks = configured.Where(category => category >= start && category < start + RangeSize).ToArray();
        var otherPicks = configured.Where(category => category >= Other && category < Other + RangeSize);
        return (kindPicks.Length > 0 ? kindPicks : [start]).Concat(otherPicks).ToArray();
    }
}

/// <summary>
/// Movie decision engine: the generic acceptance gates (protocol, link, seeders, size, terms,
/// language, blocklist) plus a resolution/source-aware ranking parsed from the release title. Runs on
/// the shared rules record — the format/quality gates are book vocabulary and don't apply here; a
/// movie-specific profile (cutoffs, resolution floors) lands with the movie import workstream.
/// </summary>
public sealed class MovieReleaseDecisionEngine : IAcquisitionDecisionEngine {
    public EntityKind Kind => EntityKind.Movie;

    private static readonly IReleaseSpecification[] Specifications = [
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
/// release title (lossless above high-bitrate lossy above the rest). Album-level; per-kind music
/// profiles (bitrate floors, format cutoffs) land with the music import workstream.
/// </summary>
public sealed class MusicReleaseDecisionEngine : IAcquisitionDecisionEngine {
    public EntityKind Kind => EntityKind.AudioLibrary;

    private static readonly IReleaseSpecification[] Specifications = [
        new ProtocolSpecification(),
        new DownloadLinkSpecification(),
        new MinSeedersSpecification(),
        new SizeSpecification(),
        new RequiredTermsSpecification(),
        new IgnoredTermsSpecification(),
        new LanguageSpecification(),
        new MediaQualityAllowedSpecification(EntityKind.AudioLibrary),
        new MinFormatScoreSpecification(),
        new MediaUpgradeSpecification(EntityKind.AudioLibrary)
    ];

    public IReadOnlyList<ScoredRelease> Evaluate(
        IReadOnlyList<(IndexerRelease Release, Guid? IndexerConfigId, string IndexerName)> releases,
        BookAcquisitionRules rules,
        IReadOnlySet<string>? blocklistedIdentities = null) =>
        MediaReleaseEvaluation.Evaluate(releases, rules, blocklistedIdentities, Specifications, MusicScore);

    /// <summary>Profile preference (terms, custom weights, language) outranks everything; then the codec-quality ladder (hi-res and lossless first), then the revision boost (a proper/repack at the same quality outranks a plain release unless propers are not preferred), then seeders.</summary>
    private static double MusicScore(IndexerRelease release, BookAcquisitionRules rules) {
        var quality = (int)AudioQualityDetection.Detect(release.Title);
        return MediaReleaseEvaluation.PreferenceScore(release, rules) * 10_000
            + quality * 100_000
            + MediaReleaseEvaluation.RevisionBoost(release.Title, rules)
            + Math.Min(release.Seeders ?? 0, 9_999);
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

        var declaredEpisode = TvReleaseTokens.ParseEpisode(release.Title);
        if (rules.EpisodeNumber is { } episode) {
            // Single episode sought: only the exact unit qualifies.
            return declaredEpisode == (season, episode) ? null : Reason;
        }

        // Season pack sought: a single-episode release can never fulfil it.
        if (declaredEpisode is not null) {
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
/// (resolution, then source provenance). One engine class serves both TV acquisition units — season
/// packs (<see cref="EntityKind.VideoSeason"/>) and single episodes (<see cref="EntityKind.Video"/>) —
/// registered once per kind, since the vocabulary of a TV release is the same at either granularity.
/// </summary>
public sealed class TvReleaseDecisionEngine(EntityKind kind) : IAcquisitionDecisionEngine {
    public EntityKind Kind => kind;

    private readonly IReleaseSpecification[] _specifications = [
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
        MediaReleaseEvaluation.Evaluate(releases, rules, blocklistedIdentities, _specifications, MediaReleaseEvaluation.VideoReleaseScore);
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

            if (blocklistedIdentities is { Count: > 0 }
                && blocklistedIdentities.Contains(ReleaseIdentity.For(release.InfoHash, indexerName, release.Title))) {
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
        tokens.Any(token => title.Contains(token, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The shared video release ranking (movies and TV alike): profile preference outranks everything,
    /// then the position on the combined source × resolution quality ladder, then the revision boost
    /// (a proper/repack outranks a plain release at the same quality, unless propers are not preferred),
    /// then seeders.
    /// </summary>
    public static double VideoReleaseScore(IndexerRelease release, BookAcquisitionRules rules) {
        var quality = (int)VideoQualityDetection.Detect(release.Title);
        return PreferenceScore(release, rules) * 10_000
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
        rules.PreferredTerms.Count(term => !string.IsNullOrWhiteSpace(term) && title.Contains(term, StringComparison.OrdinalIgnoreCase));

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
            if (!string.IsNullOrWhiteSpace(term.Term) && title.Contains(term.Term, StringComparison.OrdinalIgnoreCase)) {
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
