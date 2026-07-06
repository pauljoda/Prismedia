using Prismedia.Application.Acquisition;

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
