using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Tests.Acquisition;

public sealed class AcquisitionWorkTitlesTests {
    [Theory]
    [InlineData("Example Show EP018 1080p.mkv")]
    [InlineData("Example Show Episode 18 1080p.mkv")]
    public void ExplicitAbsolutePrefixesMapToTheVerifiedCatalogSlot(string filename) {
        var plan = TvImportPlanBuilder.PlanUnits([new(filename, 1000)], "Example Show", 2, null,
            episodeTitles: [new(1, "Requested Story", AbsoluteEpisode: 18), new(2, "Other Story", AbsoluteEpisode: 19)]);
        Assert.False(plan.Blocked);
        Assert.Equal(1, Assert.Single(plan.Units).Episode);
    }

    [Theory]
    [InlineData("Romanized Series - 83 [1080p]", true)]
    [InlineData("Primary Series - 83 [1080p]", true)]
    [InlineData("Romanized Series S02E02 1080p", false)]
    [InlineData("Romanized Series Sequel - 83 [1080p]", false)]
    [InlineData("Romanized Series 2019 S02E01 1080p", false)]
    [InlineData("Unrelated Series - 83 [1080p]", false)]
    public void FormalAlternativesKeepWorkYearAndEpisodeGates(string title, bool accepted) {
        var release = new IndexerRelease(title, 500_000_000, 10, 2, DownloadProtocol.Torrent,
            "https://download.test/release", null, null, null, null, null);
        var rules = BookAcquisitionRules.Default with {
            Kind = EntityKind.VideoEpisode, TargetTitle = "Primary Series", TargetAlternativeTitles = ["Romanized Series"],
            TargetYear = 2007, SeasonNumber = 2, EpisodeNumber = 1,
            TargetEpisodeTitle = "Final Message", TargetAbsoluteEpisodeNumber = 83
        };
        Assert.Equal(accepted, Assert.Single(new TvReleaseDecisionEngine(EntityKind.VideoEpisode)
            .Evaluate([(release, null, "Indexer")], rules)).Accepted);
    }

    [Fact]
    public void QueryVariantsPreserveTheRequestedCoordinatesAndRemoveDuplicateNames() {
        var input = new AcquisitionSearchInput(Guid.NewGuid(), "Final Message", null, EntityKind.VideoEpisode,
            Year: 2007, Series: "Primary Series", SeasonNumber: 2, EpisodeNumber: 1, AbsoluteEpisodeNumber: 83) {
            AlternativeWorkTitles = ["Primary.Series", "Romanized Series", "Romanized.Series", " "]
        };
        var variants = AcquisitionWorkTitles.QueryInputs(input);
        Assert.Equal(2, variants.Count);
        Assert.All(variants, variant => {
            Assert.Equal(input.Id, variant.Id);
            Assert.Equal(input.Title, variant.Title);
            Assert.Equal(2, variant.SeasonNumber);
            Assert.Equal(1, variant.EpisodeNumber);
            Assert.Equal(83, variant.AbsoluteEpisodeNumber);
            Assert.Equal(2007, variant.Year);
        });
    }

    [Fact]
    public void AlternativeNameNumbersDoNotLeakIntoEpisodeMappingOrLibraryNaming() {
        var plan = TvImportPlanBuilder.PlanUnits([new("Room.104 - 57 [1080p H.264].mkv", 1000)],
            "Primary Series", 2, null,
            episodeTitles: [new(1, "Wrong Story", AbsoluteEpisode: 104), new(2, "Correct Story", AbsoluteEpisode: 57)],
            alternativeWorkTitles: ["Room 104"]);
        Assert.False(plan.Blocked);
        var unit = Assert.Single(plan.Units);
        Assert.Equal(2, unit.Episode);
        Assert.Contains("Primary Series", unit.TargetRelativePath);
        Assert.DoesNotContain("Room", unit.TargetRelativePath);
    }
}
