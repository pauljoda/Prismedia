using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Tests.Acquisition;

/// <summary>
/// Covers the download payload validation that catches a wrong grab from its actual file list (the
/// torrent metadata names every file long before the bytes arrive), plus the multi-episode release
/// token parsing it leans on. Evidence-based: only positive contradictions fail a payload — terse
/// inner names and missing markers never do.
/// </summary>
public sealed class AcquisitionPayloadValidationTests {
    [Fact]
    public void MultiEpisodeTokensParseListsAndRanges() {
        Assert.Equal((1, new[] { 41, 42 }),
            Normalize(TvReleaseTokens.ParseEpisodes("Clifford.the.Big.Red.Dog.S01E41E42.GoodBye.TBone.480p.WEB-DL")));
        Assert.Equal((1, new[] { 1, 2, 3 }), Normalize(TvReleaseTokens.ParseEpisodes("Show.S01E01-E03.720p")));
        Assert.Equal((1, new[] { 1, 2, 3 }), Normalize(TvReleaseTokens.ParseEpisodes("Show.S01E01-03.720p")));
        Assert.Equal((2, new[] { 5 }), Normalize(TvReleaseTokens.ParseEpisodes("Show.S02E05.720p")));
        Assert.Equal((3, new[] { 9 }), Normalize(TvReleaseTokens.ParseEpisodes("Show.3x09.720p")));
        Assert.Null(TvReleaseTokens.ParseEpisodes("Show.S01.Complete.720p"));
        // The singular parse keeps its first-episode contract.
        Assert.Equal((1, 41), TvReleaseTokens.ParseEpisode("Show.S01E41E42.720p"));
    }

    [Fact]
    public void CompleteSeasonIsNotMisclassifiedAsACompleteSeries() {
        Assert.False(TvReleaseTokens.NamesCompleteSeries(
            "Sesame.Street.S52.COMPLETE.720p.HMAX.WEBRip.x264-GalaxyTV"));
        Assert.Equal(3, TvReleaseTokens.ParseSeason("Sesame Street - The Complete Third Season"));
        Assert.Equal(3, TvReleaseTokens.ParseSeason("Sesame Street 3rd Season"));
        Assert.Equal(23, TvReleaseTokens.ParseSeason("Sesame Street Twenty-Third Season"));
        Assert.False(TvReleaseTokens.NamesCompleteSeries("Sesame Street - The Complete Third Season"));
        Assert.True(TvReleaseTokens.NamesCompleteSeries("Sesame Street COMPLETE"));
        Assert.True(TvReleaseTokens.NamesCompleteSeries("Sesame Street complete series"));
        Assert.True(TvReleaseTokens.NamesCompleteSeries("Sesame Street all seasons"));
    }

    [Fact]
    public void EpisodeSearchAcceptsAMultiEpisodeReleaseContainingIt() {
        // Live gap: E42's only source was the S01E41E42 double release, which the unit gate rejected.
        var spec = new TvUnitSpecification();
        var rules = BookAcquisitionRules.Default with { SeasonNumber = 1, EpisodeNumber = 42 };
        Assert.Null(spec.Evaluate(Release("Clifford.the.Big.Red.Dog.S01E41E42.480p.WEB-DL"), rules));
        Assert.Equal(ReleaseRejectionReason.WrongTvUnit, spec.Evaluate(Release("Clifford.the.Big.Red.Dog.S01E43E44.480p.WEB-DL"), rules));
    }

    [Fact]
    public void WrongYearInFileNamesIsAConflict() {
        // A year-less release title passed the search gates, but the files inside name the 2019 reboot.
        var conflict = AcquisitionPayloadValidation.FindConflict(
            [
                "Clifford.the.Big.Red.Dog.2019.S01.1080p.WEB-DL/Clifford.the.Big.Red.Dog.2019.S01E01.1080p.mkv",
                "Clifford.the.Big.Red.Dog.2019.S01.1080p.WEB-DL/Clifford.the.Big.Red.Dog.2019.S01E02.1080p.mkv"
            ],
            EntityKind.VideoSeason,
            "Clifford the Big Red Dog",
            expectedYear: 2000,
            seasonNumber: 1);
        Assert.NotNull(conflict);
        Assert.Contains("2019", conflict);
    }

    [Fact]
    public void MatchingOrYearlessPayloadsPass() {
        // Correct year in the payload names.
        Assert.Null(AcquisitionPayloadValidation.FindConflict(
            ["Clifford the Big Red Dog (2000)/Clifford.the.Big.Red.Dog.S01E01.480p.mkv"],
            EntityKind.VideoSeason, "Clifford the Big Red Dog", 2000, seasonNumber: 1));
        // Terse inner names carry no evidence — never a conflict.
        Assert.Null(AcquisitionPayloadValidation.FindConflict(
            ["Some.Release.Folder/01 - Pilot.mkv", "Some.Release.Folder/02 - The Dinner.mkv"],
            EntityKind.VideoSeason, "Clifford the Big Red Dog", 2000, seasonNumber: 1));
        // Movies: the primary file naming the right year passes.
        Assert.Null(AcquisitionPayloadValidation.FindConflict(
            ["Dune.1984.1080p.BluRay/Dune.1984.1080p.BluRay.mkv"],
            EntityKind.Movie, "Dune", 1984));
    }

    [Fact]
    public void MoviePayloadNamingAnotherYearIsAConflict() {
        var conflict = AcquisitionPayloadValidation.FindConflict(
            ["Dune.2021.2160p.WEB-DL/Dune.2021.2160p.WEB-DL.mkv"],
            EntityKind.Movie, "Dune", 1984);
        Assert.NotNull(conflict);
        Assert.Contains("2021", conflict);
    }

    [Fact]
    public void UnitMarkersExcludingTheSoughtSeasonAreAConflict() {
        // A "Season 1" grab whose video files are all Season 3.
        var conflict = AcquisitionPayloadValidation.FindConflict(
            ["Show.S03.Pack/Show.S03E01.mkv", "Show.S03.Pack/Show.S03E02.mkv"],
            EntityKind.VideoSeason, "Show", expectedYear: null, seasonNumber: 1);
        Assert.NotNull(conflict);
        Assert.Contains("S01", conflict);
    }

    [Fact]
    public void SpecialsAndCompleteSeriesPacksAreNotContraryEvidence() {
        // Specials (S00) ride along in legitimate season packs.
        Assert.Null(AcquisitionPayloadValidation.FindConflict(
            ["Show.S01.Pack/Show.S01E01.mkv", "Show.S01.Pack/Show.S00E01.Special.mkv"],
            EntityKind.VideoSeason, "Show", null, seasonNumber: 1));
        // A complete-series selection legitimately spans other seasons.
        Assert.Null(AcquisitionPayloadValidation.FindConflict(
            ["Show.Complete/Show.S02E01.mkv", "Show.Complete/Show.S01E01.mkv"],
            EntityKind.VideoSeason, "Show", null, seasonNumber: 1, completeSeriesSelected: true));
    }

    [Fact]
    public void EpisodePayloadMissingTheSoughtEpisodeIsAConflict() {
        var conflict = AcquisitionPayloadValidation.FindConflict(
            ["Show.S01E05.720p/Show.S01E05.720p.mkv"],
            EntityKind.VideoEpisode, "Show", null, seasonNumber: 1, episodeNumber: 7);
        Assert.NotNull(conflict);
        // A double-episode file containing the sought half passes.
        Assert.Null(AcquisitionPayloadValidation.FindConflict(
            ["Show.S01E41E42/Show.S01E41E42.480p.mkv"],
            EntityKind.VideoEpisode, "Show", null, seasonNumber: 1, episodeNumber: 42));
    }

    [Fact]
    public void NonVideoKindsAndEmptyPayloadsAreNeverValidated() {
        Assert.Null(AcquisitionPayloadValidation.FindConflict(
            ["Author - Other Book (2019)/book.epub"], EntityKind.Book, "Some Book", 2000));
        Assert.Null(AcquisitionPayloadValidation.FindConflict(
            [], EntityKind.Movie, "Dune", 1984));
    }

    private static (int Season, int[] Episodes)? Normalize((int Season, IReadOnlyList<int> Episodes)? unit) =>
        unit is { } value ? (value.Season, value.Episodes.ToArray()) : null;

    private static IndexerRelease Release(string title) =>
        new(title, SizeBytes: 1_000_000_000, Seeders: 5, Peers: 5, DownloadProtocol.Torrent,
            DownloadUrl: "http://dl", MagnetUrl: null, InfoHash: null, InfoUrl: null, Language: null, PublishedAt: null);
}
