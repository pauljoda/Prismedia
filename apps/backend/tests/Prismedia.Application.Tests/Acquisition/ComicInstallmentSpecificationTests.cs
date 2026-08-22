using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Tests.Acquisition;

public sealed class ComicInstallmentSpecificationTests {
    private static readonly BookReleaseDecisionEngine Engine = new(EntityKind.ComicInstallment);

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
