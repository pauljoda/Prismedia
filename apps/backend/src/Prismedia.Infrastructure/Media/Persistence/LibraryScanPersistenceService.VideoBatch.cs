using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Application.Settings;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;
using Prismedia.Infrastructure.Settings;

namespace Prismedia.Infrastructure.Media.Persistence;

/// <summary>
/// Implements entity persistence operations for library scanning against the entity schema.
/// </summary>

public sealed partial class LibraryScanPersistenceService {
    // ── Batch upsert ──

    public async Task<IReadOnlyList<Guid>> UpsertVideosBatchAsync(
        IReadOnlyList<VideoUpsertItem> items, CancellationToken cancellationToken) {
        if (items.Count == 0) return [];

        var filePaths = items.Select(i => i.FilePath).ToList();
        var movieCache = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        var seriesCache = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        var seasonCache = new Dictionary<(Guid SeriesId, int SeasonNumber), Guid>();

        var existingEntities = await _db.EntityFiles.AsNoTracking()
            .Where(f => f.Role == EntityFileRole.Source && filePaths.Contains(f.Path))
            .Join(_db.Entities, f => f.EntityId, e => e.Id,
                (f, e) => new { f.Path, e.Id, Entity = e })
            .ToDictionaryAsync(x => x.Path, x => x, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var results = new List<Guid>(items.Count);

        foreach (var item in items) {
            if (existingEntities.TryGetValue(item.FilePath, out var existing)) {
                var tracked = await _db.Entities.FindAsync([existing.Id], cancellationToken);
                if (tracked is not null) tracked.UpdatedAt = now;
                // A found video may predate its detail row (a request-created wanted episode binds the
                // file path before this upsert) — backfill so it carries its library-root association.
                if (await _db.VideoDetails.FindAsync([existing.Id], cancellationToken) is null) {
                    _db.VideoDetails.Add(new VideoDetailRow { EntityId = existing.Id, LibraryRootId = item.LibraryRootId });
                }
                await MaterializeVideoHierarchyAsync(
                    existing.Id,
                    item,
                    now,
                    movieCache,
                    seriesCache,
                    seasonCache,
                    cancellationToken);
                results.Add(existing.Id);
                continue;
            }

            var id = Guid.NewGuid();
            _db.Entities.Add(new EntityRow { Id = id, KindCode = EntityKindRegistry.Video.Code, Title = item.Title, IsNsfw = item.IsNsfw, CreatedAt = now, UpdatedAt = now });
            _db.VideoDetails.Add(new VideoDetailRow { EntityId = id, LibraryRootId = item.LibraryRootId });
            _db.EntityFiles.Add(new EntityFileRow {
                Id = Guid.NewGuid(),
                EntityId = id,
                Role = EntityFileRole.Source,
                Path = item.FilePath,
                SizeBytes = LibraryScanFileSystem.TryGetFileSize(item.FilePath),
                CreatedAt = now,
                UpdatedAt = now
            });
            await MaterializeVideoHierarchyAsync(
                id,
                item,
                now,
                movieCache,
                seriesCache,
                seasonCache,
                cancellationToken);
            results.Add(id);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return results;
    }

    private async Task MaterializeVideoHierarchyAsync(
        Guid videoId,
        VideoUpsertItem item,
        DateTimeOffset now,
        Dictionary<string, Guid> movieCache,
        Dictionary<string, Guid> seriesCache,
        Dictionary<(Guid SeriesId, int SeasonNumber), Guid> seasonCache,
        CancellationToken cancellationToken) {
        if (item.EpisodeNumber is { } episodeNumber) {
            await UpsertPositionAsync(videoId, EntityPositionCodes.Episode, episodeNumber, episodeNumber.ToString(), now, cancellationToken);
        }

        if (item.AbsoluteEpisodeNumber is { } absoluteEpisodeNumber) {
            await UpsertPositionAsync(videoId, EntityPositionCodes.AbsoluteEpisode, absoluteEpisodeNumber, absoluteEpisodeNumber.ToString(), now, cancellationToken);
        }

        if (item.Movie is { } movie) {
            var movieId = await UpsertMovieFromScanAsync(
                movie,
                item.Metadata,
                item.IsNsfw,
                now,
                movieCache,
                cancellationToken);
            await UpsertStructuralChildLinkAsync(
                movieId,
                videoId,
                sortOrder: 0,
                now,
                cancellationToken);
            return;
        }

        if (item.Series is null) {
            await ClearStructuralChildLinkAsync(videoId, now, cancellationToken);
            return;
        }

        var seriesId = await UpsertVideoSeriesFromScanAsync(
            item.Series,
            item.IsNsfw,
            now,
            seriesCache,
            cancellationToken);

        if (item.Season is { } season) {
            await UpsertPositionAsync(videoId, EntityPositionCodes.Season, season.SeasonNumber, season.SeasonNumber.ToString(), now, cancellationToken);
            var seasonId = await UpsertVideoSeasonFromScanAsync(
                seriesId,
                season,
                item.IsNsfw,
                now,
                seasonCache,
                cancellationToken);
            var episodeSortOrder = item.EpisodeNumber ?? item.AbsoluteEpisodeNumber ?? 0;
            await UpsertStructuralChildLinkAsync(
                seasonId,
                videoId,
                episodeSortOrder,
                now,
                cancellationToken);
            return;
        }

        var sortOrder = item.EpisodeNumber ?? item.AbsoluteEpisodeNumber ?? item.FolderSortOrder ?? 0;
        await UpsertStructuralChildLinkAsync(
            seriesId,
            videoId,
            sortOrder,
            now,
            cancellationToken);
    }

    private async Task<Guid> UpsertMovieFromScanAsync(
        MovieScanInfo movie,
        VideoSidecarMetadata? metadata,
        bool isNsfw,
        DateTimeOffset now,
        Dictionary<string, Guid> movieCache,
        CancellationToken cancellationToken) {
        if (movieCache.TryGetValue(movie.FolderPath, out var cachedMovieId)) {
            return cachedMovieId;
        }

        var existing = await FindEntityBySourcePath(EntityKindRegistry.Movie.Code, movie.FolderPath, cancellationToken)
            ?? await FindEntityBySourceValueAsync(EntityKindRegistry.Movie.Code, "folder", movie.FolderPath, cancellationToken);
        var movieId = existing?.Id ?? Guid.NewGuid();

        if (existing is null) {
            _db.Entities.Add(new EntityRow {
                Id = movieId,
                KindCode = EntityKindRegistry.Movie.Code,
                Title = movie.Title,
                IsNsfw = isNsfw,
                CreatedAt = now,
                UpdatedAt = now
            });
        } else {
            var tracked = await _db.Entities.FindAsync([movieId], cancellationToken);
            if (tracked is not null) {
                tracked.UpdatedAt = now;
                if (isNsfw) tracked.IsNsfw = true;
                ApplyTitleIfScannedFallback(tracked, metadata?.Title, Path.GetFileName(movie.FolderPath), now);
            }
        }

        await EnsureEntityFileAsync(movieId, EntityFileRole.Source, movie.FolderPath, sizeBytes: null, now, cancellationToken);
        await EnsureEntitySourceAsync(movieId, "folder", movie.FolderPath, now, cancellationToken);
        await ApplyMovieSidecarMetadataAsync(movieId, metadata, isNsfw, now, cancellationToken);

        movieCache[movie.FolderPath] = movieId;
        return movieId;
    }

    private async Task ApplyMovieSidecarMetadataAsync(
        Guid movieId,
        VideoSidecarMetadata? metadata,
        bool markNsfw,
        DateTimeOffset now,
        CancellationToken cancellationToken) {
        if (metadata is null) {
            return;
        }

        var entity = await _db.Entities.FindAsync([movieId], cancellationToken);
        if (entity is not null) {
            if (markNsfw) {
                entity.IsNsfw = true;
            }
        }

        await UpsertDescriptionIfMissingAsync(movieId, metadata.Description, now, cancellationToken);
        await UpsertDateIfMissingAsync(movieId, "release", metadata.Date, now, cancellationToken);
        await AddUrlsAsync(movieId, metadata.Urls, now, cancellationToken);
        await AddTagsAsync(movieId, metadata.Tags, now, markNsfw, cancellationToken);
        await SetStudioIfMissingAsync(movieId, metadata.Studio, now, markNsfw, cancellationToken);
        await AddCreditsAsync(movieId, metadata.Performers, "performer", now, markNsfw, cancellationToken);
    }

    private async Task<Guid> UpsertVideoSeriesFromScanAsync(
        VideoSeriesScanInfo series,
        bool isNsfw,
        DateTimeOffset now,
        Dictionary<string, Guid> seriesCache,
        CancellationToken cancellationToken) {
        if (seriesCache.TryGetValue(series.FolderPath, out var cachedSeriesId)) {
            return cachedSeriesId;
        }

        var existing = await FindEntityBySourcePath(EntityKindRegistry.VideoSeries.Code, series.FolderPath, cancellationToken)
            ?? await FindEntityBySourceValueAsync(EntityKindRegistry.VideoSeries.Code, "folder", series.FolderPath, cancellationToken);
        var seriesId = existing?.Id ?? Guid.NewGuid();

        if (existing is null) {
            _db.Entities.Add(new EntityRow {
                Id = seriesId,
                KindCode = EntityKindRegistry.VideoSeries.Code,
                Title = series.Title,
                IsNsfw = isNsfw,
                CreatedAt = now,
                UpdatedAt = now
            });
        } else {
            var tracked = await _db.Entities.FindAsync([seriesId], cancellationToken);
            if (tracked is not null) {
                tracked.UpdatedAt = now;
                if (isNsfw) tracked.IsNsfw = true;
            }
        }

        await EnsureEntityFileAsync(seriesId, EntityFileRole.Source, series.FolderPath, sizeBytes: null, now, cancellationToken);
        await EnsureEntitySourceAsync(seriesId, "folder", series.FolderPath, now, cancellationToken);
        await EnsureVideoSeriesDetailAsync(seriesId, cancellationToken);

        seriesCache[series.FolderPath] = seriesId;
        return seriesId;
    }

    private async Task<Guid> UpsertVideoSeasonFromScanAsync(
        Guid seriesId,
        VideoSeasonScanInfo season,
        bool isNsfw,
        DateTimeOffset now,
        Dictionary<(Guid SeriesId, int SeasonNumber), Guid> seasonCache,
        CancellationToken cancellationToken) {
        var cacheKey = (seriesId, season.SeasonNumber);
        if (seasonCache.TryGetValue(cacheKey, out var cachedSeasonId)) {
            return cachedSeasonId;
        }

        var localSeasonId = _db.Entities.Local
            .Where(entity => entity.ParentEntityId == seriesId
                && entity.KindCode == EntityKindRegistry.VideoSeason.Code
                && entity.SortOrder == season.SeasonNumber)
            .Select(entity => entity.Id)
            .FirstOrDefault();
        var existingSeasonRow = localSeasonId != Guid.Empty
            ? _db.Entities.Local.FirstOrDefault(entity => entity.Id == localSeasonId)
            : await _db.Entities.FirstOrDefaultAsync(entity =>
                entity.ParentEntityId == seriesId
                && entity.KindCode == EntityKindRegistry.VideoSeason.Code
                && entity.SortOrder == season.SeasonNumber, cancellationToken);
        var seasonId = existingSeasonRow?.Id ?? Guid.NewGuid();

        if (existingSeasonRow is null) {
            _db.Entities.Add(new EntityRow {
                Id = seasonId,
                KindCode = EntityKindRegistry.VideoSeason.Code,
                Title = season.Title,
                ParentEntityId = seriesId,
                SortOrder = season.SeasonNumber,
                IsNsfw = isNsfw,
                CreatedAt = now,
                UpdatedAt = now
            });
        } else {
            var shouldMarkAncestors = ShouldMarkAutoIdentifyAncestors(existingSeasonRow, seriesId);
            existingSeasonRow.Title = season.Title;
            existingSeasonRow.ParentEntityId = seriesId;
            existingSeasonRow.SortOrder = season.SeasonNumber;
            existingSeasonRow.UpdatedAt = now;
            if (isNsfw) existingSeasonRow.IsNsfw = true;
            if (shouldMarkAncestors) {
                await MarkAutoIdentifyAncestorsUnorganizedAsync(seriesId, now, cancellationToken);
            }
        }

        await EnsureEntityFileAsync(seasonId, EntityFileRole.Source, season.FolderPath, sizeBytes: null, now, cancellationToken);
        await EnsureEntitySourceAsync(seasonId, "folder", season.FolderPath, now, cancellationToken);
        await UpsertPositionAsync(seasonId, EntityPositionCodes.Season, season.SeasonNumber, season.SeasonNumber.ToString(), now, cancellationToken);
        await UpsertStructuralChildLinkAsync(
            seriesId,
            seasonId,
            season.SeasonNumber,
            now,
            cancellationToken);

        seasonCache[cacheKey] = seasonId;
        return seasonId;
    }

}
