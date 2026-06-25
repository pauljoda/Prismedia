using Prismedia.Application.Acquisition;
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
        var rules = BookAcquisitionRules.Default with { Language = "English" };

        var mismatch = Engine.Evaluate(One(Release(language: "French")), rules);
        var unknown = Engine.Evaluate(One(Release(language: null)), rules);

        Assert.Contains(ReleaseRejectionReason.LanguageMismatch, mismatch[0].Rejections);
        Assert.DoesNotContain(ReleaseRejectionReason.LanguageMismatch, unknown[0].Rejections);
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
}
