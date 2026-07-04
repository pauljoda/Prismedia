using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Tests.Acquisition;

public sealed class MediaNamingTemplatesTests {
    // ── Defaults reproduce the historical hardcoded layouts ────────────────
    [Fact]
    public void MovieDefaultRendersTitleYearLayout() {
        var path = MediaNamingTemplates.RenderMoviePath(
            template: null, new MediaNamingContext("The Martian", Year: 2015, Extension: ".mkv"));

        Assert.Equal("The Martian (2015)/The Martian (2015).mkv", path);
    }

    [Fact]
    public void MovieDefaultWithoutYearDropsTheEmptyParentheses() {
        var path = MediaNamingTemplates.RenderMoviePath(
            template: null, new MediaNamingContext("The Martian", Year: null, Extension: ".mp4"));

        Assert.Equal("The Martian/The Martian.mp4", path);
    }

    [Fact]
    public void TvDefaultPadsSeasonAndEpisode() {
        var path = MediaNamingTemplates.RenderTvPath(
            template: null, new MediaNamingContext("Andor", Series: "Andor", Season: 1, Episode: 5, Extension: ".mkv"));

        Assert.Equal("Andor/Season 01/Andor - S01E05.mkv", path);
    }

    [Fact]
    public void TvDefaultPadsDoubleDigitSeasonAndEpisode() {
        var path = MediaNamingTemplates.RenderTvPath(
            template: null, new MediaNamingContext("Andor", Series: "Andor", Season: 12, Episode: 24, Extension: ".mkv"));

        Assert.Equal("Andor/Season 12/Andor - S12E24.mkv", path);
    }

    [Fact]
    public void MusicDefaultRendersArtistAlbumFolder() {
        var folder = MediaNamingTemplates.RenderMusicAlbumFolder(
            template: null, new MediaNamingContext("Discovery", Artist: "Daft Punk", Album: "Discovery"));

        Assert.Equal("Daft Punk/Discovery", folder);
    }

    // ── Custom templates render ────────────────────────────────────────────
    [Fact]
    public void CustomMovieTemplateRendersQualityToken() {
        var path = MediaNamingTemplates.RenderMoviePath(
            "{Title} [{Quality}]/{Title}.{ext}",
            new MediaNamingContext("The Martian", Quality: "bluray-1080p", Extension: ".mkv"));

        Assert.Equal("The Martian [bluray-1080p]/The Martian.mkv", path);
    }

    [Fact]
    public void CustomMovieTemplateWithUnknownQualityDropsEmptyBrackets() {
        var path = MediaNamingTemplates.RenderMoviePath(
            "{Title} [{Quality}]/{Title}.{ext}",
            new MediaNamingContext("The Martian", Quality: null, Extension: ".mkv"));

        Assert.Equal("The Martian/The Martian.mkv", path);
    }

    [Fact]
    public void CustomTvTemplateWithUnpaddedSeasonRenders() {
        var path = MediaNamingTemplates.RenderTvPath(
            "{Series}/S{Season}/{Series} {Season}x{Episode:00}.{ext}",
            new MediaNamingContext("Andor", Series: "Andor", Season: 2, Episode: 3, Extension: ".mkv"));

        Assert.Equal("Andor/S2/Andor 2x03.mkv", path);
    }

    [Fact]
    public void CustomMusicTemplateRendersYearInAlbumFolder() {
        var folder = MediaNamingTemplates.RenderMusicAlbumFolder(
            "{Artist}/{Album} ({Year})",
            new MediaNamingContext("Discovery", Artist: "Daft Punk", Album: "Discovery", Year: 2001));

        Assert.Equal("Daft Punk/Discovery (2001)", folder);
    }

    // ── Scan-binding helpers derive from the same render ───────────────────
    [Fact]
    public void MovieFolderMatchesThePlacedFolder() {
        const string template = "{Title} - {Year}/{Title}.{ext}";
        var context = new MediaNamingContext("Dune", Year: 2021, Extension: ".mkv");

        var path = MediaNamingTemplates.RenderMoviePath(template, context);
        var folder = MediaNamingTemplates.RenderMovieFolder(template, context);

        Assert.StartsWith(folder + "/", path);
        Assert.Equal("Dune - 2021", folder);
    }

    [Fact]
    public void TvSeriesAndSeasonFoldersMatchThePlacedPath() {
        const string template = "{Series}/Season {Season:00}/{Series} - S{Season:00}E{Episode:00}.{ext}";
        var context = new MediaNamingContext("Andor", Series: "Andor", Season: 1, Episode: 2, Extension: ".mkv");

        var path = MediaNamingTemplates.RenderTvPath(template, context);
        var seriesFolder = MediaNamingTemplates.RenderTvSeriesFolder(template, new MediaNamingContext("Andor", Series: "Andor"));
        var seasonFolder = MediaNamingTemplates.RenderTvSeasonFolder(template, context);

        Assert.Equal("Andor", seriesFolder);
        Assert.Equal("Andor/Season 01", seasonFolder);
        Assert.StartsWith(seasonFolder + "/", path);
    }

    // ── Sanitization applies per segment ───────────────────────────────────
    [Fact]
    public void IllegalCharactersAreSanitizedPerSegment() {
        var path = MediaNamingTemplates.RenderMoviePath(
            template: null, new MediaNamingContext("Face/Off: Redux", Year: 1997, Extension: ".mkv"));

        Assert.Equal("Face Off Redux (1997)/Face Off Redux (1997).mkv", path);
    }

    // ── Validation ─────────────────────────────────────────────────────────
    [Fact]
    public void ValidateAcceptsTheKindDefaults() {
        Assert.Null(MediaNamingTemplates.Validate(EntityKind.Movie, MediaNamingTemplates.MovieDefault));
        Assert.Null(MediaNamingTemplates.Validate(EntityKind.VideoSeries, MediaNamingTemplates.TvDefault));
        Assert.Null(MediaNamingTemplates.Validate(EntityKind.AudioLibrary, MediaNamingTemplates.MusicDefault));
    }

    [Fact]
    public void ValidateNormalizesTvAcquisitionUnitsToTheSeriesProfileKind() {
        // A single episode / season pack acquisition kind resolves to the VideoSeries profile kind.
        Assert.Null(MediaNamingTemplates.Validate(EntityKind.Video, MediaNamingTemplates.TvDefault));
        Assert.Null(MediaNamingTemplates.Validate(EntityKind.VideoSeason, MediaNamingTemplates.TvDefault));
    }

    [Fact]
    public void ValidateRejectsWrongMovieSegmentCount() {
        Assert.NotNull(MediaNamingTemplates.Validate(EntityKind.Movie, "{Title} ({Year}).{ext}"));
        Assert.NotNull(MediaNamingTemplates.Validate(EntityKind.Movie, "a/b/{Title}.{ext}"));
    }

    [Fact]
    public void ValidateRejectsWrongTvSegmentCount() {
        Assert.NotNull(MediaNamingTemplates.Validate(EntityKind.VideoSeries, "{Series}/{Series} S{Season:00}E{Episode:00}.{ext}"));
    }

    [Fact]
    public void ValidateRejectsMissingRequiredTvTokens() {
        // Missing {Series} in segment 1.
        Assert.NotNull(MediaNamingTemplates.Validate(EntityKind.VideoSeries, "Shows/Season {Season:00}/{Series} - S{Season:00}E{Episode:00}.{ext}"));
        // Missing a season token in segment 2.
        Assert.NotNull(MediaNamingTemplates.Validate(EntityKind.VideoSeries, "{Series}/Season/{Series} - S{Season:00}E{Episode:00}.{ext}"));
        // Missing an episode token in segment 3.
        Assert.NotNull(MediaNamingTemplates.Validate(EntityKind.VideoSeries, "{Series}/Season {Season:00}/{Series} - S{Season:00}.{ext}"));
    }

    [Fact]
    public void ValidateRejectsMissingRequiredMusicTokens() {
        Assert.NotNull(MediaNamingTemplates.Validate(EntityKind.AudioLibrary, "Music/{Album}"));
        Assert.NotNull(MediaNamingTemplates.Validate(EntityKind.AudioLibrary, "{Artist}/Albums"));
    }

    [Fact]
    public void ValidateRejectsTraversal() {
        Assert.NotNull(MediaNamingTemplates.Validate(EntityKind.Movie, "../{Title} ({Year})/{Title} ({Year}).{ext}"));
    }

    [Fact]
    public void ValidateRejectsEmptySegments() {
        // A leading slash produces an empty first segment.
        Assert.NotNull(MediaNamingTemplates.Validate(EntityKind.Movie, "/{Title} ({Year})/{Title} ({Year}).{ext}"));
        // A doubled slash produces an empty middle segment (and the wrong count).
        Assert.NotNull(MediaNamingTemplates.Validate(EntityKind.AudioLibrary, "{Artist}//{Album}"));
    }

    [Fact]
    public void ValidateRejectsSegmentThatRendersEmpty() {
        // The movie file segment is only illegal characters, which sanitize away to nothing.
        Assert.NotNull(MediaNamingTemplates.Validate(EntityKind.Movie, "{Title} ({Year})/:::"));
    }

    [Fact]
    public void DefaultForBooksIsNull() {
        Assert.Null(MediaNamingTemplates.DefaultFor(EntityKind.Book));
        Assert.False(MediaNamingTemplates.IsMediaKind(EntityKind.Book));
    }
}
