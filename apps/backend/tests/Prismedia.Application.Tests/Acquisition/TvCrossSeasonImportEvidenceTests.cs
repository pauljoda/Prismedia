using Prismedia.Application.Acquisition;

namespace Prismedia.Application.Tests.Acquisition;

public sealed class TvCrossSeasonImportEvidenceTests {
    [Theory]
    [InlineData("Show.S01E49-E50.Hidden.Garden.&.Mountain.Journey.mkv")]
    [InlineData("Show.S02E03-E04.Hidden.Garden.&.Mountain.Journey.mkv")]
    [InlineData("Hidden.Garden.&.Mountain.Journey.mkv")]
    public void UniqueCompleteTitlesIdentifyACombinedForeignFile(string filename) {
        var destination = Season(2, (3, "Hidden Garden"), (4, "Mountain Journey"));

        var match = Assert.Single(TvCrossSeasonImportEvidence.Find([new(filename, 100)], 1, [destination]));

        Assert.Equal(destination.SeasonEntityId, match.Destination!.SeasonEntityId);
        Assert.Equal([3, 4], match.Episodes.Select(episode => episode.Episode));
    }

    [Fact]
    public void OneConflictingHalfCannotReidentifyAnEntireCombinedFile() {
        var match = Find("Show.S01E49-E50.Hidden.Garden.&.Missing.Story.mkv", Season(2, (3, "Hidden Garden")));

        Assert.Null(match.Destination);
        Assert.Empty(match.Episodes);
    }

    [Fact]
    public void RepeatedTitlesAcrossSeasonsRemainAmbiguous() {
        var match = Find("Show.S01E49.Hidden.Garden.mkv", Season(2, (3, "Hidden Garden")), Season(3, (4, "Hidden Garden")));

        Assert.Null(match.Destination);
    }

    [Theory]
    [InlineData("Show.S01E01.Hidden.Garden.mkv")]
    [InlineData("Show.S01E01-E02.Hidden.Garden.&.Mountain.Journey.mkv")]
    public void MatchingRequestedNumbersAndCompleteTitlesAreNotInvalidatedByForeignRepeats(string filename) {
        var requested = Season(1, (1, "Hidden Garden"), (2, "Mountain Journey"));
        var repeat = Season(2, (3, "Hidden Garden"));

        Assert.Empty(TvCrossSeasonImportEvidence.Find([new(filename, 100)], 1, [requested, repeat], "Show"));
    }

    [Fact]
    public void ACompleteNumberedPairCanIncludeASingleWordTitle() {
        var requested = Season(1, (1, "Puzzlewood"), (2, "Mountain Journey"));
        var repeat = Season(2, (3, "Mountain Journey"));

        Assert.Empty(TvCrossSeasonImportEvidence.Find(
            [new("Show.S01E01-E02.Puzzlewood.&.Mountain.Journey.mkv", 100)], 1, [requested, repeat], "Show"));
    }

    [Fact]
    public void CompleteRequestedTitlesDoNotHideAnAdditionalForeignTitle() {
        var requested = Season(1, (1, "Hidden Garden"), (2, "Mountain Journey"));
        var foreign = Season(2, (3, "Hidden Garden"), (4, "Ocean Adventure"));

        var match = Find("Show.S01E01-E02.Hidden.Garden.&.Mountain.Journey.&.Ocean.Adventure.mkv", requested, foreign);

        Assert.Null(match.Destination);
    }

    [Fact]
    public void RepeatedTitlesDoNotResolveConflictingRequestedNumbers() {
        var requested = Season(1, (1, "Hidden Garden"), (2, "Mountain Journey"));
        var repeat = Season(2, (3, "Hidden Garden"), (4, "Mountain Journey"));

        var match = Find("Show.S01E49-E50.Hidden.Garden.&.Mountain.Journey.mkv", requested, repeat);

        Assert.Null(match.Destination);
    }

    [Fact]
    public void AShorterTitleInsideAnotherDoesNotProveTwoCoveredEpisodes() {
        var match = Find("Show.S01E49-E50.Long.Mountain.Journey.mkv",
            Season(2, (3, "Mountain Journey"), (4, "Long Mountain Journey")));

        Assert.Null(match.Destination);
    }

    [Theory]
    [InlineData("Pilot")]
    [InlineData("Episode 49")]
    public void GenericOrSingleWordTitlesCannotOverrideASeason(string title) {
        Assert.Empty(TvCrossSeasonImportEvidence.Find([new($"Show.S01E49.{title}.mkv", 100)], 1, [Season(2, (3, title))]));
    }

    [Fact]
    public void ExplicitForeignNumberingRequiresEverySlotInTheCatalog() {
        var complete = Find("Show.S02E03-E04.mkv", Season(2, (3, "First Story"), (4, "Second Story")));
        var incomplete = Find("Show.S02E03-E04.mkv", Season(2, (3, "First Story")));

        Assert.NotNull(complete.Destination);
        Assert.Equal([3, 4], complete.Episodes.Select(episode => episode.Episode));
        Assert.Null(incomplete.Destination);
    }

    [Fact]
    public void ArchiveDerivativesDoNotCompeteWithTheirOriginalForForeignEpisodeSlots() {
        var match = Assert.Single(TvCrossSeasonImportEvidence.Find([
            new("Show.S02E03.Hidden.Garden.mkv", 100),
            new("Show.S02E03.Hidden.Garden.ia.mkv", 50)
        ], 1, [Season(2, (3, "Hidden Garden"))], "Show"));

        Assert.Equal("Show.S02E03.Hidden.Garden.mkv", match.SourceRelativePath);
    }

    [Fact]
    public void TheSeriesNameIsNotEpisodeTitleEvidenceForANumberedFileWithoutATitle() {
        Assert.Empty(TvCrossSeasonImportEvidence.Find([new("Hidden.Garden.S01E02.mkv", 100)], 1,
            [Season(2, (3, "Hidden Garden"))], "Hidden Garden"));
    }

    [Fact]
    public void MatchingEpisodeTitlesCannotOverrideAnExplicitlyDifferentSeries() {
        var match = Assert.Single(TvCrossSeasonImportEvidence.Find(
            [new("Different.Show.S02E03.Hidden.Garden.mkv", 100)], 1, [Season(2, (3, "Hidden Garden"))], "Show"));

        Assert.Null(match.Destination);
    }

    [Fact]
    public void SpecialsRemainForReviewUntilTheirPlacementProtocolSupportsThem() {
        var match = Find("Show.S00E03.Hidden.Garden.mkv", Season(0, (3, "Hidden Garden")));

        Assert.Null(match.Destination);
    }

    [Fact]
    public void DuplicateSeasonNumbersDoNotChooseAnArbitraryIdentity() {
        var match = Find("Show.S02E03.mkv", Season(2, (3, "First Story")), Season(2, (3, "Second Story")));

        Assert.Null(match.Destination);
    }

    [Fact]
    public void ARequestedSeasonTitleCannotBeIgnoredWhenForeignNumbersContradictIt() {
        var match = Find("Show.S02E03.Hidden.Garden.mkv", Season(1, (2, "Hidden Garden")), Season(2, (3, "Another Story")));

        Assert.Null(match.Destination);
    }

    private static TvCrossSeasonFileEvidence Find(string filename, params TvSeasonEpisodeCatalog[] catalog) =>
        Assert.Single(TvCrossSeasonImportEvidence.Find([new(filename, 100)], 1, catalog));

    private static TvSeasonEpisodeCatalog Season(int number, params (int Episode, string Title)[] episodes) =>
        new(Guid.NewGuid(), number, episodes.Select(episode => new TvEpisodeTitle(episode.Episode, episode.Title, Guid.NewGuid())).ToArray());
}
