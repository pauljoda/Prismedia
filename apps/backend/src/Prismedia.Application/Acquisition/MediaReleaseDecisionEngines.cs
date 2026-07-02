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
    /// top-level category. Kinds with no mapping keep the configured list unchanged.
    /// </summary>
    public static IReadOnlyList<int> ForKind(EntityKind kind, IReadOnlyList<int> configured) {
        if (RangeStartFor(kind) is not { } start) {
            return configured;
        }

        var inRange = configured.Where(category => category >= start && category < start + RangeSize).ToArray();
        return inRange.Length > 0 ? inRange : [start];
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
        new LanguageSpecification()
    ];

    public IReadOnlyList<ScoredRelease> Evaluate(
        IReadOnlyList<(IndexerRelease Release, Guid? IndexerConfigId, string IndexerName)> releases,
        BookAcquisitionRules rules,
        IReadOnlySet<string>? blocklistedIdentities = null) =>
        MediaReleaseEvaluation.Evaluate(releases, rules, blocklistedIdentities, Specifications, MovieScore);

    /// <summary>Profile preference (terms, custom weights, language) outranks everything; then resolution, then source provenance, then seeders.</summary>
    private static double MovieScore(IndexerRelease release, BookAcquisitionRules rules) {
        var title = release.Title;
        var resolution =
            MediaReleaseEvaluation.TitleHasAny(title, "2160p", "4k", "uhd") ? 4 :
            MediaReleaseEvaluation.TitleHasAny(title, "1080p") ? 3 :
            MediaReleaseEvaluation.TitleHasAny(title, "720p") ? 2 :
            MediaReleaseEvaluation.TitleHasAny(title, "480p", "dvdrip") ? 1 : 0;
        var source =
            MediaReleaseEvaluation.TitleHasAny(title, "remux") ? 5 :
            MediaReleaseEvaluation.TitleHasAny(title, "bluray", "blu-ray", "bdrip", "brrip") ? 4 :
            MediaReleaseEvaluation.TitleHasAny(title, "web-dl", "webdl") ? 3 :
            MediaReleaseEvaluation.TitleHasAny(title, "webrip", "web") ? 2 :
            MediaReleaseEvaluation.TitleHasAny(title, "hdtv") ? 1 : 0;
        return MediaReleaseEvaluation.PreferenceScore(release, rules) * 10_000
            + resolution * 100_000
            + source * 10_000
            + Math.Min(release.Seeders ?? 0, 9_999);
    }
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
        new LanguageSpecification()
    ];

    public IReadOnlyList<ScoredRelease> Evaluate(
        IReadOnlyList<(IndexerRelease Release, Guid? IndexerConfigId, string IndexerName)> releases,
        BookAcquisitionRules rules,
        IReadOnlySet<string>? blocklistedIdentities = null) =>
        MediaReleaseEvaluation.Evaluate(releases, rules, blocklistedIdentities, Specifications, MusicScore);

    /// <summary>Profile preference (terms, custom weights, language) outranks everything; then codec quality (lossless first), then seeders.</summary>
    private static double MusicScore(IndexerRelease release, BookAcquisitionRules rules) {
        var title = release.Title;
        var codec =
            MediaReleaseEvaluation.TitleHasAny(title, "flac", "alac", "lossless") ? 3 :
            MediaReleaseEvaluation.TitleHasAny(title, "320", "v0") ? 2 :
            MediaReleaseEvaluation.TitleHasAny(title, "mp3", "aac", "opus", "ogg") ? 1 : 0;
        return MediaReleaseEvaluation.PreferenceScore(release, rules) * 10_000
            + codec * 100_000
            + Math.Min(release.Seeders ?? 0, 9_999);
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

    /// <summary>How many of the profile's preferred terms the title matches.</summary>
    public static int PreferredTermMatches(string title, BookAcquisitionRules rules) =>
        rules.PreferredTerms.Count(term => !string.IsNullOrWhiteSpace(term) && title.Contains(term, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The profile-preference component of a release's score, in shared preference points: each preferred
    /// term is worth 100, each matched custom weighted term contributes its own weight (so 100 equals one
    /// preferred term, negatives push down), and the release's language earns up to 50 per step of the
    /// ordered preferred-language list (an unmarked or multi release counts as the top preference). Every
    /// engine multiplies this by its own boost so preference always compares the same way across kinds.
    /// </summary>
    public static double PreferenceScore(IndexerRelease release, BookAcquisitionRules rules) {
        var title = release.Title;
        double score = PreferredTermMatches(title, rules) * 100;
        foreach (var term in rules.WeightedTerms) {
            if (!string.IsNullOrWhiteSpace(term.Term) && title.Contains(term.Term, StringComparison.OrdinalIgnoreCase)) {
                score += term.Weight;
            }
        }

        return score + LanguagePreferenceBonus(release, rules);
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
