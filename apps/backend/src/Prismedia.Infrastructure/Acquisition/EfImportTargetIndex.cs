using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Files;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>
/// Resolves an acquisition's linked entity to its existing on-disk layout by walking the entity graph
/// (episode → season → series; track → album) and reading payload files separately from structural
/// folder provenance. Fileless entities resolve to null so imports keep the template placement.
/// </summary>
public sealed class EfImportTargetIndex(PrismediaDbContext db) : IImportTargetIndex {
    // Canonical positions own numbering. Display order is only a fallback for older unnumbered catalogs.
    private IQueryable<NumberedTvEntity> NumberedTvEntities(string positionCode) => db.Entities.AsNoTracking()
        .Select(entity => new NumberedTvEntity {
            Id = entity.Id, ParentEntityId = entity.ParentEntityId, KindCode = entity.KindCode,
            Title = entity.Title, IsWanted = entity.IsWanted,
            Position = db.EntityPositions.Where(position => position.EntityId == entity.Id && position.Code == positionCode)
                .Select(position => (int?)position.Value).FirstOrDefault() ?? entity.SortOrder
        });

    private sealed class NumberedTvEntity {
        public Guid Id { get; init; }
        public Guid? ParentEntityId { get; init; }
        public string KindCode { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public bool IsWanted { get; init; }
        public int? Position { get; init; }
    }

    /// <inheritdoc />
    public Task<Guid?> GetTvSeriesEntityIdAsync(Guid entityId, CancellationToken cancellationToken) =>
        ResolveAncestorOfKindAsync(entityId, EntityKind.VideoSeries.ToCode(), cancellationToken);

    /// <inheritdoc />
    public async Task<bool> CanReplaceTvFileAsync(Guid entityId, int seasonNumber, int episodeNumber,
        IReadOnlyList<int> coveredEpisodeNumbers, string previousPath, CancellationToken cancellationToken) {
        var claims = coveredEpisodeNumbers.Prepend(episodeNumber).Distinct().ToArray();
        if (seasonNumber < 0 || claims.Any(number => number <= 0) || string.IsNullOrWhiteSpace(previousPath)) return false;
        var seriesId = await GetTvSeriesEntityIdAsync(entityId, cancellationToken);
        if (seriesId is null) return false;
        var seasonCode = EntityKind.VideoSeason.ToCode();
        var seasons = await NumberedTvEntities(EntityPositionCodes.Season)
            .Where(season => season.ParentEntityId == seriesId && season.KindCode == seasonCode && season.Position == seasonNumber)
            .Select(season => season.Id).Take(2).ToArrayAsync(cancellationToken);
        if (seasons.Length != 1) return false;
        var seasonId = seasons[0];
        var episodeCode = EntityKind.VideoEpisode.ToCode();
        var episodes = await NumberedTvEntities(EntityPositionCodes.Episode)
            .Where(episode => episode.ParentEntityId == seasonId && episode.KindCode == episodeCode
                && episode.Position != null && claims.Contains(episode.Position.Value))
            .Select(episode => new { episode.Id, episode.Position }).ToArrayAsync(cancellationToken);
        if (episodes.Length == 0 || episodes.GroupBy(episode => episode.Position).Any(group => group.Count() > 1)) return false;
        var episodeIds = episodes.Select(episode => episode.Id).ToArray();
        // Inspect both directions: every owner of these bytes, even outside this series, and every
        // current source of the claimed episodes. A dictionary choosing one file would hide ambiguity.
        var sources = await db.EntityFiles.AsNoTracking().Where(source => source.Role == EntityFileRole.Source
                && (source.Path == previousPath || episodeIds.Contains(source.EntityId)))
            .Select(source => new { source.EntityId, source.Path }).ToArrayAsync(cancellationToken);
        return sources.Length > 0 && sources.All(source => episodeIds.Contains(source.EntityId)
                && FileSystemPathComparison.Equals(source.Path, previousPath))
            && sources.GroupBy(source => source.EntityId).All(group => group.Count() == 1);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TvSeasonEpisodeCatalog>> GetSeriesEpisodeCatalogAsync(
        Guid entityId, CancellationToken cancellationToken) {
        var seriesId = await ResolveAncestorOfKindAsync(entityId, EntityKind.VideoSeries.ToCode(), cancellationToken);
        if (seriesId is null) {
            return [];
        }
        var seasonCode = EntityKind.VideoSeason.ToCode();
        var episodeCode = EntityKind.VideoEpisode.ToCode();
        var rows = await (
            from season in NumberedTvEntities(EntityPositionCodes.Season)
            join episode in NumberedTvEntities(EntityPositionCodes.Episode) on season.Id equals episode.ParentEntityId
            where season.ParentEntityId == seriesId && season.KindCode == seasonCode && season.Position != null
                && episode.KindCode == episodeCode && episode.Position != null
            select new {
                SeasonId = season.Id,
                SeasonNumber = season.Position!.Value,
                Episode = new TvEpisodeTitle(episode.Position!.Value, episode.Title, episode.Id,
                    db.EntityPositions.Where(position => position.EntityId == episode.Id
                        && position.Code == EntityPositionCodes.AbsoluteEpisode)
                        .Select(position => (int?)position.Value).FirstOrDefault(), episode.IsWanted)
            }).ToArrayAsync(cancellationToken);
        return rows.GroupBy(row => (row.SeasonId, row.SeasonNumber))
            .OrderBy(group => group.Key.SeasonNumber).ThenBy(group => group.Key.SeasonId)
            .Select(group => new TvSeasonEpisodeCatalog(group.Key.SeasonId, group.Key.SeasonNumber,
                group.Select(row => row.Episode).OrderBy(episode => episode.Episode).ToArray()))
            .ToArray();
    }

    /// <inheritdoc />
    public async Task<bool> HasUnnumberedWantedTvEpisodesAsync(
        Guid entityId, int? seasonNumber, CancellationToken cancellationToken) {
        var episodeCode = EntityKindRegistry.PlayableVideoKindFor(PlayableVideoScanPlacement.Episode).ToCode();
        var target = await NumberedTvEntities(EntityPositionCodes.Episode)
            .Where(entity => entity.Id == entityId)
            .Select(entity => new { entity.KindCode, entity.IsWanted, entity.Position })
            .FirstOrDefaultAsync(cancellationToken);
        if (target?.KindCode == episodeCode) {
            return target.IsWanted && target.Position is null;
        }

        var seriesId = await ResolveAncestorOfKindAsync(entityId, EntityKind.VideoSeries.ToCode(), cancellationToken);
        if (seriesId is null) {
            return false;
        }
        var seasonCode = EntityKind.VideoSeason.ToCode();
        var scopedSeasonId = target?.KindCode == seasonCode ? entityId : (Guid?)null;
        return await (
            from episode in NumberedTvEntities(EntityPositionCodes.Episode)
            join season in NumberedTvEntities(EntityPositionCodes.Season) on episode.ParentEntityId equals season.Id
            where episode.KindCode == episodeCode && episode.IsWanted && episode.Position == null
                && season.KindCode == seasonCode && season.ParentEntityId == seriesId
                && (scopedSeasonId != null
                    ? season.Id == scopedSeasonId
                    : seasonNumber == null || season.Position == seasonNumber)
            select episode.Id).AnyAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<TvSeriesDiskLayout?> GetTvLayoutAsync(Guid entityId, CancellationToken cancellationToken) {
        var seriesId = await ResolveAncestorOfKindAsync(entityId, EntityKind.VideoSeries.ToCode(), cancellationToken);
        if (seriesId is null) {
            return null;
        }

        var seriesFolder = await FolderPathAsync(seriesId.Value, cancellationToken);
        if (seriesFolder is null) {
            return null;
        }

        var seasonCode = EntityKind.VideoSeason.ToCode();
        var seasonRows = await (
            from season in NumberedTvEntities(EntityPositionCodes.Season)
            where season.ParentEntityId == seriesId && season.KindCode == seasonCode
            join source in db.EntitySources.AsNoTracking().Where(source => source.Code == EntitySourceCode.Folder.ToCode())
                on season.Id equals source.EntityId into folderSources
            from source in folderSources.DefaultIfEmpty()
            select new { season.Id, season.Position, Path = source == null ? null : source.Value })
            .ToArrayAsync(cancellationToken);

        var episodeCode = EntityKindRegistry.PlayableVideoKindFor(PlayableVideoScanPlacement.Episode).ToCode();
        var seasonIds = seasonRows.Select(season => season.Id).Distinct().ToArray();
        var episodeRows = await (
            from episode in NumberedTvEntities(EntityPositionCodes.Episode)
            where episode.ParentEntityId != null && seasonIds.Contains(episode.ParentEntityId.Value)
                && episode.KindCode == episodeCode
            join file in db.EntityFiles.AsNoTracking().Where(file => file.Role == EntityFileRole.Source)
                on episode.Id equals file.EntityId into sourceFiles
            from file in sourceFiles.DefaultIfEmpty()
            select new { SeasonId = episode.ParentEntityId!.Value, episode.Position, Path = file == null ? null : file.Path })
            .ToArrayAsync(cancellationToken);
        var episodesBySeason = episodeRows.ToLookup(episode => episode.SeasonId);
        var unresolvedPaths = seasonRows.Where(season => season.Position is null).SelectMany(season => episodesBySeason[season.Id])
            .Where(episode => episode.Path != null).Select(episode => episode.Path!).ToHashSet(FileSystemPathComparison.Comparer);
        var seasons = new Dictionary<int, TvSeasonDiskLayout>();
        foreach (var group in seasonRows.Where(season => season.Position != null).GroupBy(season => season.Position!.Value)) {
            var season = group.First();
            var episodes = group.Select(row => row.Id).Distinct().SelectMany(id => episodesBySeason[id]).ToArray();
            var numbered = episodes.Where(episode => episode.Position is > 0).GroupBy(episode => episode.Position!.Value).ToArray();
            var ambiguous = numbered.Where(owners => owners.Count() > 1).Select(owners => owners.Key).ToHashSet();
            var episodesByNumber = numbered.Where(owners => owners.Count() == 1 && owners.First().Path != null)
                .ToDictionary(owners => owners.Key, owners => owners.First().Path!);
            var unresolvedSeason = group.Count() > 1 || episodes.Any(episode => (episode.Position is null or <= 0) && episode.Path != null);
            unresolvedPaths.UnionWith(episodes.Where(episode => episode.Path != null
                && (unresolvedSeason || episode.Position is { } number && ambiguous.Contains(number))).Select(episode => episode.Path!));
            seasons[group.Key] = new TvSeasonDiskLayout(season.Id, season.Path, episodesByNumber) {
                HasUnresolvedOwnership = unresolvedSeason,
                AmbiguousEpisodeNumbers = ambiguous
            };
        }

        return new TvSeriesDiskLayout(seriesId.Value, seriesFolder, seasons) { UnresolvedSourcePaths = unresolvedPaths };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TvEpisodeTitle>> GetSeasonEpisodeTitlesAsync(
        Guid entityId, int seasonNumber, CancellationToken cancellationToken) {
        var seriesId = await ResolveAncestorOfKindAsync(entityId, EntityKind.VideoSeries.ToCode(), cancellationToken);
        if (seriesId is null) {
            return [];
        }

        var seasonCode = EntityKind.VideoSeason.ToCode();
        var seasonId = await NumberedTvEntities(EntityPositionCodes.Season)
            .Where(season => season.ParentEntityId == seriesId && season.KindCode == seasonCode && season.Position == seasonNumber)
            .Select(season => (Guid?)season.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (seasonId is null) {
            return [];
        }

        var episodeCode = EntityKindRegistry.PlayableVideoKindFor(PlayableVideoScanPlacement.Episode).ToCode();
        return await NumberedTvEntities(EntityPositionCodes.Episode)
            .Where(episode => episode.ParentEntityId == seasonId && episode.KindCode == episodeCode && episode.Position != null)
            .OrderBy(episode => episode.Position)
            .Select(episode => new TvEpisodeTitle(
                episode.Position!.Value,
                episode.Title,
                episode.Id,
                db.EntityPositions
                    .Where(position => position.EntityId == episode.Id
                        && position.Code == EntityPositionCodes.AbsoluteEpisode)
                    .Select(position => (int?)position.Value)
                    .FirstOrDefault(), episode.IsWanted))
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RequestedAudioTrack>> GetRequestedAudioTracksAsync(
        Guid entityId,
        CancellationToken cancellationToken) {
        var trackCode = EntityKind.AudioTrack.ToCode();
        var directTrack = await db.Entities.AsNoTracking()
            .Where(entity => entity.Id == entityId && entity.KindCode == trackCode)
            .Select(entity => new RequestedAudioTrack(entity.Id, entity.Title, entity.SortOrder))
            .FirstOrDefaultAsync(cancellationToken);
        if (directTrack is not null) {
            return [directTrack];
        }
        var albumId = await ResolveAncestorOfKindAsync(
            entityId,
            EntityKind.AudioLibrary.ToCode(),
            cancellationToken);
        if (albumId is null) {
            return [];
        }

        return await db.Entities.AsNoTracking()
            .Where(track => track.ParentEntityId == albumId
                && track.KindCode == trackCode
                && track.IsWanted)
            .OrderBy(track => track.SortOrder)
            .ThenBy(track => track.Title)
            .Select(track => new RequestedAudioTrack(track.Id, track.Title, track.SortOrder))
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<MovieDiskTarget?> GetMovieTargetAsync(Guid entityId, CancellationToken cancellationToken) {
        var movieCode = EntityKind.Movie.ToCode();
        var isMovie = await db.Entities.AsNoTracking()
            .AnyAsync(row => row.Id == entityId && row.KindCode == movieCode, cancellationToken);
        if (!isMovie || await FolderPathAsync(entityId, cancellationToken) is not { } folder) {
            return null;
        }

        var ownedFile = await SourcePathAsync(entityId, cancellationToken);

        return new MovieDiskTarget(entityId, folder, ownedFile);
    }

    /// <inheritdoc />
    public async Task<AlbumDiskTarget?> GetAlbumTargetAsync(Guid entityId, CancellationToken cancellationToken) {
        var albumId = await ResolveAncestorOfKindAsync(entityId, EntityKind.AudioLibrary.ToCode(), cancellationToken);
        if (albumId is null) {
            return null;
        }

        var albumFolder = await FolderPathAsync(albumId.Value, cancellationToken);

        var artistCode = EntityKind.MusicArtist.ToCode();
        var artistFolder = await (
            from album in db.Entities.AsNoTracking()
            where album.Id == albumId
            join artist in db.Entities.AsNoTracking().Where(row => row.KindCode == artistCode)
                on album.ParentEntityId equals artist.Id
            join source in db.EntitySources.AsNoTracking()
                    .Where(source => source.Code == EntitySourceCode.Folder.ToCode())
                on artist.Id equals source.EntityId
            select source.Value)
            .FirstOrDefaultAsync(cancellationToken);

        if (albumFolder is null && artistFolder is null) {
            return null;
        }

        var existing = new HashSet<string>(FileSystemPathComparison.Comparer);
        if (albumFolder is not null) {
            var trackCode = EntityKind.AudioTrack.ToCode();
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

    private Task<string?> FolderPathAsync(Guid entityId, CancellationToken cancellationToken) =>
        db.EntitySources.AsNoTracking()
            .Where(source => source.EntityId == entityId && source.Code == EntitySourceCode.Folder.ToCode())
            .Select(source => (string?)source.Value)
            .FirstOrDefaultAsync(cancellationToken);
}
