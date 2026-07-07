using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Acquisition;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

/// <summary>
/// Covers resolving an acquisition's linked entity to its existing on-disk layout: the graph walk from
/// any granularity to the container, Source-folder reads, and the phantom exclusion that keeps wanted
/// placeholders out of the owned-file map (they must stay bindable by the post-import scan).
/// </summary>
public sealed class EfImportTargetIndexTests {
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
        var seriesId = AddEntity(db, EntityKindRegistry.VideoSeries.Code, parent: null, sortOrder: null);
        await db.SaveChangesAsync();

        Assert.Null(await new EfImportTargetIndex(db).GetTvLayoutAsync(seriesId, CancellationToken.None));
        Assert.Null(await new EfImportTargetIndex(db).GetTvLayoutAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task ResolvesTheMovieFolderAndOwnedFile() {
        await using var db = CreateContext();
        var movieId = AddEntity(db, EntityKindRegistry.Movie.Code, parent: null, sortOrder: null, sourcePath: "/media/movies/Film (2020)");
        AddEntity(db, EntityKindRegistry.Video.Code, parent: movieId, sortOrder: 1, sourcePath: "/media/movies/Film (2020)/film.mkv");
        await db.SaveChangesAsync();

        var target = await new EfImportTargetIndex(db).GetMovieTargetAsync(movieId, CancellationToken.None);

        Assert.NotNull(target);
        Assert.Equal("/media/movies/Film (2020)", target!.FolderPath);
        Assert.Equal("/media/movies/Film (2020)/film.mkv", target.OwnedVideoFilePath);
    }

    [Fact]
    public async Task ResolvesAlbumAndArtistFoldersWithRelativeTracks() {
        await using var db = CreateContext();
        var artistId = AddEntity(db, EntityKindRegistry.MusicArtist.Code, parent: null, sortOrder: null, sourcePath: "/media/music/Artist");
        var albumId = AddEntity(db, EntityKindRegistry.AudioLibrary.Code, parent: artistId, sortOrder: null, sourcePath: "/media/music/Artist/Album");
        AddEntity(db, EntityKindRegistry.AudioTrack.Code, parent: albumId, sortOrder: 1, sourcePath: "/media/music/Artist/Album/01 - Track.flac");
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
        var artistId = AddEntity(db, EntityKindRegistry.MusicArtist.Code, parent: null, sortOrder: null, sourcePath: "/media/music/Artist");
        var albumId = AddEntity(db, EntityKindRegistry.AudioLibrary.Code, parent: artistId, sortOrder: null);
        await db.SaveChangesAsync();

        var target = await new EfImportTargetIndex(db).GetAlbumTargetAsync(albumId, CancellationToken.None);

        Assert.NotNull(target);
        Assert.Null(target!.AlbumFolderPath);
        Assert.Equal("/media/music/Artist", target.ArtistFolderPath);
        Assert.Empty(target.ExistingRelativeFiles);
    }

    private static (Guid SeriesId, Guid SeasonId, Guid EpisodeId) SeedSeries(PrismediaDbContext db, string seriesFolder) {
        var seriesId = AddEntity(db, EntityKindRegistry.VideoSeries.Code, parent: null, sortOrder: null, sourcePath: seriesFolder);
        var seasonId = AddEntity(db, EntityKindRegistry.VideoSeason.Code, parent: seriesId, sortOrder: 1, sourcePath: $"{seriesFolder}/S01");
        var episodeId = AddEntity(db, EntityKindRegistry.Video.Code, parent: seasonId, sortOrder: 1, sourcePath: $"{seriesFolder}/S01/e01.mkv");
        // Wanted phantom episode: no Source file row.
        AddEntity(db, EntityKindRegistry.Video.Code, parent: seasonId, sortOrder: 2, wanted: true);
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

    private static PrismediaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
