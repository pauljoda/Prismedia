using Prismedia.Application.Acquisition;

namespace Prismedia.Application.Tests.Acquisition;

public sealed class TvOwnedEpisodeCoveragePlannerTests {
    private readonly Guid seasonId = Guid.NewGuid();
    private readonly Guid firstId = Guid.NewGuid();
    private readonly Guid secondId = Guid.NewGuid();

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void CompleteDistinctTitlesCanReplaceOneWrongOwnerWithWantedCanonicalEpisodes(int destinationSeason) {
        var catalog = Catalog().Select(season => season with {
            SeasonNumber = destinationSeason, Episodes = season.Episodes.Select(episode => episode with { IsWanted = true }).ToArray()
        }).ToArray();
        var plan = TvOwnedEpisodeCoveragePlanner.PlanReassignment(
            "Show.S01E49.Hidden.Garden.&.Mountain.Journey.mkv", "Show", 1, catalog, [Guid.NewGuid()]);

        Assert.NotNull(plan);
        Assert.Equal(seasonId, plan.SeasonEntityId);
        Assert.Equal(destinationSeason, plan.SeasonNumber);
        Assert.Equal(new[] { firstId, secondId }, plan.Episodes.Select(episode => episode.EntityId!.Value));
    }

    [Theory]
    [InlineData("Show.S01E01-E02.mkv")]
    [InlineData("Show.S01E01-E02.Hidden.Garden.mkv")]
    [InlineData("Show.Sequel.S01E01-E02.Hidden.Garden.&.Mountain.Journey.mkv")]
    public void ReassignmentRequiresCompleteWorkAndEpisodeTitleEvidence(string name) {
        var catalog = Catalog().Select(season => season with {
            Episodes = season.Episodes.Select(episode => episode with { IsWanted = true }).ToArray()
        }).ToArray();
        Assert.Null(TvOwnedEpisodeCoveragePlanner.PlanReassignment(name, "Show", 1, catalog, [Guid.NewGuid()]));
    }

    [Fact]
    public void ReassignmentCannotReplaceAnAlreadyOwnedOrPartlyCorrectMapping() {
        const string name = "Show.S01E01-E02.Hidden.Garden.&.Mountain.Journey.mkv";
        Assert.Null(TvOwnedEpisodeCoveragePlanner.PlanReassignment(name, "Show", 1, Catalog(), [Guid.NewGuid()]));
        var catalog = Catalog().Select(season => season with {
            Episodes = season.Episodes.Select(episode => episode with { IsWanted = true }).ToArray()
        }).ToArray();
        Assert.Null(TvOwnedEpisodeCoveragePlanner.PlanReassignment(name, "Show", 1, catalog, [firstId]));
        Assert.Null(TvOwnedEpisodeCoveragePlanner.PlanReassignment(name, "Show", 1, catalog, [Guid.NewGuid(), Guid.NewGuid()]));
    }

    [Fact]
    public void ATitleOnlyPairCanHealTheMissingOwnerButASequelCannot() {
        var plan = TvOwnedEpisodeCoveragePlanner.Plan("Show - Hidden Garden & Mountain Journey.mkv", "Show", 1,
            Catalog(), [firstId]);
        Assert.NotNull(plan);
        Assert.Equal(secondId, Assert.Single(plan.MissingEpisodes).EntityId);
        Assert.Null(TvOwnedEpisodeCoveragePlanner.Plan("Show Sequel - Hidden Garden & Mountain Journey.mkv", "Show", 1,
            Catalog(), [firstId]));
    }

    [Fact]
    public void VerifiedAlternativeTitlesCanRepairCoverageWithoutMovingExistingOwners() {
        var plan = TvOwnedEpisodeCoveragePlanner.Plan("Romanized.Show.S01E01-E02.Hidden.Garden.&.Mountain.Journey.mkv",
            "Show", 1, Catalog(), [firstId], ["Romanized Show"]);
        Assert.NotNull(plan);
        Assert.Equal(secondId, Assert.Single(plan.MissingEpisodes).EntityId);
        Assert.Null(TvOwnedEpisodeCoveragePlanner.Plan("Romanized.Show.Sequel.S01E01-E02.Hidden.Garden.&.Mountain.Journey.mkv",
            "Show", 1, Catalog(), [firstId], ["Romanized Show"]));
    }

    [Fact]
    public void PairedTitlesRecoverOnlyTheMissingHalfOfAnAlreadyOwnedFile() {
        var plan = TvOwnedEpisodeCoveragePlanner.Plan("Show.S01E01-E02.Hidden.Garden.&.Mountain.Journey.mkv", "Show", 1,
            Catalog(), [firstId]);

        Assert.NotNull(plan);
        Assert.Equal(seasonId, plan.SeasonEntityId);
        Assert.Equal(1, plan.SeasonNumber);
        Assert.Equal(secondId, Assert.Single(plan.MissingEpisodes).EntityId);
    }

    [Fact]
    public void ConfidentForeignTitlesCanRestoreASecondLinkWithoutMovingTheFirstOwner() {
        var catalog = Catalog().Select(season => season with { SeasonNumber = 2 }).ToArray();
        var plan = TvOwnedEpisodeCoveragePlanner.Plan("Show.S01E49-E50.Hidden.Garden.&.Mountain.Journey.mkv", "Show", 1,
            catalog, [firstId]);

        Assert.NotNull(plan);
        Assert.Equal(2, plan.SeasonNumber);
        Assert.Equal(secondId, Assert.Single(plan.MissingEpisodes).EntityId);
    }

    [Fact]
    public void ConflictingExistingOwnersRequireReviewInsteadOfAnAdditiveRepair() =>
        Assert.Null(TvOwnedEpisodeCoveragePlanner.Plan("Show.S01E01-E02.Hidden.Garden.&.Mountain.Journey.mkv", "Show", 1,
            Catalog(), [Guid.NewGuid()]));

    [Theory]
    [InlineData("Show.S01E01-E02.mkv")]
    [InlineData("Show.S01E01-E02.Hidden.Garden.mkv")]
    [InlineData("Another.Show.S01E01-E02.Hidden.Garden.&.Mountain.Journey.mkv")]
    public void NumberingOrIncompleteTitlesCannotProveAdditionalLibraryCoverage(string originalName) =>
        Assert.Null(TvOwnedEpisodeCoveragePlanner.Plan(originalName, "Show", 1, Catalog(), [firstId]));

    [Fact]
    public void AnEpisodeAlreadyOwnedElsewhereIsNotReassigned() =>
        Assert.Null(TvOwnedEpisodeCoveragePlanner.Plan("Show.S01E01-E02.Hidden.Garden.&.Mountain.Journey.mkv", "Show", 1,
            [new(seasonId, 1, [new(1, "Hidden Garden", firstId, IsWanted: false), new(2, "Mountain Journey", secondId, IsWanted: false)])], [firstId]));

    [Fact]
    public void DuplicateCatalogPositionsCannotSelectAnArbitraryMissingEntity() {
        var catalog = Catalog().Select(season => season with { Episodes = [.. season.Episodes, new(2, "Mountain Journey", Guid.NewGuid())] }).ToArray();
        Assert.Null(TvOwnedEpisodeCoveragePlanner.Plan("Show.S01E01-E02.Hidden.Garden.&.Mountain.Journey.mkv", "Show", 1,
            catalog, [firstId]));
    }

    [Fact]
    public void ACompleteSharedFileNeedsNoRepair() =>
        Assert.Null(TvOwnedEpisodeCoveragePlanner.Plan("Show.S01E01-E02.Hidden.Garden.&.Mountain.Journey.mkv", "Show", 1,
            Catalog(), [firstId, secondId]));

    private IReadOnlyList<TvSeasonEpisodeCatalog> Catalog() => [new(seasonId, 1,
        [new(1, "Hidden Garden", firstId, IsWanted: false), new(2, "Mountain Journey", secondId)])];
}
