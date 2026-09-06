using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;
using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Acquisition;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

/// <summary>
/// Covers resolving an acquisition's linked entity to its existing on-disk layout: the graph walk from
/// any granularity to the container, folder-provenance reads, and the phantom exclusion that keeps wanted
/// placeholders out of the owned-file map (they must stay bindable by the post-import scan).
/// </summary>
public sealed class EfImportTargetIndexTests {
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task CanonicalEpisodePositionsGovernImportCoverageEvenWhenDisplayOrderDiffers(bool missingDisplayOrder, bool postgres) {
        await using var database = postgres ? await PostgresTestDatabase.CreateAsync() : null;
        await using var db = database?.CreateContext() ?? CreateContext();
        var ids = SeedSeries(db, "/media/tv/Numbered Series");
        await db.SaveChangesAsync();
        var season = await db.Entities.SingleAsync(row => row.Id == ids.SeasonId);
        var episode = await db.Entities.SingleAsync(row => row.Id == ids.EpisodeId);
        var wanted = await db.Entities.SingleAsync(row => row.ParentEntityId == ids.SeasonId && row.Id != ids.EpisodeId);
        season.SortOrder = missingDisplayOrder ? null : 3;
        episode.SortOrder = missingDisplayOrder ? null : 8;
        wanted.SortOrder = null;
        db.EntityPositions.AddRange(
            new EntityPositionRow { EntityId = season.Id, Code = EntityPositionCodes.Season, Value = 20 },
            new EntityPositionRow { EntityId = episode.Id, Code = EntityPositionCodes.Episode, Value = 500 },
            new EntityPositionRow { EntityId = wanted.Id, Code = EntityPositionCodes.Episode, Value = 501 });
        await db.SaveChangesAsync();
        var index = new EfImportTargetIndex(db);

        var layout = (await index.GetTvLayoutAsync(ids.EpisodeId, default))!;
        var catalog = Assert.Single(await index.GetSeriesEpisodeCatalogAsync(ids.SeriesId, default));

        Assert.Equal(20, Assert.Single(layout.Seasons).Key);
        Assert.Equal(500, Assert.Single(layout.Seasons[20].EpisodeFileByNumber).Key);
        Assert.Equal(20, catalog.SeasonNumber);
        Assert.Equal([500, 501], catalog.Episodes.Select(row => row.Episode));
        Assert.Equal([500, 501], (await index.GetSeasonEpisodeTitlesAsync(ids.SeriesId, 20, default)).Select(row => row.Episode));
        Assert.False(await index.HasUnnumberedWantedTvEpisodesAsync(wanted.Id, 20, default));
        Assert.False(await index.HasUnnumberedWantedTvEpisodesAsync(ids.SeriesId, 20, default));
    }

    [Fact]
    public async Task SharedOwnershipInASeasonWithoutAFolderStillProtectsEveryEpisodeDuringUpgrades() {
        await using var db = CreateContext();
        var ids = SeedSeries(db, "/media/tv/Show");
        var source = db.EntityFiles.Local.Single();
        source.Path = "/media/tv/Show/S01/Show.S01E01.720p.WEB-DL.mkv";
        var otherSeason = AddEntity(db, EntityKind.VideoSeason.ToCode(), ids.SeriesId, 2);
        AddEntity(db, EntityKind.VideoEpisode.ToCode(), otherSeason, 1, sourcePath: source.Path);
        await db.SaveChangesAsync();

        var layout = (await new EfImportTargetIndex(db).GetTvLayoutAsync(ids.SeriesId, default))!;
        var merged = TvExistingTargetMerge.Plan([
            new("incoming/Show.S01E01.1080p.WEB-DL.mkv", 1, 1, "Show/S01/Show.S01E01.mkv"),
            new("incoming/Show.S02E02.1080p.WEB-DL.mkv", 2, 2, "Show/S02/Show.S02E02.mkv")
        ], layout, number => $"Season {number:00}", (int)VideoQuality.Webdl1080p, 1, ProperDownloadPolicy.PreferAndUpgrade);

        Assert.Equal(MergeFileAction.HoldStructuralConflict, merged[0].Action);
        Assert.Equal(source.Path, layout.Seasons[2].EpisodeFileByNumber[1]);
        Assert.Null(layout.Seasons[2].FolderPath);
        Assert.Equal(MergeFileAction.PlaceNew, merged[1].Action);
        Assert.Equal("/media/tv/Show/Season 02/Show.S02E02.mkv", merged[1].TargetAbsolutePath);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task OwnedEpisodeLookupDoesNotAddQueriesForEverySeason() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var setup = database.CreateContext();
        var ids = SeedSeries(setup, "/media/tv/Long Series");
        await setup.SaveChangesAsync();
        var counter = new QueryCounter();
        await using var measured = new PrismediaDbContext(new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseNpgsql(setup.Database.GetConnectionString()).AddInterceptors(counter).Options);
        var index = new EfImportTargetIndex(measured);
        Assert.Single((await index.GetTvLayoutAsync(ids.SeriesId, default))!.Seasons);
        var initialQueries = counter.Reads;
        counter.Reads = 0;
        Assert.Single(await index.GetSeriesEpisodeCatalogAsync(ids.SeriesId, default));
        var initialCatalogQueries = counter.Reads;
        for (var seasonNumber = 2; seasonNumber <= 60; seasonNumber++) {
            var season = AddEntity(setup, EntityKind.VideoSeason.ToCode(), ids.SeriesId, seasonNumber);
            var path = $"/media/tv/Long Series/Season {seasonNumber:00}";
            AddFolderSource(setup, season, path);
            AddEntity(setup, EntityKind.VideoEpisode.ToCode(), season, 1, sourcePath: path + "/episode.mkv");
        }
        await setup.SaveChangesAsync();
        counter.Reads = 0;

        var expanded = await index.GetTvLayoutAsync(ids.SeriesId, default);

        Assert.Equal(60, expanded!.Seasons.Count);
        Assert.All(expanded.Seasons.Values, season => Assert.Single(season.EpisodeFileByNumber));
        Assert.True(counter.Reads <= initialQueries,
            $"Owned-file lookup grew from {initialQueries} queries for one season to {counter.Reads} for sixty seasons.");
        counter.Reads = 0;
        Assert.Equal(60, (await index.GetSeriesEpisodeCatalogAsync(ids.SeriesId, default)).Count);
        Assert.True(counter.Reads <= initialCatalogQueries,
            $"Episode catalog lookup grew from {initialCatalogQueries} to {counter.Reads} queries.");
    }

    private sealed class QueryCounter : DbCommandInterceptor {
        public int Reads { get; set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default) {
            Reads++;
            return ValueTask.FromResult(result);
        }
    }

    [Fact]
    public async Task MissingEpisodeNumbersAreScopedToTheRequestedEntityAndSeason() {
        await using var db = CreateContext();
        var ids = SeedSeries(db, "/media/tv/Show");
        var otherSeasonId = AddEntity(db, EntityKind.VideoSeason.ToCode(), ids.SeriesId, 2);
        var unnumberedId = AddEntity(db, EntityKind.VideoEpisode.ToCode(), otherSeasonId, null, wanted: true);
        await db.SaveChangesAsync();
        var index = new EfImportTargetIndex(db);

        Assert.False(await index.HasUnnumberedWantedTvEpisodesAsync(ids.SeasonId, null, CancellationToken.None));
        Assert.False(await index.HasUnnumberedWantedTvEpisodesAsync(ids.SeriesId, 1, CancellationToken.None));
        Assert.True(await index.HasUnnumberedWantedTvEpisodesAsync(ids.SeriesId, 2, CancellationToken.None));
        Assert.True(await index.HasUnnumberedWantedTvEpisodesAsync(ids.SeriesId, null, CancellationToken.None));
        Assert.False(await index.HasUnnumberedWantedTvEpisodesAsync(ids.EpisodeId, 1, CancellationToken.None));
        Assert.True(await index.HasUnnumberedWantedTvEpisodesAsync(unnumberedId, 2, CancellationToken.None));

        (await db.Entities.SingleAsync(row => row.Id == otherSeasonId)).SortOrder = null;
        await db.SaveChangesAsync();
        Assert.True(await index.HasUnnumberedWantedTvEpisodesAsync(otherSeasonId, 2, CancellationToken.None));

        (await db.Entities.SingleAsync(row => row.Id == unnumberedId)).SortOrder = 3;
        await db.SaveChangesAsync();
        Assert.False(await index.HasUnnumberedWantedTvEpisodesAsync(ids.SeriesId, null, CancellationToken.None));
    }

    [Theory]
    [InlineData("series")]
    [InlineData("season")]
    [InlineData("episode")]
    public async Task ResolvesTheSeriesLayoutFromAnyLinkedGranularity(string linked) {
        await using var db = CreateContext();
        var ids = SeedSeries(db, "/media/tv/Show (2008)");
        await db.SaveChangesAsync();
        var index = new EfImportTargetIndex(db);

        var entityId = linked switch { "series" => ids.SeriesId, "season" => ids.SeasonId, _ => ids.EpisodeId };
        var layout = await index.GetTvLayoutAsync(entityId, CancellationToken.None);

        Assert.NotNull(layout);
        Assert.Equal(ids.SeriesId, layout!.SeriesEntityId);
        Assert.Equal("/media/tv/Show (2008)", layout.SeriesFolderPath);
        var season = Assert.Single(layout.Seasons).Value;
        Assert.Equal("/media/tv/Show (2008)/S01", season.FolderPath);
        // E01 is owned; the wanted phantom E02 has no Source file and must be absent.
        Assert.Equal(["/media/tv/Show (2008)/S01/e01.mkv"], season.EpisodeFileByNumber.Values.ToArray());
        Assert.Equal([1], season.EpisodeFileByNumber.Keys.ToArray());
    }

    [Fact]
    public async Task FilelessSeriesResolvesToNull() {
        await using var db = CreateContext();
        var seriesId = AddEntity(db, EntityKind.VideoSeries.ToCode(), parent: null, sortOrder: null);
        await db.SaveChangesAsync();

        Assert.Null(await new EfImportTargetIndex(db).GetTvLayoutAsync(seriesId, CancellationToken.None));
        Assert.Null(await new EfImportTargetIndex(db).GetTvLayoutAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task EpisodeTargetsCarryTheirAbsoluteEpisodeNumber() {
        await using var db = CreateContext();
        var ids = SeedSeries(db, "/media/tv/Sesame Street");
        db.Entities.Local.Single(entity => entity.Id == ids.EpisodeId).Title = "Episode 1316";
        db.EntityPositions.Add(new EntityPositionRow {
            EntityId = ids.EpisodeId,
            Code = EntityPositionCodes.AbsoluteEpisode,
            Value = 1316,
            Label = "1316",
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var episodes = await new EfImportTargetIndex(db)
            .GetSeasonEpisodeTitlesAsync(ids.SeriesId, 1, CancellationToken.None);

        var episode = Assert.Single(episodes, episode => episode.EntityId == ids.EpisodeId);
        Assert.Equal(1, episode.Episode);
        Assert.Equal("Episode 1316", episode.Title);
        Assert.Equal(1316, episode.AbsoluteEpisode);
    }

    [Fact]
    public async Task ResolvesTheMovieFolderAndOwnedFile() {
        await using var db = CreateContext();
        var movieId = AddEntity(db, EntityKind.Movie.ToCode(), parent: null, sortOrder: null, sourcePath: "/media/movies/Film (2020)/film.mkv");
        AddFolderSource(db, movieId, "/media/movies/Film (2020)");
        await db.SaveChangesAsync();

        var target = await new EfImportTargetIndex(db).GetMovieTargetAsync(movieId, CancellationToken.None);

        Assert.NotNull(target);
        Assert.Equal("/media/movies/Film (2020)", target!.FolderPath);
        Assert.Equal("/media/movies/Film (2020)/film.mkv", target.OwnedSourceFilePath);
    }

    [Fact]
    public async Task DoesNotResolveMovieTargetsFromVideoChildrenOrUnrelatedVideos() {
        await using var db = CreateContext();
        var movieId = AddEntity(db, EntityKind.Movie.ToCode(), parent: null, sortOrder: null,
            sourcePath: "/media/movies/Film (2020)/film.mkv");
        AddFolderSource(db, movieId, "/media/movies/Film (2020)");
        var childVideoId = AddEntity(db, EntityKind.Video.ToCode(), parent: movieId, sortOrder: null,
            sourcePath: "/media/movies/Film (2020)/bonus.mkv");
        var unrelatedVideoId = AddEntity(db, EntityKind.Video.ToCode(), parent: null, sortOrder: null,
            sourcePath: "/media/videos/Unrelated.mkv");
        await db.SaveChangesAsync();

        var index = new EfImportTargetIndex(db);

        Assert.Null(await index.GetMovieTargetAsync(childVideoId, CancellationToken.None));
        Assert.Null(await index.GetMovieTargetAsync(unrelatedVideoId, CancellationToken.None));
    }

    [Fact]
    public async Task ResolvesAlbumAndArtistFoldersWithRelativeTracks() {
        await using var db = CreateContext();
        var artistId = AddEntity(db, EntityKind.MusicArtist.ToCode(), parent: null, sortOrder: null);
        AddFolderSource(db, artistId, "/media/music/Artist");
        var albumId = AddEntity(db, EntityKind.AudioLibrary.ToCode(), parent: artistId, sortOrder: null);
        AddFolderSource(db, albumId, "/media/music/Artist/Album");
        AddEntity(db, EntityKind.AudioTrack.ToCode(), parent: albumId, sortOrder: 1, sourcePath: "/media/music/Artist/Album/01 - Track.flac");
        await db.SaveChangesAsync();

        var target = await new EfImportTargetIndex(db).GetAlbumTargetAsync(albumId, CancellationToken.None);

        Assert.NotNull(target);
        Assert.Equal("/media/music/Artist/Album", target!.AlbumFolderPath);
        Assert.Equal("/media/music/Artist", target.ArtistFolderPath);
        Assert.Contains("01 - Track.flac", target.ExistingRelativeFiles);
    }

    [Fact]
    public async Task FilelessAlbumUnderAnOnDiskArtistKeepsTheArtistFolder() {
        await using var db = CreateContext();
        var artistId = AddEntity(db, EntityKind.MusicArtist.ToCode(), parent: null, sortOrder: null);
        AddFolderSource(db, artistId, "/media/music/Artist");
        var albumId = AddEntity(db, EntityKind.AudioLibrary.ToCode(), parent: artistId, sortOrder: null);
        await db.SaveChangesAsync();

        var target = await new EfImportTargetIndex(db).GetAlbumTargetAsync(albumId, CancellationToken.None);

        Assert.NotNull(target);
        Assert.Null(target!.AlbumFolderPath);
        Assert.Equal("/media/music/Artist", target.ArtistFolderPath);
        Assert.Empty(target.ExistingRelativeFiles);
    }

    [Fact]
    public async Task AlbumSelectionReturnsOnlyStillWantedTrackChildren() {
        await using var db = CreateContext();
        var artistId = AddEntity(db, EntityKind.MusicArtist.ToCode(), parent: null, sortOrder: null);
        var albumId = AddEntity(db, EntityKind.AudioLibrary.ToCode(), parent: artistId, sortOrder: null);
        var ownedId = AddEntity(
            db,
            EntityKind.AudioTrack.ToCode(),
            parent: albumId,
            sortOrder: 1,
            sourcePath: "/media/music/Artist/Album/01 - Owned.flac");
        var wantedId = AddEntity(
            db,
            EntityKind.AudioTrack.ToCode(),
            parent: albumId,
            sortOrder: 2,
            wanted: true);
        db.Entities.Local.Single(entity => entity.Id == ownedId).Title = "Owned";
        db.Entities.Local.Single(entity => entity.Id == wantedId).Title = "Wanted";
        await db.SaveChangesAsync();

        var tracks = await new EfImportTargetIndex(db)
            .GetRequestedAudioTracksAsync(albumId, CancellationToken.None);

        var wanted = Assert.Single(tracks);
        Assert.Equal(wantedId, wanted.EntityId);
        Assert.Equal("Wanted", wanted.Title);
        Assert.Equal(2, wanted.Position);
    }

    [Fact]
    public async Task DirectTrackSelectionNeverExpandsToItsWantedSiblings() {
        await using var db = CreateContext();
        var artistId = AddEntity(db, EntityKind.MusicArtist.ToCode(), parent: null, sortOrder: null);
        var albumId = AddEntity(db, EntityKind.AudioLibrary.ToCode(), parent: artistId, sortOrder: null);
        var selectedId = AddEntity(db, EntityKind.AudioTrack.ToCode(), parent: albumId, sortOrder: 1, wanted: true);
        AddEntity(db, EntityKind.AudioTrack.ToCode(), parent: albumId, sortOrder: 2, wanted: true);
        db.Entities.Local.Single(entity => entity.Id == selectedId).Title = "Selected";
        await db.SaveChangesAsync();

        var tracks = await new EfImportTargetIndex(db)
            .GetRequestedAudioTracksAsync(selectedId, CancellationToken.None);

        Assert.Equal(selectedId, Assert.Single(tracks).EntityId);
    }

    [Fact]
    public async Task AStandaloneSongDoesNotRequireAnAlbumToBecomeAnImportTarget() {
        await using var db = CreateContext();
        var selectedId = AddEntity(db, EntityKind.AudioTrack.ToCode(), parent: null, sortOrder: null, wanted: true);
        db.Entities.Local.Single(entity => entity.Id == selectedId).Title = "Selected song";
        await db.SaveChangesAsync();

        var track = Assert.Single(await new EfImportTargetIndex(db).GetRequestedAudioTracksAsync(selectedId, default));

        Assert.Equal(selectedId, track.EntityId);
        Assert.Equal("Selected song", track.Title);
    }

    private static (Guid SeriesId, Guid SeasonId, Guid EpisodeId) SeedSeries(PrismediaDbContext db, string seriesFolder) {
        var seriesId = AddEntity(db, EntityKind.VideoSeries.ToCode(), parent: null, sortOrder: null);
        AddFolderSource(db, seriesId, seriesFolder);
        var seasonId = AddEntity(db, EntityKind.VideoSeason.ToCode(), parent: seriesId, sortOrder: 1);
        AddFolderSource(db, seasonId, $"{seriesFolder}/S01");
        var episodeId = AddEntity(db, EntityKind.VideoEpisode.ToCode(), parent: seasonId, sortOrder: 1, sourcePath: $"{seriesFolder}/S01/e01.mkv");
        // Wanted phantom episode: no Source file row.
        AddEntity(db, EntityKind.VideoEpisode.ToCode(), parent: seasonId, sortOrder: 2, wanted: true);
        return (seriesId, seasonId, episodeId);
    }

    private static Guid AddEntity(
        PrismediaDbContext db, string kindCode, Guid? parent, int? sortOrder, string? sourcePath = null, bool wanted = false) {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.Entities.Add(new EntityRow {
            Id = id, KindCode = kindCode, Title = kindCode, ParentEntityId = parent,
            SortOrder = sortOrder, IsWanted = wanted, CreatedAt = now, UpdatedAt = now
        });
        if (sourcePath is not null) {
            db.EntityFiles.Add(new EntityFileRow {
                Id = Guid.NewGuid(), EntityId = id, Role = EntityFileRole.Source, Path = sourcePath,
                CreatedAt = now, UpdatedAt = now
            });
        }

        return id;
    }

    private static void AddFolderSource(PrismediaDbContext db, Guid entityId, string folderPath) =>
        db.EntitySources.Add(new EntitySourceRow {
            EntityId = entityId,
            Code = EntitySourceCode.Folder.ToCode(),
            Value = folderPath,
            UpdatedAt = DateTimeOffset.UtcNow
        });

    private static PrismediaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
