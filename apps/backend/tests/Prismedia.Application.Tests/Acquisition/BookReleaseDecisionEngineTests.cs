using Prismedia.Application.Acquisition;
using Prismedia.Contracts.Acquisition;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Tests.Acquisition;

public sealed class BookReleaseDecisionEngineTests {
    private static readonly BookReleaseDecisionEngine Engine = new();

    private static IndexerRelease Release(
        string title = "Some Book (2020) (epub)",
        long sizeBytes = 5_000_000,
        int? seeders = 10,
        int? peers = 2,
        DownloadProtocol protocol = DownloadProtocol.Torrent,
        string? language = null) =>
        new(title, sizeBytes, seeders, peers, protocol, "http://dl", "magnet:?x", "hash", "http://info", language, null);

    private static IReadOnlyList<(IndexerRelease, Guid?, string)> One(IndexerRelease release) =>
        [(release, (Guid?)null, "Test Indexer")];

    [Fact]
    public void AcceptsCleanTorrentWithNoRules() {
        var result = Engine.Evaluate(One(Release()), BookAcquisitionRules.Default);

        Assert.Single(result);
        Assert.True(result[0].Accepted);
        Assert.Empty(result[0].Rejections);
    }

    [Fact]
    public void RejectsReleaseWithNoLinkAndNoInfoPage() {
        var linkless = new IndexerRelease("Some Book (epub)", 5_000_000, 5, 1, DownloadProtocol.Torrent, null, null, null, null, null, null);

        var result = Engine.Evaluate(One(linkless), BookAcquisitionRules.Default);

        Assert.False(result[0].Accepted);
        Assert.Contains(ReleaseRejectionReason.NoDownloadLink, result[0].Rejections);
    }

    [Fact]
    public void AcceptsInfoPageOnlyReleaseAsResolvable() {
        // Meta-search results have only an info page; the magnet is resolved from it at queue time.
        var infoOnly = new IndexerRelease("Some Book (epub)", 5_000_000, 5, 1, DownloadProtocol.Torrent, null, null, null, "http://info", null, null);

        var result = Engine.Evaluate(One(infoOnly), BookAcquisitionRules.Default);

        Assert.True(result[0].Accepted);
        Assert.DoesNotContain(ReleaseRejectionReason.NoDownloadLink, result[0].Rejections);
    }

    [Fact]
    public void RejectsUsenetProtocol() {
        var result = Engine.Evaluate(One(Release(protocol: DownloadProtocol.Usenet)), BookAcquisitionRules.Default);

        Assert.False(result[0].Accepted);
        Assert.Contains(ReleaseRejectionReason.WrongProtocol, result[0].Rejections);
    }

    [Fact]
    public void RejectsBelowMinSeeders() {
        var rules = BookAcquisitionRules.Default with { MinSeeders = 5 };

        var result = Engine.Evaluate(One(Release(seeders: 2)), rules);

        Assert.Contains(ReleaseRejectionReason.BelowMinSeeders, result[0].Rejections);
    }

    [Fact]
    public void UnknownSeedersPassMinSeeders() {
        var rules = BookAcquisitionRules.Default with { MinSeeders = 5 };

        var result = Engine.Evaluate(One(Release(seeders: null)), rules);

        Assert.DoesNotContain(ReleaseRejectionReason.BelowMinSeeders, result[0].Rejections);
    }

    [Fact]
    public void RejectsDisallowedFormatWhenTitleNamesIt() {
        var rules = BookAcquisitionRules.Default with { AllowedFormats = [BookFormat.ImageArchive] };

        var result = Engine.Evaluate(One(Release(title: "Some Book (2020) (epub)")), rules);

        Assert.Contains(ReleaseRejectionReason.UnsupportedFormat, result[0].Rejections);
    }

    [Fact]
    public void AllowsMatchingFormat() {
        var rules = BookAcquisitionRules.Default with { AllowedFormats = [BookFormat.Epub] };

        var result = Engine.Evaluate(One(Release(title: "Some Book (2020) (EPUB)")), rules);

        Assert.True(result[0].Accepted);
    }

    [Fact]
    public void RejectsTitleNamingOnlyAnUnimportableFormat() {
        // Default rules (allow-all): a release whose only declared format is mobi/cbr can't be imported,
        // so it must be rejected up front rather than downloaded and dead-ended at import.
        var mobi = Engine.Evaluate(One(Release(title: "The Hobbit (MOBI)")), BookAcquisitionRules.Default);
        var cbr = Engine.Evaluate(One(Release(title: "Saga Vol 1 (CBR)")), BookAcquisitionRules.Default);

        Assert.Contains(ReleaseRejectionReason.UnsupportedFormat, mobi[0].Rejections);
        Assert.Contains(ReleaseRejectionReason.UnsupportedFormat, cbr[0].Rejections);
    }

    [Fact]
    public void TitleWithoutFormatTokenPassesFormatRule() {
        var rules = BookAcquisitionRules.Default with { AllowedFormats = [BookFormat.Epub] };

        var result = Engine.Evaluate(One(Release(title: "Some Book 2020")), rules);

        Assert.DoesNotContain(ReleaseRejectionReason.UnsupportedFormat, result[0].Rejections);
    }

    [Fact]
    public void RejectsSizeOutOfRange() {
        var rules = BookAcquisitionRules.Default with { MaxSizeBytes = 1_000_000 };

        var result = Engine.Evaluate(One(Release(sizeBytes: 5_000_000)), rules);

        Assert.Contains(ReleaseRejectionReason.SizeOutOfRange, result[0].Rejections);
    }

    [Fact]
    public void RejectsMissingRequiredTermAndPresentIgnoredTerm() {
        var rules = BookAcquisitionRules.Default with {
            RequiredTerms = ["retail"],
            IgnoredTerms = ["scan"]
        };

        var result = Engine.Evaluate(One(Release(title: "Some Book (scan) (epub)")), rules);

        Assert.Contains(ReleaseRejectionReason.MissingRequiredTerm, result[0].Rejections);
        Assert.Contains(ReleaseRejectionReason.HasIgnoredTerm, result[0].Rejections);
    }

    [Fact]
    public void RejectsLanguageMismatchButAllowsUnknownLanguage() {
        var rules = BookAcquisitionRules.Default with { PreferredLanguages = ["English"] };

        var mismatch = Engine.Evaluate(One(Release(language: "French")), rules);
        var unknown = Engine.Evaluate(One(Release(language: null)), rules);

        Assert.Contains(ReleaseRejectionReason.LanguageMismatch, mismatch[0].Rejections);
        Assert.DoesNotContain(ReleaseRejectionReason.LanguageMismatch, unknown[0].Rejections);
    }

    [Fact]
    public void LanguageGateReadsTitleTokensAndAllowsMultiAndAliases() {
        var rules = BookAcquisitionRules.Default with { PreferredLanguages = ["English"] };

        var french = Engine.Evaluate(One(Release(title: "Some Book FRENCH epub")), rules);
        var multi = Engine.Evaluate(One(Release(title: "Some Book MULTi epub")), rules);
        var aliased = Engine.Evaluate(One(Release(title: "Some Book ENG epub")), rules);

        Assert.Contains(ReleaseRejectionReason.LanguageMismatch, french[0].Rejections);
        Assert.DoesNotContain(ReleaseRejectionReason.LanguageMismatch, multi[0].Rejections);
        Assert.DoesNotContain(ReleaseRejectionReason.LanguageMismatch, aliased[0].Rejections);
    }

    [Fact]
    public void WeightedTermsMoveRankingUpAndDown() {
        var rules = BookAcquisitionRules.Default with {
            WeightedTerms = [new WeightedTerm("retail", 100), new WeightedTerm("abridged", -200)]
        };

        var result = Engine.Evaluate([
            (Release(title: "Some Book abridged epub", seeders: 500), null, "Test Indexer"),
            (Release(title: "Some Book epub", seeders: 10), null, "Test Indexer"),
            (Release(title: "Some Book retail epub", seeders: 10), null, "Test Indexer")
        ], rules);

        Assert.Equal(
            ["Some Book retail epub", "Some Book epub", "Some Book abridged epub"],
            result.Select(candidate => candidate.Release.Title).ToArray());
    }

    [Fact]
    public void PreferredLanguageOrderRanksEarlierLanguagesHigher() {
        var rules = BookAcquisitionRules.Default with { PreferredLanguages = ["English", "German"] };

        var result = Engine.Evaluate([
            (Release(title: "Some Book GERMAN epub", seeders: 500), null, "Test Indexer"),
            (Release(title: "Some Book epub", seeders: 10), null, "Test Indexer")
        ], rules);

        // The unmarked release counts as the top preference (English) and outranks the German copy.
        Assert.Equal(
            ["Some Book epub", "Some Book GERMAN epub"],
            result.Select(candidate => candidate.Release.Title).ToArray());
    }

    [Fact]
    public void RejectsBlocklistedReleaseByInfoHash() {
        var release = new IndexerRelease("Some Book (epub)", 5_000_000, 50, 5, DownloadProtocol.Torrent, "http://dl", "magnet:?x", "ABCDEF", "http://info", null, null);
        var blocklist = new HashSet<string> { ReleaseIdentity.For("abcdef", "Test Indexer", "irrelevant title") };

        var result = Engine.Evaluate(One(release), BookAcquisitionRules.Default, blocklist);

        Assert.False(result[0].Accepted);
        Assert.Contains(ReleaseRejectionReason.Blocklisted, result[0].Rejections);
    }

    [Fact]
    public void RejectsBlocklistedReleaseByTitleWhenNoInfoHash() {
        var noHash = new IndexerRelease("Some Book (epub)", 5_000_000, 50, 5, DownloadProtocol.Torrent, "http://dl", "magnet:?x", null, "http://info", null, null);
        var blocklist = new HashSet<string> { ReleaseIdentity.For(null, "Test Indexer", "Some Book (epub)") };

        var result = Engine.Evaluate(One(noHash), BookAcquisitionRules.Default, blocklist);

        Assert.Contains(ReleaseRejectionReason.Blocklisted, result[0].Rejections);
    }

    [Fact]
    public void NonBlocklistedReleaseIsUnaffectedByBlocklist() {
        var blocklist = new HashSet<string> { ReleaseIdentity.For("someotherhash", "Other", "Other Book") };

        var result = Engine.Evaluate(One(Release()), BookAcquisitionRules.Default, blocklist);

        Assert.True(result[0].Accepted);
        Assert.DoesNotContain(ReleaseRejectionReason.Blocklisted, result[0].Rejections);
    }

    [Fact]
    public void NullBlocklistLeavesDecisionsUnchanged() {
        // The blocklist parameter defaults to null so existing callers see no behavior change.
        var result = Engine.Evaluate(One(Release()), BookAcquisitionRules.Default, null);

        Assert.True(result[0].Accepted);
        Assert.Empty(result[0].Rejections);
    }

    [Fact]
    public void PreferredTermOutranksSeeders() {
        var rules = BookAcquisitionRules.Default with { PreferredTerms = ["retail"] };
        var preferred = Release(title: "Some Book (retail) (epub)", seeders: 5);
        var seedy = Release(title: "Some Book (epub)", seeders: 500);

        var result = Engine.Evaluate([(seedy, null, "i"), (preferred, null, "i")], rules);

        // The preferred release ranks first despite far fewer seeders.
        Assert.Equal("Some Book (retail) (epub)", result[0].Release.Title);
        Assert.True(result[0].Score > result[1].Score);
    }

    [Fact]
    public void MorePreferredMatchesRankHigher() {
        var rules = BookAcquisitionRules.Default with { PreferredTerms = ["retail", "epub"] };
        var two = Release(title: "Some Book (retail) (epub)", seeders: 1);
        var one = Release(title: "Some Book (retail) (pdf)", seeders: 999);

        var result = Engine.Evaluate([(one, null, "i"), (two, null, "i")], rules);

        Assert.Equal("Some Book (retail) (epub)", result[0].Release.Title);
    }

    [Fact]
    public void NoPreferredTermsLeavesSeederOrderingUnchanged() {
        // Default rules (no preferred terms) → the original seeder-based ranking is preserved.
        var high = Release(title: "Book A (epub)", seeders: 100);
        var low = Release(title: "Book B (epub)", seeders: 5);

        var result = Engine.Evaluate([(low, null, "i"), (high, null, "i")], BookAcquisitionRules.Default);

        Assert.Equal("Book A (epub)", result[0].Release.Title);
    }

    [Fact]
    public void OrdersAcceptedBeforeRejectedAndBySeeders() {
        var highSeed = Release(title: "Book A (epub)", seeders: 100);
        var lowSeed = Release(title: "Book B (epub)", seeders: 5);
        var usenet = Release(title: "Book C (epub)", seeders: 999, protocol: DownloadProtocol.Usenet);

        var result = Engine.Evaluate(
            [(highSeed, null, "i"), (lowSeed, null, "i"), (usenet, null, "i")],
            BookAcquisitionRules.Default);

        // Accepted come first, highest seeders first; the rejected usenet release trails despite its seed count.
        Assert.Equal("Book A (epub)", result[0].Release.Title);
        Assert.Equal("Book B (epub)", result[1].Release.Title);
        Assert.False(result[2].Accepted);
    }

    // ── Quality gates (slice 2b-ii.4) ─────────────────────────────────────────

    private static ReleaseRejectionReason[] Reasons(IndexerRelease release, BookAcquisitionRules rules) =>
        [.. Engine.Evaluate(One(release), rules)[0].Rejections];

    [Fact]
    public void InitialGrabIsNotGatedByQualityOrUpgrade() {
        // Default rules (MinQuality and OwnedQuality both Floor): even a low-quality release is accepted —
        // the quality/upgrade specs are inert on an initial grab.
        var result = Engine.Evaluate(One(Release(title: "Some Book (pdf)")), BookAcquisitionRules.Default);

        Assert.True(result[0].Accepted);
        Assert.DoesNotContain(ReleaseRejectionReason.QualityNotAllowed, result[0].Rejections);
        Assert.DoesNotContain(ReleaseRejectionReason.NotAnUpgrade, result[0].Rejections);
        Assert.DoesNotContain(ReleaseRejectionReason.FormatDowngrade, result[0].Rejections);
    }

    [Fact]
    public void InitialGrabRanksHigherQualityFirstEvenWithFewerSeeders() {
        var retailEpub = Release(title: "Some Book (retail) (epub)", seeders: 5);
        var plainPdf = Release(title: "Some Book (pdf)", seeders: 500);

        var result = Engine.Evaluate([(plainPdf, null, "i"), (retailEpub, null, "i")], BookAcquisitionRules.Default);

        Assert.True(result[0].Accepted);
        Assert.Equal("Some Book (retail) (epub)", result[0].Release.Title);
    }

    [Fact]
    public void RejectsBelowMinQualityFloor() {
        var rules = BookAcquisitionRules.Default with { MinQuality = new(BookSourceTier.Web, BookFormatTier.Reflowable) };

        // Untagged source ("(epub)" → Unknown source) is below the Web floor.
        Assert.Contains(ReleaseRejectionReason.QualityNotAllowed, Reasons(Release(title: "Some Book (epub)"), rules));
        // Meets the floor on both axes.
        Assert.DoesNotContain(ReleaseRejectionReason.QualityNotAllowed, Reasons(Release(title: "Some Book (web) (epub)"), rules));
    }

    [Fact]
    public void UpgradeRejectsEqualQualityAndAcceptsStrictlyBetter() {
        var owned = new BookQualityRank(BookSourceTier.Web, BookFormatTier.Reflowable);
        var rules = BookAcquisitionRules.Default with { OwnedQuality = owned, IsUpgradeSearch = true };

        Assert.Contains(ReleaseRejectionReason.NotAnUpgrade, Reasons(Release(title: "Some Book (web) (epub)"), rules));
        Assert.DoesNotContain(ReleaseRejectionReason.NotAnUpgrade, Reasons(Release(title: "Some Book (retail) (epub)"), rules));
    }

    [Fact]
    public void UpgradeGatesDoNotApplyWithoutTheUpgradeFlag() {
        // OwnedQuality is set but IsUpgradeSearch is false (e.g. an unrelated ad-hoc search): the upgrade and
        // format-floor gates stay inert, so this is decoupled from the owned-quality value entirely.
        var rules = BookAcquisitionRules.Default with { OwnedQuality = new(BookSourceTier.Retail, BookFormatTier.Archive) };

        var reasons = Reasons(Release(title: "Some Book (web) (pdf)"), rules);
        Assert.DoesNotContain(ReleaseRejectionReason.NotAnUpgrade, reasons);
        Assert.DoesNotContain(ReleaseRejectionReason.FormatDowngrade, reasons);
    }

    [Fact]
    public void UpgradeRejectsFormatDowngradeEvenWithBetterSource() {
        // The load-bearing rule: a retail PDF must never replace an owned web EPUB (format would regress).
        var rules = BookAcquisitionRules.Default with { OwnedQuality = new(BookSourceTier.Web, BookFormatTier.Reflowable), IsUpgradeSearch = true };
        var reasons = Reasons(Release(title: "Some Book (retail) (pdf)"), rules);

        Assert.Contains(ReleaseRejectionReason.FormatDowngrade, reasons);
        Assert.Contains(ReleaseRejectionReason.NotAnUpgrade, reasons);
        Assert.False(Engine.Evaluate(One(Release(title: "Some Book (retail) (pdf)")), rules)[0].Accepted);
    }

    [Fact]
    public void UpgradeWithFormatAnonymousTitleIsNotAnUpgradeNotADowngrade() {
        // A title naming no format makes no downgrade claim: it must not be mislabeled FormatDowngrade, but it
        // still isn't a confirmed upgrade, so UpgradeSpecification rejects it as NotAnUpgrade.
        var rules = BookAcquisitionRules.Default with { OwnedQuality = new(BookSourceTier.Web, BookFormatTier.Reflowable), IsUpgradeSearch = true };
        var reasons = Reasons(Release(title: "Some Book (retail)"), rules);

        Assert.DoesNotContain(ReleaseRejectionReason.FormatDowngrade, reasons);
        Assert.Contains(ReleaseRejectionReason.NotAnUpgrade, reasons);
    }

    [Fact]
    public void UnknownOwnedSourceRequiresAFormatWinNotASourceOnlyGain() {
        // Owned PDF with unparseable source: a "retail" PDF (source-only gain) is NOT trusted to replace it,
        // because the owned file might already be retail. Only a real format improvement counts.
        var rules = BookAcquisitionRules.Default with { OwnedQuality = new(BookSourceTier.Unknown, BookFormatTier.Fixed), IsUpgradeSearch = true };

        Assert.Contains(ReleaseRejectionReason.NotAnUpgrade, Reasons(Release(title: "Some Book (retail) (pdf)"), rules));
        // A genuine format win (PDF → EPUB) is accepted even though the source stays unknown.
        Assert.DoesNotContain(ReleaseRejectionReason.NotAnUpgrade, Reasons(Release(title: "Some Book (epub)"), rules));
    }
}
