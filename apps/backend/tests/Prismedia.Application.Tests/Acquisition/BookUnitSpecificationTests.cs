using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Tests.Acquisition;

/// <summary>
/// Covers the book/comic wrong-volume gate — the book analog of the wrong-tv-unit specification: a
/// Volume 1 search must never auto-grab Volume 3 on quality or seeders, volume-less releases stay
/// eligible (omnibus/unlabeled), and exact-volume releases outrank them.
/// </summary>
public sealed class BookUnitSpecificationTests {
    [Theory]
    [InlineData("Berserk Vol 3 (2019) [CBZ]", 3)]
    [InlineData("Berserk Vol. 3 [CBZ]", 3)]
    [InlineData("Berserk Volume 3 (Dark Horse) [CBZ]", 3)]
    [InlineData("Berserk v03 (2019) (Digital)", 3)]
    [InlineData("Berserk v112 (Digital)", 112)]
    [InlineData("Berserk (2019) [CBZ]", null)]
    // A bare v + single digit is an anime-style revision marker, never a volume.
    [InlineData("Some Episode v2 [CBZ]", null)]
    public void ParseVolumeReadsTheCommonConventions(string title, int? expected) {
        Assert.Equal(expected, BookReleaseTokens.ParseVolume(title));
    }

    [Fact]
    public void VolumeSearchRejectsOtherVolumesAndPassesUnlabeledReleases() {
        var spec = new BookUnitSpecification();
        var rules = BookAcquisitionRules.Default with { VolumeNumber = 1 };

        Assert.Null(spec.Evaluate(Release("Berserk Vol 1 (Digital) [CBZ]"), rules));
        Assert.Null(spec.Evaluate(Release("Berserk Deluxe Edition [CBZ]"), rules));
        Assert.Equal(ReleaseRejectionReason.WrongVolume, spec.Evaluate(Release("Berserk Vol 3 (Digital) [CBZ]"), rules));
        Assert.Equal(ReleaseRejectionReason.WrongVolume, spec.Evaluate(Release("Berserk v03 (Digital) [CBZ]"), rules));

        // No sought volume → the gate is a no-op (whole-book acquisitions).
        Assert.Null(spec.Evaluate(Release("Berserk Vol 3 (Digital) [CBZ]"), BookAcquisitionRules.Default));
    }

    [Fact]
    public void EngineRejectsWrongVolumesAndPrefersTheExactVolumeOverUnlabeled() {
        var engine = new BookReleaseDecisionEngine();
        var rules = BookAcquisitionRules.Default with {
            TargetTitle = "Berserk",
            VolumeNumber = 1,
        };

        var scored = engine.Evaluate([
            (Release("Berserk Vol 1 [CBZ]", seeders: 5), null, "Idx"),
            (Release("Berserk Deluxe Edition [EPUB]", seeders: 900), null, "Idx"),
            (Release("Berserk Vol 3 [CBZ]", seeders: 500), null, "Idx"),
        ], rules);

        var verdicts = scored.ToDictionary(candidate => candidate.Release.Title, candidate => candidate);
        Assert.True(verdicts["Berserk Vol 1 [CBZ]"].Accepted);
        Assert.True(verdicts["Berserk Deluxe Edition [EPUB]"].Accepted);
        Assert.Contains(ReleaseRejectionReason.WrongVolume, verdicts["Berserk Vol 3 [CBZ]"].Rejections);
        // The exact-volume release wins the auto-pick over the (higher-quality, better-seeded) unlabeled one.
        Assert.Equal("Berserk Vol 1 [CBZ]", scored.First(candidate => candidate.Accepted).Release.Title);
    }

    [Fact]
    public void VolumeLabelTitlesFallBackToVolumeAgreementInTheIdentityGate() {
        // A volume phantom's own title is often just the label ("Vol. 3"); release naming spells it
        // differently ("v03"), so the identity gate accepts on volume agreement instead of a title run.
        var spec = new BookTitleIdentitySpecification();
        var rules = BookAcquisitionRules.Default with {
            TargetTitle = "Vol. 3",
            TargetAuthor = "Kentaro Miura",
            VolumeNumber = 3,
        };

        Assert.Null(spec.Evaluate(Release("Kentaro Miura - Berserk v03 (Digital) [CBZ]"), rules));
        Assert.Equal(ReleaseRejectionReason.TitleMismatch,
            spec.Evaluate(Release("Kentaro Miura - Berserk v05 (Digital) [CBZ]"), rules));
    }

    private static IndexerRelease Release(string title, int seeders = 5) =>
        new(title, SizeBytes: 50_000_000, Seeders: seeders, Peers: seeders, DownloadProtocol.Torrent,
            DownloadUrl: "http://dl", MagnetUrl: null, InfoHash: null, InfoUrl: null, Language: null, PublishedAt: null);
}
