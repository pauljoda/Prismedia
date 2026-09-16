using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Tests.Acquisition;

public sealed class ComicInstallmentSpecificationTests {
    private static readonly BookReleaseDecisionEngine Engine = new(EntityKind.ComicInstallment);

    [Theory]
    [InlineData("Chapter 12.5", "Chapter 12")]
    [InlineData("Issue 12A", "Issue 12B")]
    [InlineData("Issue -1", "Issue 1")]
    [InlineData("Issue 1/2", "Issue 1")]
    [InlineData("Chapter 123456", "Chapter 12345")]
    public void ExactInstallmentLabelsCannotCollapseIntoTheSameInteger(string sought, string offered) {
        var result = Evaluate($"Witch Hat Atelier {offered} Digital CBZ", sought);

        Assert.False(result.Accepted);
        Assert.Contains(ReleaseRejectionReason.WrongInstallment, result.Rejections);
    }

    [Theory]
    [InlineData("Chapter 012.50", "Chapter 12.5")]
    [InlineData("Issue 12a", "Issue 12A")]
    [InlineData("Issue 1/2", "Issue 1/2")]
    [InlineData("Chapter 0", "Chapter 000")]
    public void EquivalentExactInstallmentLabelsAreAccepted(string sought, string offered) {
        Assert.True(Evaluate($"Witch Hat Atelier {offered} Digital CBZ", sought).Accepted);
    }

    [Fact]
    public void ASeriesPackWithoutAnInstallmentNumberCannotAutomaticallyFulfillAnExactIssue() {
        var result = Evaluate("Witch Hat Atelier Complete Digital CBZ", "Chapter 12.5");
        Assert.Contains(ReleaseRejectionReason.WrongInstallment, result.Rejections);
    }

    [Fact]
    public void MatchingInstallmentArchiveIsAccepted() {
        var result = Evaluate("Witch Hat Atelier Chapter 83 Digital CBZ", "Chapter 83");

        Assert.True(result.Accepted);
        Assert.DoesNotContain(ReleaseRejectionReason.WrongInstallment, result.Rejections);
    }

    [Fact]
    public void DifferentInstallmentIsRejectedEvenWhenTheSeriesMatches() {
        var result = Evaluate("Witch Hat Atelier Chapter 82 Digital CBZ", "Chapter 83");

        Assert.False(result.Accepted);
        Assert.Contains(ReleaseRejectionReason.WrongInstallment, result.Rejections);
    }

    [Fact]
    public void ComicAcquisitionAcceptsOnlyImageArchives() {
        var result = Evaluate("Witch Hat Atelier Chapter 83 EPUB", "Chapter 83");

        Assert.False(result.Accepted);
        Assert.Contains(ReleaseRejectionReason.UnsupportedFormat, result.Rejections);
    }

    private static ScoredRelease Evaluate(string releaseTitle, string installmentTitle) {
        var release = new IndexerRelease(
            releaseTitle,
            10_000_000,
            12,
            2,
            DownloadProtocol.Torrent,
            "https://example.invalid/download",
            null,
            "hash",
            null,
            null,
            null);
        var rules = BookAcquisitionRules.Default with {
            Kind = EntityKind.ComicInstallment,
            TargetTitle = installmentTitle
        };

        return Assert.Single(Engine.Evaluate([(release, null, "Indexer")], rules));
    }
}
