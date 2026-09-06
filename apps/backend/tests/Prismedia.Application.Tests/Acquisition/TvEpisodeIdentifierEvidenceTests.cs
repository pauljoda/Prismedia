using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Tests.Acquisition;

/// <summary>Generic catalog labels cannot establish an episode's place in another numbering system.</summary>
public sealed class TvEpisodeIdentifierEvidenceTests {
    [Theory]
    [InlineData(null)]
    [InlineData(83)]
    public void SeasonRelativePlaceholderDoesNotAuthorizeAnAbsoluteRelease(int? absolute) {
        var rules = BookAcquisitionRules.Default with {
            TargetTitle = "Example Show", SeasonNumber = 2, EpisodeNumber = 1,
            TargetEpisodeTitle = "Episode 1", TargetAbsoluteEpisodeNumber = absolute
        };
        var candidates = new TvReleaseDecisionEngine(EntityKind.VideoEpisode).Evaluate([
            (Release("Example Show 1 1080p"), null, "Indexer"),
            (Release("Example Show Episode 1 1080p"), null, "Indexer"),
            (Release("Example Show S02E01 1080p"), null, "Indexer"),
            (Release("Example Show 83 1080p"), null, "Indexer")
        ], rules).ToDictionary(candidate => candidate.Release.Title, candidate => candidate.Accepted);

        Assert.False(candidates["Example Show 1 1080p"]);
        Assert.False(candidates["Example Show Episode 1 1080p"]);
        Assert.True(candidates["Example Show S02E01 1080p"]);
        Assert.Equal(absolute == 83, candidates["Example Show 83 1080p"]);
    }

    [Theory]
    [InlineData("Example Show - 1.mkv")]
    [InlineData("Example Show - Episode 1.mkv")]
    public void TokenlessFilesCannotMapFromAnUnverifiedPlaceholder(string filename) {
        Assert.Null(TvImportPlanBuilder.InferEpisode(filename, 2, [new(1, "Episode 1")]));
    }

    [Fact]
    public void GenericTitleCannotOverrideStructuredEpisodeNumbers() {
        var unit = TvImportPlanBuilder.InferEpisode("Example Show S02E02 Episode 1.mkv", 2,
            [new(1, "Episode 1"), new(2, "Episode 2")]);

        Assert.Equal((2, 2), unit);
    }

    [Fact]
    public void VerifiedAbsolutePositionStillMapsAGenericEpisode() {
        var unit = TvImportPlanBuilder.InferEpisode("Example Show - 83.mkv", 2,
            [new(1, "Episode 1", AbsoluteEpisode: 83)]);

        Assert.Equal((2, 1), unit);
    }

    private static IndexerRelease Release(string title) =>
        new(title, 500_000_000, 10, 2, DownloadProtocol.Torrent, "http://download.test/release", null, null, null, null, null);
}
