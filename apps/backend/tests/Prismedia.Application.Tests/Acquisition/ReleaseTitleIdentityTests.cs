using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Tests.Acquisition;

/// <summary>
/// Covers the release title-identity gates that keep automatic selection from grabbing the wrong work:
/// the digit-preserving title comparison, the title-adjacent year extraction, and the decision-engine
/// integration. Regression anchor: the "Clifford the Big Red Dog" (2000) season search that auto-grabbed
/// the 2019 reboot's 1080p pack because nothing compared years and quality outranked everything.
/// </summary>
public sealed class ReleaseTitleIdentityTests {
    [Theory]
    // Exact titles, with unit/quality tails ending the title at a recognized boundary.
    [InlineData("Clifford the Big Red Dog", "Clifford.the.Big.Red.Dog.S02.1080p.PCOK.WEB-DL.AAC.2.0.H.264-Suki", true, null)]
    [InlineData("Clifford the Big Red Dog", "Clifford the Big Red Dog (S01)(Complete)(2000)(H264)(480p)(WebDl)(Multi 14 Lang)", true, null)]
    // The scene year disambiguator right after the title is captured for the year gate.
    [InlineData("Clifford the Big Red Dog", "Clifford.the.Big.Red.Dog.2019.S01.1080p.AMZN.WEB-DL.DDP5.1.H.264-LAZY", true, 2019)]
    [InlineData("Clifford the Big Red Dog", "Clifford.The.Big.Red.Dog.2019.S01.COMPLETE.720p.AMZN.WEBRip.x264-GalaxyTV[TGx]", true, 2019)]
    [InlineData("Dune", "Dune.2021.2160p.WEB-DL.DDP5.1.Atmos.DV.HDR.H.265", true, 2021)]
    // A year that is PART of the title is consumed as title, and the following year is the disambiguator.
    [InlineData("Blade Runner 2049", "Blade.Runner.2049.2017.1080p.BluRay.x264", true, 2017)]
    [InlineData("Blade Runner 2049", "Blade.Runner.2049.1080p.BluRay.x264", true, null)]
    [InlineData("1917", "1917.2019.1080p.BluRay.x264", true, 2019)]
    [InlineData("2001: A Space Odyssey", "2001.A.Space.Odyssey.1968.2160p.UHD.BluRay.x265", true, 1968)]
    // Articles, "&", and diacritics normalize away on both sides.
    [InlineData("Tom & Jerry", "Tom.and.Jerry.2021.1080p.HMAX.WEB-DL", true, 2021)]
    [InlineData("Amélie", "Amelie.2001.1080p.BluRay.x264", true, 2001)]
    [InlineData("The Office", "Office.S01.1080p.WEB-DL", true, null)]
    // Longer titles sharing a prefix are DIFFERENT works, not loose matches.
    [InlineData("Dune", "Dune.Part.Two.2024.1080p.WEB-DL", false, null)]
    [InlineData("Clifford the Big Red Dog", "Cliffords.Puppy.Days.S01.480p.WEB-DL", false, null)]
    [InlineData("Clifford the Big Red Dog", "Clifford.the.Big.Red.Dog.Movie.2004.1080p.WEB-DL", false, null)]
    // Entirely different or truncated titles never match.
    [InlineData("Clifford the Big Red Dog", "Clifford.1994.720p.BluRay.x264", false, null)]
    [InlineData("Clifford the Big Red Dog", "[02/13] - \"Clifford.The.Big.Red.Dog.S01E06.The.Little.Red.Dream\"", false, null)]
    // No target disables the gate.
    [InlineData("", "Anything.At.All.2020.1080p", true, null)]
    public void MatchComparesLeadingTitleTokensAndExtractsTheAdjacentYear(
        string target, string release, bool expectedMatch, int? expectedYear) {
        var result = ReleaseTitleIdentity.Match(release, target);
        Assert.Equal(expectedMatch, result.TitleMatched);
        Assert.Equal(expectedYear, result.TitleYear);
    }

    [Fact]
    public void SeasonSearchRejectsTheRebootAndPicksTheOriginalDespiteLowerQuality() {
        // The live Clifford incident, replayed: the 2000 series' Season 1 search saw the 2019 reboot's
        // 1080p packs (higher quality) alongside the original's 480p pack. The year gate must reject
        // every reboot release so the correct 480p original wins the auto-pick.
        var engine = new TvReleaseDecisionEngine(EntityKind.VideoSeason);
        var rules = BookAcquisitionRules.Default with {
            Kind = EntityKind.VideoSeason,
            TargetTitle = "Clifford the Big Red Dog",
            TargetYear = 2000,
            SeasonNumber = 1
        };

        var scored = engine.Evaluate([
            (Release("Clifford.the.Big.Red.Dog.2019.S01.1080p.AMZN.WEB-DL.DDP5.1.H.264-LAZY", seeders: 50), null, "Idx"),
            (Release("Clifford.The.Big.Red.Dog.2019.S01.COMPLETE.720p.AMZN.WEBRip.x264-GalaxyTV[TGx]", seeders: 37), null, "Idx"),
            (Release("Clifford the Big Red Dog (S01)(Complete)(2000)(H264)(480p)(WebDl)(Multi 14 Lang)", seeders: 15), null, "Idx"),
        ], rules);

        var accepted = scored.Where(candidate => candidate.Accepted).ToArray();
        var rejected = scored.Where(candidate => !candidate.Accepted).ToArray();

        Assert.Single(accepted);
        Assert.Contains("(2000)", accepted[0].Release.Title);
        Assert.Equal(2, rejected.Length);
        Assert.All(rejected, candidate => Assert.Contains(ReleaseRejectionReason.WrongYear, candidate.Rejections));
    }

    [Fact]
    public void MovieSearchRejectsRemakeYearsAndSequelTitles() {
        var engine = new MovieReleaseDecisionEngine();
        var rules = BookAcquisitionRules.Default with {
            Kind = EntityKind.Movie,
            TargetTitle = "Dune",
            TargetYear = 1984
        };

        var scored = engine.Evaluate([
            (Release("Dune.1984.1080p.BluRay.x264", seeders: 20), null, "Idx"),
            (Release("Dune.2021.2160p.WEB-DL.DDP5.1.Atmos", seeders: 900), null, "Idx"),
            (Release("Dune.Part.Two.2024.1080p.WEB-DL", seeders: 500), null, "Idx"),
        ], rules);

        var verdicts = scored.ToDictionary(candidate => candidate.Release.Title, candidate => candidate);
        Assert.True(verdicts["Dune.1984.1080p.BluRay.x264"].Accepted);
        Assert.Contains(ReleaseRejectionReason.WrongYear, verdicts["Dune.2021.2160p.WEB-DL.DDP5.1.Atmos"].Rejections);
        Assert.Contains(ReleaseRejectionReason.TitleMismatch, verdicts["Dune.Part.Two.2024.1080p.WEB-DL"].Rejections);
    }

    [Fact]
    public void YearToleranceAbsorbsRegionalOffsetsAndMissingYearsPass() {
        var engine = new MovieReleaseDecisionEngine();
        var rules = BookAcquisitionRules.Default with {
            Kind = EntityKind.Movie,
            TargetTitle = "Some Film",
            TargetYear = 2020
        };

        var scored = engine.Evaluate([
            (Release("Some.Film.2021.1080p.BluRay.x264", seeders: 5), null, "Idx"),
            (Release("Some.Film.1080p.BluRay.x264", seeders: 5), null, "Idx"),
        ], rules);

        Assert.All(scored, candidate => Assert.True(candidate.Accepted));
    }

    [Fact]
    public void GatesAreNoOpsWithoutATargetTitleOrYear() {
        // Ad-hoc evaluations (no wanted entity, no target) keep the historical permissive behavior.
        var engine = new MovieReleaseDecisionEngine();
        var scored = engine.Evaluate(
            [(Release("Completely.Unrelated.2020.1080p", seeders: 5), null, "Idx")],
            BookAcquisitionRules.Default);
        Assert.True(scored[0].Accepted);
    }

    private static IndexerRelease Release(string title, int seeders) =>
        new(title, SizeBytes: 1_000_000_000, Seeders: seeders, Peers: seeders, DownloadProtocol.Torrent,
            DownloadUrl: "http://dl", MagnetUrl: null, InfoHash: null, InfoUrl: null, Language: null, PublishedAt: null);
}
