using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Tests.Acquisition;

/// <summary>
/// Covers the custom-format scoring engine and its two decision seams: the pure matcher
/// (<see cref="CustomFormatEvaluation"/>) with Sonarr's required/any/negate algebra, the
/// <see cref="MinFormatScoreSpecification"/> floor gate, the <see cref="MediaUpgradeSpecification"/>
/// same-quality format-score upgrade path, and the load-bearing "DUAL flips the grab" case.
/// </summary>
public sealed class CustomFormatEvaluationTests {
    private static CustomFormatCondition Title(string pattern, bool negate = false, bool required = false) =>
        new(CustomFormatConditionType.ReleaseTitle, pattern, negate, required);

    private static ScoredCustomFormat Format(string name, int score, params CustomFormatCondition[] conditions) =>
        new(name, score, conditions);

    private static BookAcquisitionRules RulesWith(EntityKind kind, params ScoredCustomFormat[] formats) =>
        BookAcquisitionRules.Default with { Kind = kind, CustomFormats = formats };

    [Fact]
    public void NoFormatsScoresZero() {
        Assert.Equal(0, CustomFormatEvaluation.Score("Anything 1080p", BookAcquisitionRules.Default));
    }

    [Fact]
    public void SingleMatchingTitleConditionAddsItsScore() {
        var rules = RulesWith(EntityKind.Movie, Format("HDR", 50, Title("(?i)hdr")));
        Assert.Equal(50, CustomFormatEvaluation.Score("Movie 2160p WEB-DL HDR", rules));
        Assert.Equal(0, CustomFormatEvaluation.Score("Movie 1080p WEB-DL", rules));
    }

    [Fact]
    public void MultipleFormatsSumIncludingNegatives() {
        var rules = RulesWith(EntityKind.Movie,
            Format("x265", 50, Title("(?i)x265|hevc")),
            Format("Upscale", -200, Title("(?i)upscal")));
        // Both match → 50 + (-200) = -150.
        Assert.Equal(-150, CustomFormatEvaluation.Score("Movie 2160p x265 upscaled", rules));
    }

    [Fact]
    public void RequiredConditionMustMatchAndAtLeastOneConditionMustMatch() {
        // A format with a Required title condition and a non-required one: the Required one gates the format.
        var rules = RulesWith(EntityKind.Movie,
            Format("Required web", 100, Title("(?i)web", required: true), Title("(?i)hdr")));

        // Required matches AND at least one matches → applies.
        Assert.Equal(100, CustomFormatEvaluation.Score("Movie WEB-DL HDR", rules));
        // Required matches, the other doesn't, but the required IS a match, so "at least one" holds → applies.
        Assert.Equal(100, CustomFormatEvaluation.Score("Movie WEB-DL", rules));
        // Required fails → the whole format is rejected regardless of the other matching.
        Assert.Equal(0, CustomFormatEvaluation.Score("Movie BluRay HDR", rules));
    }

    [Fact]
    public void NonRequiredConditionsActAsAnOr() {
        // Neither required: any one matching applies the format.
        var rules = RulesWith(EntityKind.Movie,
            Format("Either", 30, Title("(?i)atmos"), Title("(?i)dts")));
        Assert.Equal(30, CustomFormatEvaluation.Score("Movie Atmos", rules));
        Assert.Equal(30, CustomFormatEvaluation.Score("Movie DTS-HD", rules));
        Assert.Equal(0, CustomFormatEvaluation.Score("Movie AAC", rules));
    }

    [Fact]
    public void NegateMatchesWhenTheUnderlyingTestDoesNot() {
        var rules = RulesWith(EntityKind.Movie, Format("No junk", 20, Title("(?i)cam|ts", negate: true)));
        // No "cam"/"ts" token → negated condition matches → format applies.
        Assert.Equal(20, CustomFormatEvaluation.Score("Movie 1080p BluRay", rules));
        // Contains "cam" → negated condition fails → format does not apply.
        Assert.Equal(0, CustomFormatEvaluation.Score("Movie 1080p CAM", rules));
    }

    [Fact]
    public void InvalidRegexNeverMatchesAndNeverThrows() {
        // An unbalanced group is an invalid pattern; it must simply never match.
        var rules = RulesWith(EntityKind.Movie, Format("Broken", 500, Title("(?i)(unclosed")));
        Assert.Equal(0, CustomFormatEvaluation.Score("Movie unclosed 1080p", rules));
    }

    [Fact]
    public void ReleaseGroupConditionMatchesTheDetectedGroup() {
        var rules = RulesWith(EntityKind.Movie,
            Format("Trusted group", 300, new CustomFormatCondition(CustomFormatConditionType.ReleaseGroup, "(?i)ntb", false, false)));
        Assert.Equal(300, CustomFormatEvaluation.Score("Show.S01E05.1080p.WEB-DL-NTb", rules));
        Assert.Equal(0, CustomFormatEvaluation.Score("Show.S01E05.1080p.WEB-DL-OTHER", rules));
    }

    [Fact]
    public void LanguageConditionMatchesADeclaredLanguage() {
        var rules = RulesWith(EntityKind.Movie,
            Format("French", 40, new CustomFormatCondition(CustomFormatConditionType.Language, "french", false, false)));
        // A FRENCH token in the title canonicalizes to "french".
        Assert.Equal(40, CustomFormatEvaluation.Score("Movie 1080p FRENCH", rules));
        Assert.Equal(0, CustomFormatEvaluation.Score("Movie 1080p GERMAN", rules));
    }

    [Fact]
    public void QualityConditionMatchesTheExactLadderCode() {
        var rules = RulesWith(EntityKind.Movie,
            Format("Remux only", 200, new CustomFormatCondition(CustomFormatConditionType.Quality, "remux-2160p", false, false)));
        Assert.Equal(200, CustomFormatEvaluation.Score("Movie 2160p BluRay REMUX", rules));
        Assert.Equal(0, CustomFormatEvaluation.Score("Movie 2160p WEB-DL", rules));
    }

    [Fact]
    public void QualityConditionSpeaksTheKindsLadder() {
        // The SAME code "lossless" is a music-ladder code; a movie release never matches it.
        var music = RulesWith(EntityKind.AudioLibrary,
            Format("Lossless", 100, new CustomFormatCondition(CustomFormatConditionType.Quality, "lossless", false, false)));
        Assert.Equal(100, CustomFormatEvaluation.Score("Artist - Album FLAC", music));
    }

    [Fact]
    public void DualFormatFlipsTheGrabDecision() {
        // The live install's real-world case: a +500 format matching (?i)dual|eng|english floats an
        // otherwise weaker release above its twin. Both are the same quality and the dual release has
        // FAR fewer seeders, so only the format score can outrank it.
        var engine = new MovieReleaseDecisionEngine();
        var rules = BookAcquisitionRules.Default with {
            Kind = EntityKind.Movie,
            CustomFormats = [Format("English/Dual", 500, Title("(?i)dual|eng|english"))]
        };

        var scored = engine.Evaluate([
            (Release("Movie 1080p WEB-DL", seeders: 9_000), null, "Idx"),
            (Release("Movie 1080p WEB-DL DUAL", seeders: 5), null, "Idx"),
        ], rules);

        Assert.Equal("Movie 1080p WEB-DL DUAL", scored[0].Release.Title);
    }

    [Fact]
    public void MinFormatScoreSpecificationRejectsBelowTheFloor() {
        var spec = new MinFormatScoreSpecification();
        var rules = BookAcquisitionRules.Default with {
            Kind = EntityKind.Movie,
            CustomFormats = [Format("Dual", 500, Title("(?i)dual"))],
            MinFormatScore = 1
        };

        // A dual release scores 500 ≥ 1 → accepted.
        Assert.Null(spec.Evaluate(Release("Movie 1080p DUAL", seeders: 10), rules));
        // A non-dual release scores 0 < 1 → rejected below the floor.
        Assert.Equal(ReleaseRejectionReason.BelowMinFormatScore, spec.Evaluate(Release("Movie 1080p", seeders: 10), rules));
    }

    [Fact]
    public void MinFormatScoreSpecificationIsNoOpWithoutCustomFormats() {
        var spec = new MinFormatScoreSpecification();
        // No formats configured → the gate never rejects, even with a positive floor.
        var rules = BookAcquisitionRules.Default with { Kind = EntityKind.Movie, MinFormatScore = 1_000 };
        Assert.Null(spec.Evaluate(Release("Movie 1080p", seeders: 10), rules));
    }

    [Fact]
    public void MinFormatScoreSpecificationRejectsNegativeScoringReleaseAtZeroFloor() {
        var spec = new MinFormatScoreSpecification();
        var rules = BookAcquisitionRules.Default with {
            Kind = EntityKind.Movie,
            CustomFormats = [Format("Banned upscale", -1_000, Title("(?i)upscal"))],
            MinFormatScore = 0
        };
        // An upscale scores -1000 < 0 → rejected (a soft ban via a large negative score).
        Assert.Equal(ReleaseRejectionReason.BelowMinFormatScore, spec.Evaluate(Release("Movie 2160p upscaled", seeders: 10), rules));
        // A clean release scores 0 ≥ 0 → accepted.
        Assert.Null(spec.Evaluate(Release("Movie 2160p BluRay", seeders: 10), rules));
    }

    [Fact]
    public void MediaUpgradeSpecificationAcceptsSameQualityHigherFormatScoreUnderCutoff() {
        var spec = new MediaUpgradeSpecification(EntityKind.Movie);
        // Owned: a webdl-1080p with format score 0. Candidate: same quality but a +500 dual format,
        // and the profile has a cutoff format score of 500 the owned copy has not reached.
        var rules = BookAcquisitionRules.Default with {
            Kind = EntityKind.Movie,
            IsUpgradeSearch = true,
            OwnedMediaQuality = "webdl-1080p",
            OwnedFormatScore = 0,
            CutoffFormatScore = 500,
            CustomFormats = [Format("Dual", 500, Title("(?i)dual"))]
        };

        // Same quality, strictly higher score, owned below cutoff → an upgrade.
        Assert.Null(spec.Evaluate(Release("Movie 1080p WEB-DL DUAL", seeders: 10), rules));
        // Same quality, same (zero) score → not an upgrade.
        Assert.Equal(ReleaseRejectionReason.NotAnUpgrade, spec.Evaluate(Release("Movie 1080p WEB-DL", seeders: 10), rules));
    }

    [Fact]
    public void MediaUpgradeSpecificationRejectsFormatScoreGainWhenNoCutoffSet() {
        var spec = new MediaUpgradeSpecification(EntityKind.Movie);
        // No CutoffFormatScore → a same-quality format-score gain is NOT an upgrade (nothing to chase toward).
        var rules = BookAcquisitionRules.Default with {
            Kind = EntityKind.Movie,
            IsUpgradeSearch = true,
            OwnedMediaQuality = "webdl-1080p",
            OwnedFormatScore = 0,
            CutoffFormatScore = null,
            CustomFormats = [Format("Dual", 500, Title("(?i)dual"))]
        };
        Assert.Equal(ReleaseRejectionReason.NotAnUpgrade, spec.Evaluate(Release("Movie 1080p WEB-DL DUAL", seeders: 10), rules));
    }

    [Fact]
    public void MediaUpgradeSpecificationRejectsFormatScoreGainWhenOwnedAlreadyAtCutoff() {
        var spec = new MediaUpgradeSpecification(EntityKind.Movie);
        // Owned already at/above the cutoff format score → no further format-score chase.
        var rules = BookAcquisitionRules.Default with {
            Kind = EntityKind.Movie,
            IsUpgradeSearch = true,
            OwnedMediaQuality = "webdl-1080p",
            OwnedFormatScore = 500,
            CutoffFormatScore = 500,
            CustomFormats = [Format("Dual", 500, Title("(?i)dual")), Format("Extra", 100, Title("(?i)atmos"))]
        };
        // A candidate scoring 600 (dual + atmos) is higher, but the owned copy already met the cutoff → not an upgrade.
        Assert.Equal(ReleaseRejectionReason.NotAnUpgrade, spec.Evaluate(Release("Movie 1080p WEB-DL DUAL Atmos", seeders: 10), rules));
    }

    [Fact]
    public void MediaUpgradeSpecificationNeverRescuesALowerLadderPositionWithFormatScore() {
        var spec = new MediaUpgradeSpecification(EntityKind.Movie);
        // A strictly LOWER ladder position with a big format score is never an upgrade — position dominates.
        var rules = BookAcquisitionRules.Default with {
            Kind = EntityKind.Movie,
            IsUpgradeSearch = true,
            OwnedMediaQuality = "bluray-1080p",
            OwnedFormatScore = 0,
            CutoffFormatScore = 500,
            CustomFormats = [Format("Dual", 500, Title("(?i)dual"))]
        };
        // webdl-1080p is below bluray-1080p on the ladder — the +500 dual score cannot rescue it.
        Assert.Equal(ReleaseRejectionReason.NotAnUpgrade, spec.Evaluate(Release("Movie 1080p WEB-DL DUAL", seeders: 10), rules));
    }

    private static IndexerRelease Release(string title, int seeders) =>
        new(title, SizeBytes: 1_000_000_000, Seeders: seeders, Peers: seeders, DownloadProtocol.Torrent,
            DownloadUrl: "http://dl", MagnetUrl: null, InfoHash: null, InfoUrl: null, Language: null, PublishedAt: null);
}
