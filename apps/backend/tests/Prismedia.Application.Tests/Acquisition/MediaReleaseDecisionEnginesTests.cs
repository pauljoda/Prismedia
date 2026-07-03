using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Tests.Acquisition;

/// <summary>
/// Covers the per-kind acquisition seams added for multi-media expansion: Torznab category routing by
/// media kind, and the movie/music decision engines' ranking (resolution/source and codec quality) with
/// the shared acceptance gates.
/// </summary>
public sealed class MediaReleaseDecisionEnginesTests {
    [Fact]
    public void CategoriesNarrowToTheKindRangeAndFallBackToItsTopLevel() {
        // A book-configured indexer (7000s) searching movies falls back to the movie top-level category…
        Assert.Equal([2000], TorznabCategories.ForKind(EntityKind.Movie, [7000, 7030]));
        // …while the user's narrower in-range picks are preserved for the matching kind.
        Assert.Equal([7000, 7030], TorznabCategories.ForKind(EntityKind.Book, [7000, 7030, 2000]).Where(c => c is 7000 or 7030).ToArray());
        Assert.Equal([3000], TorznabCategories.ForKind(EntityKind.AudioLibrary, []));
        Assert.Equal([5000], TorznabCategories.ForKind(EntityKind.VideoSeries, [7000]));
        // Kinds with no mapping keep the configured list unchanged.
        Assert.Equal([7000], TorznabCategories.ForKind(EntityKind.Person, [7000]));
    }

    [Fact]
    public void MovieEngineRanksResolutionThenSourceThenSeeders() {
        var engine = new MovieReleaseDecisionEngine();

        var scored = engine.Evaluate([
            (Release("Dune Part Two 1080p BluRay x264", seeders: 500), null, "Idx"),
            (Release("Dune Part Two 2160p WEB-DL HDR", seeders: 50), null, "Idx"),
            (Release("Dune Part Two 720p HDTV", seeders: 900), null, "Idx"),
        ], BookAcquisitionRules.Default);

        Assert.All(scored, candidate => Assert.True(candidate.Accepted));
        Assert.Equal(
            ["Dune Part Two 2160p WEB-DL HDR", "Dune Part Two 1080p BluRay x264", "Dune Part Two 720p HDTV"],
            scored.Select(candidate => candidate.Release.Title).ToArray());
    }

    [Fact]
    public void TvLadderDrillsSeasonThenCompleteAndEpisodeExactly() {
        var season = new AcquisitionSearchInput(
            Guid.NewGuid(), "Season 1", null, EntityKind.VideoSeason, Series: "Andor", SeasonNumber: 1);
        Assert.Equal(["Andor S01", "Andor Season 1", "Andor complete"], ReleaseQueryLadder.For(season));

        var episode = new AcquisitionSearchInput(
            Guid.NewGuid(), "Pilot", null, EntityKind.Video, Series: "Andor", SeasonNumber: 1, EpisodeNumber: 5);
        Assert.Equal(["Andor S01E05", "Andor 1x05"], ReleaseQueryLadder.For(episode));

        // A plain video with no unit context keeps the movie-style year ladder.
        var video = new AcquisitionSearchInput(Guid.NewGuid(), "Some Video", null, EntityKind.Video, Year: 2020);
        Assert.Equal(["Some Video 2020", "Some Video"], ReleaseQueryLadder.For(video));
    }

    [Fact]
    public void SeasonPackSearchRejectsWrongUnitsAndAcceptsCompletePacks() {
        var engine = new TvReleaseDecisionEngine(EntityKind.VideoSeason);
        var rules = BookAcquisitionRules.Default with { SeasonNumber = 1 };

        var scored = engine.Evaluate([
            (Release("Andor S01 1080p WEB-DL", seeders: 100), null, "Idx"),
            (Release("Andor Season 1 720p", seeders: 100), null, "Idx"),
            (Release("Andor COMPLETE 1080p", seeders: 100), null, "Idx"),
            (Release("Andor 1080p WEB-DL", seeders: 100), null, "Idx"),
            (Release("Andor S02 1080p WEB-DL", seeders: 100), null, "Idx"),
            (Release("Andor S01E05 1080p WEB-DL", seeders: 100), null, "Idx"),
        ], rules);

        var verdicts = scored.ToDictionary(candidate => candidate.Release.Title, candidate => candidate.Accepted);
        Assert.True(verdicts["Andor S01 1080p WEB-DL"]);
        Assert.True(verdicts["Andor Season 1 720p"]);
        Assert.True(verdicts["Andor COMPLETE 1080p"]);
        // A marker-less pack passes (judged by the query match), like format-anonymous book titles.
        Assert.True(verdicts["Andor 1080p WEB-DL"]);
        Assert.False(verdicts["Andor S02 1080p WEB-DL"]);
        Assert.False(verdicts["Andor S01E05 1080p WEB-DL"]);
        Assert.Contains(ReleaseRejectionReason.WrongTvUnit, scored.Single(c => c.Release.Title.Contains("S01E05")).Rejections);
    }

    [Fact]
    public void EpisodeSearchAcceptsOnlyTheExactUnit() {
        var engine = new TvReleaseDecisionEngine(EntityKind.Video);
        var rules = BookAcquisitionRules.Default with { SeasonNumber = 1, EpisodeNumber = 5 };

        var scored = engine.Evaluate([
            (Release("Andor S01E05 1080p WEB-DL", seeders: 10), null, "Idx"),
            (Release("Andor 1x05 720p", seeders: 10), null, "Idx"),
            (Release("Andor S01E06 1080p", seeders: 10), null, "Idx"),
            (Release("Andor S01 1080p pack", seeders: 10), null, "Idx"),
        ], rules);

        var verdicts = scored.ToDictionary(candidate => candidate.Release.Title, candidate => candidate.Accepted);
        Assert.True(verdicts["Andor S01E05 1080p WEB-DL"]);
        Assert.True(verdicts["Andor 1x05 720p"]);
        Assert.False(verdicts["Andor S01E06 1080p"]);
        Assert.False(verdicts["Andor S01 1080p pack"]);
    }

    [Fact]
    public void MusicEngineRanksLosslessAboveHighBitrateLossy() {
        var engine = new MusicReleaseDecisionEngine();

        var scored = engine.Evaluate([
            (Release("Daft Punk - Discovery (2001) MP3 320", seeders: 800), null, "Idx"),
            (Release("Daft Punk - Discovery (2001) FLAC", seeders: 100), null, "Idx"),
        ], BookAcquisitionRules.Default);

        Assert.Equal("Daft Punk - Discovery (2001) FLAC", scored[0].Release.Title);
    }

    [Fact]
    public void SharedGatesStillApplyToTheNewEngines() {
        var engine = new MovieReleaseDecisionEngine();
        var rules = BookAcquisitionRules.Default with { MinSeeders = 5, IgnoredTerms = ["cam"] };

        var scored = engine.Evaluate([
            (Release("Movie 1080p CAM", seeders: 100), null, "Idx"),
            (Release("Movie 1080p BluRay", seeders: 1), null, "Idx"),
            (Release("Movie 1080p BluRay", seeders: 100), null, "Idx"),
        ], rules);

        Assert.Single(scored, candidate => candidate.Accepted);
        Assert.Contains(scored, candidate => candidate.Rejections.Contains(ReleaseRejectionReason.HasIgnoredTerm));
        Assert.Contains(scored, candidate => candidate.Rejections.Contains(ReleaseRejectionReason.BelowMinSeeders));
    }

    [Fact]
    public void BlocklistGateAppliesToTheNewEngines() {
        var engine = new MusicReleaseDecisionEngine();
        var release = Release("Album FLAC", seeders: 10);
        var identity = ReleaseIdentity.For(release.InfoHash, "Idx", release.Title);

        var scored = engine.Evaluate([(release, null, "Idx")], BookAcquisitionRules.Default, new HashSet<string> { identity });

        Assert.False(scored[0].Accepted);
        Assert.Contains(ReleaseRejectionReason.Blocklisted, scored[0].Rejections);
    }

    private static IndexerRelease Release(string title, int seeders) =>
        new(title, SizeBytes: 1_000_000_000, Seeders: seeders, Peers: seeders, DownloadProtocol.Torrent,
            DownloadUrl: "http://dl", MagnetUrl: null, InfoHash: null, InfoUrl: null, Language: null, PublishedAt: null);
}
