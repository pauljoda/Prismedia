using Prismedia.Application.Acquisition;

namespace Prismedia.Application.Tests.Acquisition;

public sealed class TvOwnedEpisodeCoveragePlannerTests {
    private readonly Guid seasonId = Guid.NewGuid();
    private readonly Guid firstId = Guid.NewGuid();
    private readonly Guid secondId = Guid.NewGuid();

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
