using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Files;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>
/// Resolves an acquisition's linked entity to its existing on-disk layout by walking the entity graph
/// (episode → season → series; track → album) and reading the Source-role file rows the scan persists
/// for folders and files. Fileless entities resolve to null so imports keep the template placement.
/// </summary>
public sealed class EfImportTargetIndex(PrismediaDbContext db) : IImportTargetIndex {
    /// <inheritdoc />
    public async Task<TvSeriesDiskLayout?> GetTvLayoutAsync(Guid entityId, CancellationToken cancellationToken) {
        var seriesId = await ResolveAncestorOfKindAsync(entityId, EntityKindRegistry.VideoSeries.Code, cancellationToken);
        if (seriesId is null) {
            return null;
        }

        var seriesFolder = await SourcePathAsync(seriesId.Value, cancellationToken);
        if (seriesFolder is null) {
            return null;
        }

        var seasonCode = EntityKindRegistry.VideoSeason.Code;
        var seasonRows = await (
            from season in db.Entities.AsNoTracking()
            where season.ParentEntityId == seriesId && season.KindCode == seasonCode
            join file in db.EntityFiles.AsNoTracking().Where(file => file.Role == EntityFileRole.Source)
                on season.Id equals file.EntityId
            select new { season.Id, season.SortOrder, file.Path })
            .ToArrayAsync(cancellationToken);

        var episodeCode = EntityKindRegistry.Video.Code;
        var seasons = new Dictionary<int, TvSeasonDiskLayout>();
        foreach (var season in seasonRows) {
            if (season.SortOrder is not { } seasonNumber || seasons.ContainsKey(seasonNumber)) {
                continue;
            }

            var episodeRows = await (
                from episode in db.Entities.AsNoTracking()
                where episode.ParentEntityId == season.Id && episode.KindCode == episodeCode
                join file in db.EntityFiles.AsNoTracking().Where(file => file.Role == EntityFileRole.Source)
                    on episode.Id equals file.EntityId
                select new { episode.SortOrder, file.Path })
                .ToArrayAsync(cancellationToken);

            var episodesByNumber = new Dictionary<int, string>();
            foreach (var episode in episodeRows) {
                if (episode.SortOrder is { } episodeNumber) {
                    episodesByNumber.TryAdd(episodeNumber, episode.Path);
                }
            }

            seasons[seasonNumber] = new TvSeasonDiskLayout(season.Id, season.Path, episodesByNumber);
        }

        return new TvSeriesDiskLayout(seriesId.Value, seriesFolder, seasons);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TvEpisodeTitle>> GetSeasonEpisodeTitlesAsync(
        Guid entityId, int seasonNumber, CancellationToken cancellationToken) {
        var seriesId = await ResolveAncestorOfKindAsync(entityId, EntityKindRegistry.VideoSeries.Code, cancellationToken);
        if (seriesId is null) {
            return [];
        }

        var seasonCode = EntityKindRegistry.VideoSeason.Code;
        var seasonId = await db.Entities.AsNoTracking()
            .Where(season => season.ParentEntityId == seriesId && season.KindCode == seasonCode && season.SortOrder == seasonNumber)
            .Select(season => (Guid?)season.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (seasonId is null) {
            return [];
        }

        var episodeCode = EntityKindRegistry.Video.Code;
        return await db.Entities.AsNoTracking()
            .Where(episode => episode.ParentEntityId == seasonId && episode.KindCode == episodeCode && episode.SortOrder != null)
            .OrderBy(episode => episode.SortOrder)
            .Select(episode => new TvEpisodeTitle(episode.SortOrder!.Value, episode.Title))
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<MovieDiskTarget?> GetMovieTargetAsync(Guid entityId, CancellationToken cancellationToken) {
        var movieId = await ResolveAncestorOfKindAsync(entityId, EntityKindRegistry.Movie.Code, cancellationToken);
        if (movieId is null || await SourcePathAsync(movieId.Value, cancellationToken) is not { } folder) {
            return null;
        }

        // A movie streams through its child video entity — that child owns the actual file.
        var episodeCode = EntityKindRegistry.Video.Code;
        var ownedFile = await (
            from child in db.Entities.AsNoTracking()
            where child.ParentEntityId == movieId && child.KindCode == episodeCode
            join file in db.EntityFiles.AsNoTracking().Where(file => file.Role == EntityFileRole.Source)
                on child.Id equals file.EntityId
            select file.Path)
            .FirstOrDefaultAsync(cancellationToken);

        return new MovieDiskTarget(movieId.Value, folder, ownedFile);
    }

    /// <inheritdoc />
    public async Task<AlbumDiskTarget?> GetAlbumTargetAsync(Guid entityId, CancellationToken cancellationToken) {
        var albumId = await ResolveAncestorOfKindAsync(entityId, EntityKindRegistry.AudioLibrary.Code, cancellationToken);
        if (albumId is null) {
            return null;
        }

        var albumFolder = await SourcePathAsync(albumId.Value, cancellationToken);

        var artistCode = EntityKindRegistry.MusicArtist.Code;
        var artistFolder = await (
            from album in db.Entities.AsNoTracking()
            where album.Id == albumId
            join artist in db.Entities.AsNoTracking().Where(row => row.KindCode == artistCode)
                on album.ParentEntityId equals artist.Id
            join file in db.EntityFiles.AsNoTracking().Where(file => file.Role == EntityFileRole.Source)
                on artist.Id equals file.EntityId
            select file.Path)
            .FirstOrDefaultAsync(cancellationToken);

        if (albumFolder is null && artistFolder is null) {
            return null;
        }

        var existing = new HashSet<string>(FileSystemPathComparison.Comparer);
        if (albumFolder is not null) {
            var trackCode = EntityKindRegistry.AudioTrack.Code;
            var trackPaths = await (
                from track in db.Entities.AsNoTracking()
                where track.ParentEntityId == albumId && track.KindCode == trackCode
                join file in db.EntityFiles.AsNoTracking().Where(file => file.Role == EntityFileRole.Source)
                    on track.Id equals file.EntityId
                select file.Path)
                .ToArrayAsync(cancellationToken);
            foreach (var path in trackPaths) {
                existing.Add(Path.GetRelativePath(albumFolder, path).Replace('\\', '/'));
            }
        }

        return new AlbumDiskTarget(albumId.Value, albumFolder, artistFolder, existing);
    }

    /// <summary>
    /// The entity itself when it already is <paramref name="kindCode"/>, else the nearest ancestor of
    /// that kind within a cycle-safe structural walk (an episode's series is two hops up). Null when absent.
    /// </summary>
    private async Task<Guid?> ResolveAncestorOfKindAsync(Guid entityId, string kindCode, CancellationToken cancellationToken) {
        var currentId = (Guid?)entityId;
        var visited = new HashSet<Guid>();
        while (currentId is { } id && visited.Add(id)) {
            var current = await db.Entities.AsNoTracking()
                .Where(row => row.Id == id)
                .Select(row => new { row.KindCode, row.ParentEntityId })
                .FirstOrDefaultAsync(cancellationToken);
            if (current is null) {
                return null;
            }

            if (string.Equals(current.KindCode, kindCode, StringComparison.Ordinal)) {
                return id;
            }

            currentId = current.ParentEntityId;
        }

        return null;
    }

    private Task<string?> SourcePathAsync(Guid entityId, CancellationToken cancellationToken) =>
        db.EntityFiles.AsNoTracking()
            .Where(file => file.EntityId == entityId && file.Role == EntityFileRole.Source)
            .Select(file => (string?)file.Path)
            .FirstOrDefaultAsync(cancellationToken);
}
