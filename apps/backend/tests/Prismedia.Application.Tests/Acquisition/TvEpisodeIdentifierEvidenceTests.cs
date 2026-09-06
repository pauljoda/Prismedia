using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Tests.Acquisition;

/// <summary>Generic catalog labels cannot establish an episode's place in another numbering system.</summary>
public sealed class TvEpisodeIdentifierEvidenceTests {
    [Theory]
    [InlineData("S02E01", "S02E02")]
    [InlineData("2x01", "2x02")]
    [InlineData("Season 2 Episode 1", "Season 2 Episode 2")]
    public void StructuredCatalogPlaceholdersCannotRealignAnotherEpisode(string firstTitle, string secondTitle) {
        var titles = new TvEpisodeTitle[] { new(1, firstTitle), new(2, secondTitle) };
        var file = new ImportCandidateFile($"Example Show S02E02 {firstTitle}.mkv", 1000);
        var plan = TvImportPlanBuilder.PlanUnits([file], "Example Show", 2, null, episodeTitles: titles);

        Assert.False(plan.Blocked);
        Assert.Equal(2, Assert.Single(plan.Units).Episode);
        Assert.True(TvEpisodeIdentifiers.IsGenericTitle(firstTitle));
        Assert.Null(TvEpisodeIdentifiers.Create(firstTitle, 1).ProviderTitle);
    }

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
    public void FinalPackRealignmentCannotReintroduceUnverifiedGenericTitleEvidence() {
        var plan = TvImportPlanBuilder.PlanUnits([new("Example Show S02E02 Episode 1.mkv", 1000)],
            "Example Show", 2, null, episodeTitles: [new(1, "Episode 1"), new(2, "Episode 2")]);

        Assert.False(plan.Blocked);
        Assert.Equal(2, Assert.Single(plan.Units).Episode);
    }

    [Fact]
    public void VerifiedAbsolutePositionStillMapsAGenericEpisode() {
        var unit = TvImportPlanBuilder.InferEpisode("Example Show - 83.mkv", 2,
            [new(1, "Episode 1", AbsoluteEpisode: 83)]);

        Assert.Equal((2, 1), unit);
    }

    [Theory]
    [InlineData("Example Show 1080p H.264", 264)]
    [InlineData("Example Show 720p AAC 2.0", 2)]
    [InlineData("Example Show 720p DDP 5.1", 5)]
    [InlineData("Example Show 720p 10 bit", 10)]
    [InlineData("Example Show 720p 500 MB", 500)]
    public void TechnicalNumbersCannotEstablishAbsoluteEpisodeIdentity(string filename, int absolute) {
        var identifiers = TvEpisodeIdentifiers.Create("An Unrelated Story", absolute);

        Assert.False(identifiers.Matches(filename));
        Assert.False(identifiers.MatchesNumeric(filename));
        Assert.Null(TvImportPlanBuilder.InferEpisode(filename + ".mkv", 2,
            [new(1, "An Unrelated Story", AbsoluteEpisode: absolute)]));
    }

    [Theory]
    [InlineData("Example Show - 500v2 [1080p]", 500)]
    [InlineData("Example Show - 0264 [H.264]", 264)]
    [InlineData("Example Show - 2 [AAC 2.0]", 2)]
    public void EpisodeNumbersRemainUsableAlongsideTechnicalMetadataAndRevisions(string filename, int absolute) {
        Assert.True(TvEpisodeIdentifiers.Create("An Unrelated Story", absolute).MatchesNumeric(filename));
        var candidate = new TvReleaseDecisionEngine(EntityKind.VideoEpisode).Evaluate(
            [(Release(filename), null, "Indexer")], BookAcquisitionRules.Default with {
                Kind = EntityKind.VideoEpisode, TargetTitle = "Example Show", SeasonNumber = 2, EpisodeNumber = 1,
                TargetEpisodeTitle = "An Unrelated Story", TargetAbsoluteEpisodeNumber = absolute
            }).Single();
        Assert.True(candidate.Accepted, string.Join(", ", candidate.Rejections));
    }

    [Theory]
    [InlineData(ProperDownloadPolicy.PreferAndUpgrade, true)]
    [InlineData(ProperDownloadPolicy.DoNotUpgrade, false)]
    [InlineData(ProperDownloadPolicy.DoNotPrefer, false)]
    public void AttachedEpisodeRevisionsRespectTheProfileUpgradePolicy(ProperDownloadPolicy policy, bool accepted) {
        var candidate = new TvReleaseDecisionEngine(EntityKind.VideoEpisode).Evaluate(
            [(Release("Example Show - 500v2 [1080p WEB-DL]"), null, "Indexer")], BookAcquisitionRules.Default with {
                Kind = EntityKind.VideoEpisode, TargetTitle = "Example Show", SeasonNumber = 20, EpisodeNumber = 500,
                TargetEpisodeTitle = "The Final Message", TargetAbsoluteEpisodeNumber = 500,
                IsUpgradeSearch = true, OwnedMediaQuality = VideoQuality.Webdl1080p.ToCode(), OwnedMediaRevision = 1,
                ProperPolicy = policy
            }).Single();

        Assert.Equal(accepted, candidate.Accepted);
        if (!accepted) Assert.Contains(ReleaseRejectionReason.NotAnUpgrade, candidate.Rejections);
    }

    private static IndexerRelease Release(string title) =>
        new(title, 500_000_000, 10, 2, DownloadProtocol.Torrent, "http://download.test/release", null, null, null, null, null);
}
