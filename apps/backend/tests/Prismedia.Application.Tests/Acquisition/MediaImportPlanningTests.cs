using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Tests.Acquisition;

public sealed class MovieImportPlanBuilderTests {
    private static ImportTemplateContext Context(string title = "The Martian", int? year = 2015) =>
        new(title, null, year);

    private static ImportCandidateFile File(string path, long size) => new(path, size);

    [Fact]
    public void PicksTheLargestVideoAndRendersTitleYearLayout() {
        var plan = MovieImportPlanBuilder.Plan([
            File("The.Martian.2015.1080p/movie.mkv", 8_000_000_000),
            File("The.Martian.2015.1080p/extras/featurette.mkv", 300_000_000),
            File("The.Martian.2015.1080p/release.nfo", 5_000)
        ], Context());

        Assert.False(plan.Blocked);
        var item = Assert.Single(plan.Items);
        Assert.Equal("The.Martian.2015.1080p/movie.mkv", item.SourceRelativePath);
        Assert.Equal("The Martian (2015)/The Martian (2015).mkv", item.TargetRelativePath);
    }

    [Fact]
    public void MissingYearUsesBareTitle() {
        var plan = MovieImportPlanBuilder.Plan([File("movie.mp4", 100)], Context(year: null));

        Assert.Equal("The Martian/The Martian.mp4", Assert.Single(plan.Items).TargetRelativePath);
    }

    [Fact]
    public void SampleFilesAreNeverThePrimary() {
        var plan = MovieImportPlanBuilder.Plan([
            File("movie-sample.mkv", 9_000_000_000),
            File("movie.mkv", 8_000_000_000)
        ], Context());

        Assert.Equal("movie.mkv", Assert.Single(plan.Items).SourceRelativePath);
    }

    [Fact]
    public void OnlySampleNamedVideosStillImportTheLargest() {
        var plan = MovieImportPlanBuilder.Plan([File("weird.sample.mkv", 700)], Context());

        Assert.False(plan.Blocked);
        Assert.Equal("weird.sample.mkv", Assert.Single(plan.Items).SourceRelativePath);
    }

    [Fact]
    public void TwoFullSizeVideosBlockAsAmbiguous() {
        var plan = MovieImportPlanBuilder.Plan([
            File("part1.mkv", 4_000_000_000),
            File("part2.mkv", 3_900_000_000)
        ], Context());

        Assert.True(plan.Blocked);
        Assert.Equal(ImportBlockReason.AmbiguousMultiplePrimaries, plan.BlockReason);
    }

    [Fact]
    public void NoVideoFilesBlockAsUnsupported() {
        var plan = MovieImportPlanBuilder.Plan([File("cover.jpg", 100), File("release.nfo", 10)], Context());

        Assert.True(plan.Blocked);
        Assert.Equal(ImportBlockReason.NoSupportedPayload, plan.BlockReason);
    }

    [Fact]
    public void IllegalTitleCharactersAreSanitized() {
        var plan = MovieImportPlanBuilder.Plan([File("m.mkv", 1)], Context(title: "Face/Off: Redux"));

        Assert.Equal("Face Off Redux (2015)/Face Off Redux (2015).mkv", Assert.Single(plan.Items).TargetRelativePath);
    }

    [Fact]
    public void CustomTemplateFlowsThroughToTheTargetPathAndHintFolder() {
        var context = Context();
        var plan = MovieImportPlanBuilder.Plan(
            [File("The.Martian.2015.1080p/movie.mkv", 8_000_000_000)],
            context, template: "{Title} [{Quality}]/{Title}.{ext}", quality: "bluray-1080p");

        var item = Assert.Single(plan.Items);
        Assert.Equal("The Martian [bluray-1080p]/The Martian.mkv", item.TargetRelativePath);
        // The scan-hint folder helper derives from the same render, so it prefixes the placed path.
        var folder = MovieImportPlanBuilder.MovieFolderRelative(context, template: "{Title} [{Quality}]/{Title}.{ext}", quality: "bluray-1080p");
        Assert.Equal("The Martian [bluray-1080p]", folder);
        Assert.StartsWith(folder + "/", item.TargetRelativePath);
    }
}

public sealed class MusicImportPlanBuilderTests {
    private static ImportCandidateFile File(string path, long size = 1_000) => new(path, size);

    [Fact]
    public void PlacesTracksUnderArtistAlbumStrippingTheReleaseWrapperFolder() {
        var plan = MusicImportPlanBuilder.Plan([
            File("Artist - Album (2020) [FLAC]/01 - One.flac"),
            File("Artist - Album (2020) [FLAC]/02 - Two.flac")
        ], "Daft Punk", "Discovery");

        Assert.False(plan.Blocked);
        Assert.Equal(
            ["Daft Punk/Discovery/01 - One.flac", "Daft Punk/Discovery/02 - Two.flac"],
            plan.Items.Select(item => item.TargetRelativePath).ToArray());
    }

    [Fact]
    public void DiscSubfoldersArePreserved() {
        var plan = MusicImportPlanBuilder.Plan([
            File("Release/CD1/01.flac"),
            File("Release/CD2/01.flac")
        ], "Artist", "Album");

        Assert.Equal(
            ["Artist/Album/CD1/01.flac", "Artist/Album/CD2/01.flac"],
            plan.Items.Select(item => item.TargetRelativePath).ToArray());
    }

    [Fact]
    public void UnixCaseDistinctWrapperFoldersAreNotCollapsedTogether() {
        if (OperatingSystem.IsWindows()) {
            return;
        }

        var plan = MusicImportPlanBuilder.Plan([
            File("Release/CD1/01.flac"),
            File("release/CD2/02.flac")
        ], "Artist", "Album");

        Assert.Equal(
            [
                "Artist/Album/Release/CD1/01.flac",
                "Artist/Album/release/CD2/02.flac"
            ],
            plan.Items.Select(item => item.TargetRelativePath).ToArray());
    }

    [Fact]
    public void CoverArtIsCarriedFlattenedIntoTheAlbumFolder() {
        var plan = MusicImportPlanBuilder.Plan([
            File("Release/01.mp3"),
            File("Release/artwork/cover.jpg")
        ], "Artist", "Album");

        Assert.Contains(plan.Items, item => item.TargetRelativePath == "Artist/Album/cover.jpg");
    }

    [Fact]
    public void NoAudioFilesBlockAsUnsupported() {
        var plan = MusicImportPlanBuilder.Plan([File("cover.jpg")], "Artist", "Album");

        Assert.True(plan.Blocked);
        Assert.Equal(ImportBlockReason.NoSupportedPayload, plan.BlockReason);
    }

    [Fact]
    public void ArtistAndAlbumNamesAreSanitized() {
        Assert.Equal("AC DC/Back In Black", MusicImportPlanBuilder.AlbumFolderRelative("AC/DC", "Back In Black"));
    }

    [Fact]
    public void CustomAlbumTemplateFlowsThroughToTrackPathsAndTheHintFolder() {
        const string template = "{Artist}/{Album} ({Year})";
        var plan = MusicImportPlanBuilder.Plan([
            File("Release/01 - One.flac"),
            File("Release/02 - Two.flac")
        ], "Daft Punk", "Discovery", template, year: 2001);

        // The album folder helper (the scan hint) matches the folder tracks were placed under.
        var folder = MusicImportPlanBuilder.AlbumFolderRelative("Daft Punk", "Discovery", template, year: 2001);
        Assert.Equal("Daft Punk/Discovery (2001)", folder);
        Assert.All(plan.Items, item => Assert.StartsWith(folder + "/", item.TargetRelativePath));
        Assert.Equal(
            ["Daft Punk/Discovery (2001)/01 - One.flac", "Daft Punk/Discovery (2001)/02 - Two.flac"],
            plan.Items.Select(item => item.TargetRelativePath).ToArray());
    }

    [Fact]
    public void CustomAlbumTemplateDropsEmptyYearWhenNoYearIsKnown() {
        const string template = "{Artist}/{Album} ({Year})";

        var folder = MusicImportPlanBuilder.AlbumFolderRelative("Daft Punk", "Discovery", template);

        Assert.Equal("Daft Punk/Discovery", folder);
    }
}

public sealed class TvImportPlanBuilderTests {
    private static ImportCandidateFile File(string path, long size = 1_000_000) => new(path, size);

    [Fact]
    public void SeasonPackPlacesEachTokenedEpisodeUnderItsSeasonFolder() {
        var plan = TvImportPlanBuilder.Plan([
            File("Andor.S01.1080p/Andor.S01E01.1080p.mkv"),
            File("Andor.S01.1080p/Andor.S01E02.1080p.mkv"),
            File("Andor.S01.1080p/release.nfo"),
        ], "Andor", seasonNumber: 1, episodeNumber: null);

        Assert.False(plan.Blocked);
        Assert.Equal(
            ["Andor/Season 01/Andor - S01E01.mkv", "Andor/Season 01/Andor - S01E02.mkv"],
            plan.Items.Select(item => item.TargetRelativePath).ToArray());
    }

    [Fact]
    public void CompleteSeriesPackSpansSeasonFoldersByEachFilesToken() {
        var plan = TvImportPlanBuilder.Plan([
            File("pack/Andor.S01E01.mkv"),
            File("pack/Andor.S02E01.mkv"),
        ], "Andor", seasonNumber: 1, episodeNumber: null);

        Assert.Equal(
            ["Andor/Season 01/Andor - S01E01.mkv", "Andor/Season 02/Andor - S02E01.mkv"],
            plan.Items.Select(item => item.TargetRelativePath).ToArray());
    }

    [Fact]
    public void TokenlessSingleEpisodeFallsBackToTheAcquisitionsUnit() {
        var plan = TvImportPlanBuilder.Plan(
            [File("andor.pilot.recode.mkv")], "Andor", seasonNumber: 1, episodeNumber: 5);

        Assert.Equal("Andor/Season 01/Andor - S01E05.mkv", Assert.Single(plan.Items).TargetRelativePath);
    }

    [Fact]
    public void TokenlessSeasonPackBlocksInsteadOfGuessingOrder() {
        var plan = TvImportPlanBuilder.Plan([
            File("pack/episode one.mkv"),
            File("pack/episode two.mkv"),
        ], "Andor", seasonNumber: 1, episodeNumber: null);

        Assert.True(plan.Blocked);
        Assert.Equal(ImportBlockReason.AmbiguousMultiplePrimaries, plan.BlockReason);
    }

    [Fact]
    public void SamplesAndNonVideoFilesAreSkipped() {
        var plan = TvImportPlanBuilder.Plan([
            File("pack/Andor.S01E01.sample.mkv"),
            File("pack/Andor.S01E01.mkv"),
            File("pack/artwork.jpg"),
        ], "Andor", seasonNumber: 1, episodeNumber: null);

        Assert.Equal("Andor/Season 01/Andor - S01E01.mkv", Assert.Single(plan.Items).TargetRelativePath);
    }

    [Fact]
    public void CustomTemplateFlowsThroughToEpisodePathsAndTheSeriesFolderStaysConsistent() {
        const string template = "{Series}/S{Season:00}/{Series} {Season:00}x{Episode:00} [{Quality}].{ext}";
        var plan = TvImportPlanBuilder.Plan([
            File("Andor.S01.1080p/Andor.S01E01.1080p.mkv"),
            File("Andor.S01.1080p/Andor.S01E02.1080p.mkv"),
        ], "Andor", seasonNumber: 1, episodeNumber: null, template: template, quality: "webdl-1080p");

        Assert.Equal(
            ["Andor/S01/Andor 01x01 [webdl-1080p].mkv", "Andor/S01/Andor 01x02 [webdl-1080p].mkv"],
            plan.Items.Select(item => item.TargetRelativePath).ToArray());
        // The series-folder hint helper is derived from the same template's first segment.
        var seriesFolder = TvImportPlanBuilder.SeriesFolderRelative("Andor", template);
        Assert.Equal("Andor", seriesFolder);
        Assert.All(plan.Items, item => Assert.StartsWith(seriesFolder + "/", item.TargetRelativePath));
    }

    [Fact]
    public void InvalidTemplateDegradesToTheDefaultLayout() {
        // A wrong-segment-count template must not break placement — it falls back to the default.
        var plan = TvImportPlanBuilder.Plan(
            [File("Andor.S01E01.mkv")], "Andor", seasonNumber: 1, episodeNumber: 1, template: "{Series} S{Season:00}E{Episode:00}.{ext}");

        Assert.Equal("Andor/Season 01/Andor - S01E01.mkv", Assert.Single(plan.Items).TargetRelativePath);
    }
}

public sealed class ImportTargetResolverTests {
    [Fact]
    public void ResolvesRelativeMovesUnderTheLibraryRoot() {
        var plan = ImportPlan.For([new ImportPlanItem("movie.mkv", "Title (2020)/Title (2020).mkv")]);

        var resolved = ImportTargetResolver.Resolve("/downloads/x", "/library/movies", plan);

        Assert.False(resolved.Blocked);
        var item = Assert.Single(resolved.Items);
        Assert.Equal(Path.GetFullPath("/downloads/x/movie.mkv"), item.SourceAbsolutePath);
        Assert.Equal(Path.GetFullPath("/library/movies/Title (2020)/Title (2020).mkv"), item.TargetAbsolutePath);
    }

    [Fact]
    public void TargetsEscapingTheRootAreBlocked() {
        var plan = ImportPlan.For([new ImportPlanItem("movie.mkv", "../outside/movie.mkv")]);

        var resolved = ImportTargetResolver.Resolve("/downloads/x", "/library/movies", plan);

        Assert.True(resolved.Blocked);
    }
}

/// <summary>
/// Covers the unit pass behind the TV plan (parity with <see cref="TvImportPlanBuilder.Plan"/>) and the
/// existing-target merges: folder anchoring on the real on-disk tree and the per-file upgrade gate.
/// </summary>
public sealed class MediaImportMergeTests {
    private static ImportCandidateFile File(string path, long size = 1_000_000) => new(path, size);

    [Fact]
    public void PlanUnitsMirrorsPlanWithParsedUnits() {
        var files = new[] {
            File("Andor.S01.1080p/Andor.S01E01.1080p.mkv"),
            File("Andor.S01.1080p/Andor.S01E02.1080p.mkv"),
        };

        var plan = TvImportPlanBuilder.Plan(files, "Andor", seasonNumber: 1, episodeNumber: null);
        var units = TvImportPlanBuilder.PlanUnits(files, "Andor", seasonNumber: 1, episodeNumber: null);

        Assert.False(units.Blocked);
        Assert.Equal(
            plan.Items.Select(item => (item.SourceRelativePath, item.TargetRelativePath)).ToArray(),
            units.Units.Select(unit => (unit.SourceRelativePath, unit.TargetRelativePath)).ToArray());
        Assert.Equal([(1, 1), (1, 2)], units.Units.Select(unit => (unit.Season, unit.Episode)).ToArray());
        Assert.Equal("Andor - S01E01.mkv", units.Units[0].FileName);
    }

    [Fact]
    public void SeasonFolderSegmentRendersTheTemplatesMiddleSegment() {
        Assert.Equal("Season 03", TvImportPlanBuilder.SeasonFolderSegment("Andor", 3));
        Assert.Equal(
            "S03",
            TvImportPlanBuilder.SeasonFolderSegment("Andor", 3, "{Series}/S{Season:00}/{Series} {Season:00}x{Episode:00}.{ext}"));
    }

    // ── TvExistingTargetMerge: per-file collision gate against an existing on-disk series ──

    private static TvSeriesDiskLayout Layout(Dictionary<int, string>? season1Episodes = null) =>
        new(Guid.NewGuid(), "/media/tv/Andor (2022)", new Dictionary<int, TvSeasonDiskLayout> {
            [1] = new(Guid.NewGuid(), "/media/tv/Andor (2022)/S01", season1Episodes ?? []),
        });

    private static IReadOnlyList<TvPlanUnit> Units(params (int Season, int Episode)[] units) =>
        units.Select(unit => new TvPlanUnit(
            $"pack/e{unit.Episode}.mkv", unit.Season, unit.Episode,
            $"Andor/Season {unit.Season:00}/Andor - S{unit.Season:00}E{unit.Episode:00}.mkv")).ToArray();

    private static string SeasonSegment(int season) => $"Season {season:00}";

    [Fact]
    public void NewEpisodesPlaceIntoTheExistingSeasonFolder() {
        var merged = TvExistingTargetMerge.Plan(
            Units((1, 2)), Layout(), SeasonSegment,
            incomingQualityPosition: (int)VideoQuality.Webdl1080p, incomingRevision: 1, ProperDownloadPolicy.PreferAndUpgrade);

        var item = Assert.Single(merged);
        Assert.Equal(MergeFileAction.PlaceNew, item.Action);
        Assert.Equal(Path.Combine("/media/tv/Andor (2022)/S01", "Andor - S01E02.mkv"), item.TargetAbsolutePath);
    }

    [Fact]
    public void MissingSeasonGetsATemplateNamedFolderInsideTheExistingSeriesFolder() {
        var merged = TvExistingTargetMerge.Plan(
            Units((2, 1)), Layout(), SeasonSegment,
            incomingQualityPosition: (int)VideoQuality.Webdl1080p, incomingRevision: 1, ProperDownloadPolicy.PreferAndUpgrade);

        Assert.Equal(
            Path.Combine("/media/tv/Andor (2022)", "Season 02", "Andor - S02E01.mkv"),
            Assert.Single(merged).TargetAbsolutePath);
    }

    [Fact]
    public void StrictlyBetterQualityReplacesTheOwnedFileInPlace() {
        var layout = Layout(new Dictionary<int, string> { [1] = "/media/tv/Andor (2022)/S01/e01.720p.WEB.mkv" });

        var merged = TvExistingTargetMerge.Plan(
            Units((1, 1)), layout, SeasonSegment,
            incomingQualityPosition: (int)VideoQuality.Bluray1080p, incomingRevision: 1, ProperDownloadPolicy.PreferAndUpgrade);

        var item = Assert.Single(merged);
        Assert.Equal(MergeFileAction.ReplaceUpgrade, item.Action);
        Assert.Equal("/media/tv/Andor (2022)/S01/e01.720p.WEB.mkv", item.OwnedFilePath);
        Assert.Equal(item.OwnedFilePath, item.TargetAbsolutePath);
    }

    [Fact]
    public void EqualOrWorseQualityIsDropped() {
        var layout = Layout(new Dictionary<int, string> { [1] = "/media/tv/Andor (2022)/S01/e01.1080p.BluRay.mkv" });

        var merged = TvExistingTargetMerge.Plan(
            Units((1, 1)), layout, SeasonSegment,
            incomingQualityPosition: (int)VideoQuality.Webdl1080p, incomingRevision: 1, ProperDownloadPolicy.PreferAndUpgrade);

        Assert.Equal(MergeFileAction.DropNotUpgrade, Assert.Single(merged).Action);
    }

    [Theory]
    [InlineData(ProperDownloadPolicy.PreferAndUpgrade, MergeFileAction.ReplaceUpgrade)]
    [InlineData(ProperDownloadPolicy.DoNotUpgrade, MergeFileAction.DropNotUpgrade)]
    [InlineData(ProperDownloadPolicy.DoNotPrefer, MergeFileAction.DropNotUpgrade)]
    public void EqualQualityProperReplacesOnlyUnderThePreferAndUpgradePolicy(ProperDownloadPolicy policy, MergeFileAction expected) {
        var layout = Layout(new Dictionary<int, string> { [1] = "/media/tv/Andor (2022)/S01/e01.1080p.WEB-DL.mkv" });

        var merged = TvExistingTargetMerge.Plan(
            Units((1, 1)), layout, SeasonSegment,
            incomingQualityPosition: (int)VideoQuality.Webdl1080p, incomingRevision: 2, policy);

        Assert.Equal(expected, Assert.Single(merged).Action);
    }

    [Theory]
    [InlineData(0, "/media/tv/Andor (2022)/S01/e01.1080p.WEB.mkv")] // incoming unrankable
    [InlineData(10, "/media/tv/Andor (2022)/S01/Episode 1.mkv")] // owned unrankable
    public void UnknownQualityOnEitherSideNeverReplaces(int incomingPosition, string ownedPath) {
        var layout = Layout(new Dictionary<int, string> { [1] = ownedPath });

        var merged = TvExistingTargetMerge.Plan(
            Units((1, 1)), layout, SeasonSegment, incomingPosition, incomingRevision: 1, ProperDownloadPolicy.PreferAndUpgrade);

        Assert.Equal(MergeFileAction.DropNotUpgrade, Assert.Single(merged).Action);
    }

    [Fact]
    public void UpgradeThatChangesTheExtensionIsSurfacedNotSwapped() {
        var layout = Layout(new Dictionary<int, string> { [1] = "/media/tv/Andor (2022)/S01/e01.720p.WEB.mp4" });

        var merged = TvExistingTargetMerge.Plan(
            Units((1, 1)), layout, SeasonSegment,
            incomingQualityPosition: (int)VideoQuality.Bluray2160p, incomingRevision: 1, ProperDownloadPolicy.PreferAndUpgrade);

        Assert.Equal(MergeFileAction.DropFormatChange, Assert.Single(merged).Action);
    }

    [Fact]
    public void ConsentedFormatChangeReplacesAGenuineUpgrade() {
        // The user's explicit "import anyway": the same cross-format upgrade becomes a replace…
        var layout = Layout(new Dictionary<int, string> { [1] = "/media/tv/Andor (2022)/S01/e01.720p.WEB.mp4" });

        var merged = TvExistingTargetMerge.Plan(
            Units((1, 1)), layout, SeasonSegment,
            incomingQualityPosition: (int)VideoQuality.Bluray2160p, incomingRevision: 1, ProperDownloadPolicy.PreferAndUpgrade,
            allowFormatChange: true);

        Assert.Equal(MergeFileAction.ReplaceUpgrade, Assert.Single(merged).Action);
    }

    [Fact]
    public void ConsentedFormatChangeStillNeverReplacesANonUpgrade() {
        // …but consent to a format change is not consent to a downgrade: quality gating is unchanged.
        var layout = Layout(new Dictionary<int, string> { [1] = "/media/tv/Andor (2022)/S01/e01.1080p.WEB.mp4" });

        var merged = TvExistingTargetMerge.Plan(
            Units((1, 1)), layout, SeasonSegment,
            incomingQualityPosition: (int)VideoQuality.Webdl720p, incomingRevision: 1, ProperDownloadPolicy.PreferAndUpgrade,
            allowFormatChange: true);

        Assert.Equal(MergeFileAction.DropNotUpgrade, Assert.Single(merged).Action);
    }

    [Fact]
    public void MultiEpisodeFileIsHeldWhenCoveredSlotsOwnDifferentFiles() {
        var layout = Layout(new Dictionary<int, string> {
            [1] = "/media/tv/Andor (2022)/S01/e01.720p.WEB.mkv",
            [2] = "/media/tv/Andor (2022)/S01/e02.720p.WEB.mkv",
        });
        var unit = new TvPlanUnit(
            "pack/e01-e02.mkv",
            1,
            1,
            "Andor/Season 01/Andor - S01E01.mkv") {
            ExtraEpisodes = [2],
        };

        var merged = TvExistingTargetMerge.Plan(
            [unit],
            layout,
            SeasonSegment,
            incomingQualityPosition: (int)VideoQuality.Bluray1080p,
            incomingRevision: 1,
            ProperDownloadPolicy.PreferAndUpgrade);

        Assert.Equal(MergeFileAction.HoldStructuralConflict, Assert.Single(merged).Action);
    }

    [Fact]
    public void MultiEpisodeFileCanUpgradeWhenEveryCoveredSlotSharesOneOwnedFile() {
        const string shared = "/media/tv/Andor (2022)/S01/e01-e02.720p.WEB.mkv";
        var layout = Layout(new Dictionary<int, string> { [1] = shared, [2] = shared });
        var unit = new TvPlanUnit(
            "pack/e01-e02.mkv",
            1,
            1,
            "Andor/Season 01/Andor - S01E01.mkv") {
            ExtraEpisodes = [2],
        };

        var merged = TvExistingTargetMerge.Plan(
            [unit],
            layout,
            SeasonSegment,
            incomingQualityPosition: (int)VideoQuality.Bluray1080p,
            incomingRevision: 1,
            ProperDownloadPolicy.PreferAndUpgrade);

        var item = Assert.Single(merged);
        Assert.Equal(MergeFileAction.ReplaceUpgrade, item.Action);
        Assert.Equal(shared, item.OwnedFilePath);
    }

    [Fact]
    public void SeparateIncomingFilesAreHeldWhenTheyOverlapOneSharedOwnedFile() {
        const string shared = "/media/tv/Andor (2022)/S01/e01-e02.720p.WEB.mkv";
        var layout = Layout(new Dictionary<int, string> { [1] = shared, [2] = shared });

        var merged = TvExistingTargetMerge.Plan(
            Units((1, 1), (1, 2)),
            layout,
            SeasonSegment,
            incomingQualityPosition: (int)VideoQuality.Bluray1080p,
            incomingRevision: 1,
            ProperDownloadPolicy.PreferAndUpgrade);

        Assert.All(merged, item => Assert.Equal(MergeFileAction.HoldStructuralConflict, item.Action));
    }

    [Fact]
    public void MissingAndOwnedClaimsAreHeldWhenTheCombinedFileIsNotAnUpgrade() {
        const string ownedEpisodeTwo = "/media/tv/Andor (2022)/S01/e02.1080p.BluRay.mkv";
        var layout = Layout(new Dictionary<int, string> { [2] = ownedEpisodeTwo });
        var unit = new TvPlanUnit(
            "pack/e01-e02.mkv",
            1,
            1,
            "Andor/Season 01/Andor - S01E01.mkv") {
            ExtraEpisodes = [2],
        };

        var merged = TvExistingTargetMerge.Plan(
            [unit],
            layout,
            SeasonSegment,
            incomingQualityPosition: (int)VideoQuality.Webdl1080p,
            incomingRevision: 1,
            ProperDownloadPolicy.PreferAndUpgrade);

        var item = Assert.Single(merged);
        Assert.Equal(MergeFileAction.HoldStructuralConflict, item.Action);
        Assert.Equal(ownedEpisodeTwo, item.OwnedFilePath);
    }

    [Fact]
    public void NarrowerUpgradeIsHeldWhenTheOwnedFileAlsoSatisfiesAnotherEpisode() {
        const string shared = "/media/tv/Andor (2022)/S01/e01-e02.720p.WEB.mkv";
        var layout = Layout(new Dictionary<int, string> { [1] = shared, [2] = shared });

        var merged = TvExistingTargetMerge.Plan(
            Units((1, 1)),
            layout,
            SeasonSegment,
            incomingQualityPosition: (int)VideoQuality.Bluray1080p,
            incomingRevision: 1,
            ProperDownloadPolicy.PreferAndUpgrade);

        var item = Assert.Single(merged);
        Assert.Equal(MergeFileAction.HoldStructuralConflict, item.Action);
        Assert.Equal(shared, item.OwnedFilePath);
    }

    [Fact]
    public void CombinedFileMayUpgradeOneOwnedSlotWhileAddingAMissingCoveredSlot() {
        const string ownedEpisodeOne = "/media/tv/Andor (2022)/S01/e01.720p.WEB.mkv";
        var layout = Layout(new Dictionary<int, string> { [1] = ownedEpisodeOne });
        var unit = new TvPlanUnit(
            "pack/e01-e02.mkv",
            1,
            1,
            "Andor/Season 01/Andor - S01E01.mkv") {
            ExtraEpisodes = [2],
        };

        var merged = TvExistingTargetMerge.Plan(
            [unit],
            layout,
            SeasonSegment,
            incomingQualityPosition: (int)VideoQuality.Bluray1080p,
            incomingRevision: 1,
            ProperDownloadPolicy.PreferAndUpgrade);

        var item = Assert.Single(merged);
        Assert.Equal(MergeFileAction.ReplaceUpgrade, item.Action);
        Assert.Equal(ownedEpisodeOne, item.OwnedFilePath);
    }

    [Fact]
    public void UnixCaseDistinctOwnedFilesRemainSeparateStructuralOwners() {
        if (OperatingSystem.IsWindows()) {
            return;
        }

        var layout = Layout(new Dictionary<int, string> {
            [1] = "/media/tv/Andor (2022)/S01/Episode.mkv",
            [2] = "/media/tv/Andor (2022)/S01/episode.mkv",
        });
        var unit = new TvPlanUnit(
            "pack/e01-e02.mkv",
            1,
            1,
            "Andor/Season 01/Andor - S01E01.mkv") {
            ExtraEpisodes = [2],
        };

        var merged = TvExistingTargetMerge.Plan(
            [unit],
            layout,
            SeasonSegment,
            incomingQualityPosition: (int)VideoQuality.Bluray1080p,
            incomingRevision: 1,
            ProperDownloadPolicy.PreferAndUpgrade);

        Assert.Equal(MergeFileAction.HoldStructuralConflict, Assert.Single(merged).Action);
    }

    [Fact]
    public void MultiSeasonPacksRoutePerFileToTheirOwnSeasons() {
        var layout = Layout(new Dictionary<int, string> { [1] = "/media/tv/Andor (2022)/S01/e01.720p.WEB.mkv" });

        var merged = TvExistingTargetMerge.Plan(
            Units((1, 1), (1, 2), (2, 1)), layout, SeasonSegment,
            incomingQualityPosition: (int)VideoQuality.Webdl1080p, incomingRevision: 1, ProperDownloadPolicy.PreferAndUpgrade);

        Assert.Equal(
            [MergeFileAction.ReplaceUpgrade, MergeFileAction.PlaceNew, MergeFileAction.PlaceNew],
            merged.Select(item => item.Action).ToArray());
        Assert.StartsWith(Path.Combine("/media/tv/Andor (2022)", "Season 02"), merged[2].TargetAbsolutePath);
    }

    // ── MusicExistingTargetMerge: re-anchor onto an existing album folder ──

    [Fact]
    public void MusicItemsReAnchorOntoTheExistingAlbumFolderAndSkipOwnedTracks() {
        var items = new[] {
            new ImportPlanItem("release/01 track.flac", "Artist/Album (2020)/01 track.flac"),
            new ImportPlanItem("release/Disc 1/02 track.flac", "Artist/Album (2020)/Disc 1/02 track.flac"),
        };
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "01 track.flac" };

        var merged = MusicExistingTargetMerge.Plan(items, "/media/music/Artist/Album", existing);

        Assert.Equal(MergeFileAction.DropNotUpgrade, merged[0].Action);
        Assert.Equal(MergeFileAction.PlaceNew, merged[1].Action);
        Assert.Equal(Path.Combine("/media/music/Artist/Album", "Disc 1/02 track.flac"), merged[1].TargetAbsolutePath);
    }
}

/// <summary>
/// Covers title-based multi-episode alignment: "as aired" packs bundle two provider episodes per file
/// (the live Clifford pack: the file labeled S01E01 carries the titles of provider episodes 1 AND 2,
/// S01E02 carries 3+4), so numeric placement would import the wrong content into almost every slot.
/// </summary>
public sealed class TvEpisodeTitleAlignmentTests {
    private static readonly TvEpisodeTitle[] CliffordTitles = [
        new(1, "My Best Friend"),
        new(2, "Cleo's Fair Share"),
        new(3, "Special Delivery"),
        new(4, "Ferry Tale"),
    ];

    private static ImportCandidateFile File(string path) => new(path, 700_000_000);

    [Fact]
    public void RealignsBundledEpisodesToTheirProviderNumbersAndRecordsCoverage() {
        var plan = TvImportPlanBuilder.PlanUnits([
            File("Pack/Clifford the Big Red Dog_S01E01_MY BEST FRIEND_CLEO'S FAIR SHARE.mkv"),
            File("Pack/Clifford the Big Red Dog_S01E02_SPECIAL DELIVERY_FERRY TALE.mkv"),
        ], "Clifford the Big Red Dog", seasonNumber: 1, episodeNumber: null, episodeTitles: CliffordTitles);

        Assert.False(plan.Blocked);
        var units = plan.Units.OrderBy(unit => unit.Episode).ToArray();
        // File "E01" anchors at provider episode 1 and covers 2…
        Assert.Equal(1, units[0].Episode);
        Assert.Equal([2], units[0].ExtraEpisodes);
        // …file "E02" is REALIGNED to provider episode 3 (its numeric label lied) and covers 4.
        Assert.Equal(3, units[1].Episode);
        Assert.Equal([4], units[1].ExtraEpisodes);
        Assert.Contains("S01E03", units[1].TargetRelativePath.Replace(" ", ""));
    }

    [Fact]
    public void FilesWhoseTailsMatchNoTitleKeepNumericPlacement() {
        var plan = TvImportPlanBuilder.PlanUnits([
            File("Pack/Show.S01E05.720p.WEB-DL.mkv"),
        ], "Show", seasonNumber: 1, episodeNumber: null, episodeTitles: CliffordTitles);

        var unit = Assert.Single(plan.Units);
        Assert.Equal(5, unit.Episode);
        Assert.Empty(unit.ExtraEpisodes);
    }

    [Fact]
    public void ConflictingRealignmentFallsBackToNumericForTheWholePayload() {
        // Both files' tails claim episode 1 — unreliable evidence, so numeric wins everywhere.
        var plan = TvImportPlanBuilder.PlanUnits([
            File("Pack/Show_S01E01_MY BEST FRIEND.mkv"),
            File("Pack/Show_S01E02_MY BEST FRIEND.mkv"),
        ], "Show", seasonNumber: 1, episodeNumber: null, episodeTitles: CliffordTitles);

        var units = plan.Units.OrderBy(unit => unit.Episode).ToArray();
        Assert.Equal(1, units[0].Episode);
        Assert.Equal(2, units[1].Episode);
        Assert.All(units, unit => Assert.Empty(unit.ExtraEpisodes));
    }

    [Fact]
    public void NoTitlesMeansNoRealignment() {
        var plan = TvImportPlanBuilder.PlanUnits([
            File("Pack/Show_S01E02_SPECIAL DELIVERY_FERRY TALE.mkv"),
        ], "Show", seasonNumber: 1, episodeNumber: null);

        Assert.Equal(2, Assert.Single(plan.Units).Episode);
    }

    [Fact]
    public void DuplicateNumericEpisodeSlotsAreHeldForManualResolution() {
        var plan = TvImportPlanBuilder.PlanUnits([
            File("Pack/Show.S01E02.WEB.mkv"),
            File("Pack/Show.1x02.BluRay.mp4"),
        ], "Show", seasonNumber: 1, episodeNumber: null);

        Assert.True(plan.Blocked);
        Assert.Equal(ImportBlockReason.AmbiguousMultiplePrimaries, plan.BlockReason);
    }

    [Fact]
    public void CoveredEpisodeCannotAlsoBeClaimedByASecondPrimaryFile() {
        var plan = TvImportPlanBuilder.PlanUnits([
            File("Pack/Show_S01E01_MY BEST FRIEND_CLEO'S FAIR SHARE.mkv"),
            File("Pack/Show.S01E02.720p.WEB-DL.mkv"),
        ], "Show", seasonNumber: 1, episodeNumber: null, episodeTitles: CliffordTitles);

        Assert.True(plan.Blocked);
        Assert.Equal(ImportBlockReason.AmbiguousMultiplePrimaries, plan.BlockReason);
    }

    [Fact]
    public void RequestedSeasonTitlesNeverRealignOtherSeasonsInACompleteSeriesPack() {
        var plan = TvImportPlanBuilder.PlanUnits([
            File("Pack/Show_S01E05_SPECIAL DELIVERY.mkv"),
            File("Pack/Show_S03E01_SPECIAL DELIVERY.mkv"),
        ], "Show", seasonNumber: 3, episodeNumber: null, episodeTitles: CliffordTitles);

        Assert.False(plan.Blocked);
        var seasonOne = Assert.Single(plan.Units, unit => unit.Season == 1);
        var seasonThree = Assert.Single(plan.Units, unit => unit.Season == 3);
        Assert.Equal(5, seasonOne.Episode);
        Assert.Equal(3, seasonThree.Episode);
    }
}
