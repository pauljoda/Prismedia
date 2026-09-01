using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Files;
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

    public Task DiscardPendingScanChangesAsync(CancellationToken cancellationToken) {
        // A failed SaveChanges leaves the poisoned entries tracked; they would be re-attempted
        // (and re-fail) on the next save in this scan's scope, so drop them entirely.
        _db.ChangeTracker.Clear();
        _structurePlacement.Reset();
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<PlayableVideoSourceOwner>> ListPlayableVideoSourceOwnersAsync(
        IReadOnlyCollection<string> filePaths,
        CancellationToken cancellationToken) {
        if (filePaths.Count == 0) {
            return [];
        }

        var paths = filePaths.Distinct(FileSystemPathComparison.Comparer).ToArray();
        var pathLengths = paths.Select(path => path.Length).Distinct().ToArray();
        var playableCodes = EntityKindRegistry.All
            .OfType<IPlayableVideoKindDefinition>()
            .Select(definition => definition.Kind.ToCode())
            .ToArray();
        var candidates = await _db.EntityFiles.AsNoTracking()
            .Where(file => file.Role == EntityFileRole.Source &&
                pathLengths.Contains(file.Path.Length))
            .Join(
                _db.Entities.AsNoTracking()
                    .Where(entity => playableCodes.Contains(entity.KindCode)),
                file => file.EntityId,
                entity => entity.Id,
                (file, entity) => new { file.EntityId, file.Path, entity.KindCode })
            .ToArrayAsync(cancellationToken);
        return candidates
            .Where(owner => paths.Contains(owner.Path, FileSystemPathComparison.Comparer))
            .Select(owner => new PlayableVideoSourceOwner(
                owner.EntityId,
                owner.Path,
                EntityKindRegistry.Require(owner.KindCode)))
            .ToArray();
    }

    public async Task<IReadOnlyList<Guid>> RebindPlayableVideoSourceAsync(
        string previousPath,
        string replacementPath,
        CancellationToken cancellationToken) {
        var playableCodes = EntityKindRegistry.All
            .OfType<IPlayableVideoKindDefinition>()
            .Select(definition => definition.Kind.ToCode())
            .ToArray();
        var previousOwnerCandidates = await _db.EntityFiles
            .Where(file => file.Role == EntityFileRole.Source
                && file.Path.Length == previousPath.Length)
            .Join(
                _db.Entities.Where(entity => playableCodes.Contains(entity.KindCode)),
                file => file.EntityId,
                entity => entity.Id,
                (file, _) => file)
            .ToArrayAsync(cancellationToken);
        var previousOwners = previousOwnerCandidates
            .Where(file => FileSystemPathComparison.Equals(file.Path, previousPath))
            .ToArray();
        if (previousOwners.Length == 0) {
            return [];
        }

        var ownerIds = previousOwners.Select(file => file.EntityId).Distinct().ToArray();
        if (!FileSystemPathComparison.Equals(previousPath, replacementPath)) {
            var replacementCandidates = await _db.EntityFiles.AsNoTracking()
                .Where(file => file.Role == EntityFileRole.Source
                    && file.Path.Length == replacementPath.Length
                    && !ownerIds.Contains(file.EntityId))
                .Join(
                    _db.Entities.AsNoTracking().Where(entity => playableCodes.Contains(entity.KindCode)),
                    file => file.EntityId,
                    entity => entity.Id,
                    (file, _) => file)
                .Select(file => file.Path)
                .ToArrayAsync(cancellationToken);
            var conflictingOwner = replacementCandidates.Any(path =>
                FileSystemPathComparison.Equals(path, replacementPath));
            if (conflictingOwner) {
                throw new InvalidOperationException(
                    $"The replacement video path is already owned by another Entity: {replacementPath}");
            }
        }

        var now = DateTimeOffset.UtcNow;
        var replacementSize = LibraryScanFileSystem.TryGetFileSize(replacementPath);
        foreach (var source in previousOwners) {
            source.Path = replacementPath;
            source.SizeBytes = replacementSize;
            source.UpdatedAt = now;
        }

        var entities = await _db.Entities.Where(entity => ownerIds.Contains(entity.Id)).ToArrayAsync(cancellationToken);
        foreach (var entity in entities) {
            entity.UpdatedAt = now;
        }

        // Technical, fingerprint, and subtitle data describes the retired file. Clearing it makes the
        // shared downstream planner probe/extract the replacement immediately while user state stays on
        // the same Entity id.
        _db.MediaSources.RemoveRange(await _db.MediaSources
            .Where(source => ownerIds.Contains(source.EntityId))
            .ToArrayAsync(cancellationToken));
        _db.EntityTechnical.RemoveRange(await _db.EntityTechnical
            .Where(technical => ownerIds.Contains(technical.EntityId))
            .ToArrayAsync(cancellationToken));
        _db.EntityFileFingerprints.RemoveRange(await _db.EntityFileFingerprints
            .Where(fingerprint => ownerIds.Contains(fingerprint.EntityId))
            .ToArrayAsync(cancellationToken));
        _db.EntitySubtitles.RemoveRange(await _db.EntitySubtitles
            .Where(subtitle => ownerIds.Contains(subtitle.EntityId))
            .ToArrayAsync(cancellationToken));
        var subtitleStates = await _db.EntitySubtitleStates
            .Where(detail => ownerIds.Contains(detail.EntityId))
            .ToArrayAsync(cancellationToken);
        foreach (var detail in subtitleStates) {
            detail.SubtitlesExtractedAt = null;
            detail.SubtitleSidecarSignature = null;
        }

        var generatedRolesByEntity = entities
            .Where(entity => EntityKindRegistry.TryDescribe(entity.KindCode, out _))
            .ToDictionary(
                entity => entity.Id,
                entity => EntityKindRegistry.TryDescribe(entity.KindCode, out var definition)
                    ? definition.Processing.GeneratedFileRoles.ToHashSet()
                    : new HashSet<EntityFileRole>());
        var generatedFiles = await _db.EntityFiles
            .Where(file => ownerIds.Contains(file.EntityId) && file.Source == FileSourceKind.Scan.ToCode())
            .ToArrayAsync(cancellationToken);
        _db.EntityFiles.RemoveRange(generatedFiles.Where(file =>
            generatedRolesByEntity.TryGetValue(file.EntityId, out var roles) && roles.Contains(file.Role)));
        var videoOwnerIds = entities
            .Where(entity => EntityKindRegistry.TryDescribe(entity.KindCode, out var definition)
                && definition.Processing.AssetFamily == GeneratedAssetFamily.Video)
            .Select(entity => entity.Id)
            .ToArray();
        _db.TrickplayInfos.RemoveRange(await _db.TrickplayInfos
            .Where(info => videoOwnerIds.Contains(info.EntityId))
            .ToArrayAsync(cancellationToken));

        var protectedPaths = (await _db.EntityFiles.AsNoTracking()
            .Where(file => ownerIds.Contains(file.EntityId) && file.Source != FileSourceKind.Scan.ToCode())
            .Select(file => file.Path)
            .ToArrayAsync(cancellationToken))
            .ToHashSet(FileSystemPathComparison.Comparer);

        await SaveChangesWithLifecycleAsync(cancellationToken);
        if (_assets is not null) {
            foreach (var entity in entities) {
                if (!EntityKindRegistry.TryDescribe(entity.KindCode, out var definition)) continue;
                if (definition.Processing.AssetFamily == GeneratedAssetFamily.None) continue;
                GeneratedAssetFamilyCatalog.DeleteGeneratedAssets(
                    _assets,
                    definition.Processing.AssetFamily,
                    entity.Id,
                    path => { if (!protectedPaths.Contains(path)) DeleteGeneratedFile(path); },
                    DeleteGeneratedDirectory);
            }
        }
        return ownerIds;
    }

    private static void DeleteGeneratedFile(string path) {
        try {
            if (File.Exists(path)) File.Delete(path);
        } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static void DeleteGeneratedDirectory(string path) {
        try {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    public async Task<IReadOnlyList<Guid>> UpsertVideosBatchAsync(
        IReadOnlyList<VideoUpsertItem> items, CancellationToken cancellationToken) {
        if (items.Count == 0) return [];

        var filePaths = items.Select(i => i.FilePath)
            .Distinct(FileSystemPathComparison.Comparer)
            .ToArray();
        var filePathLengths = filePaths.Select(path => path.Length).Distinct().ToArray();
        var itemCountsByPath = items
            .GroupBy(item => item.FilePath, FileSystemPathComparison.Comparer)
            .ToDictionary(group => group.Key, group => group.Count(), FileSystemPathComparison.Comparer);
        var movieCache = new Dictionary<string, Guid>(FileSystemPathComparison.Comparer);
        var seriesCache = new Dictionary<string, Guid>(FileSystemPathComparison.Comparer);
        var seasonCache = new Dictionary<(Guid SeriesId, int SeasonNumber), Guid>();
        var existingMoviesByFolderPath = await LoadExistingMoviesByFolderPathAsync(items, cancellationToken);

        // One source path can legitimately belong to SEVERAL video entities: a multi-episode file
        // (S01E05-E06) is bound to each episode it covers, so this lookup must group rather than
        // key a dictionary on the path — a unique-key dictionary here crashed every scan of a
        // library containing such a file.
        var existingEntities = (await _db.EntityFiles.AsNoTracking()
            .Where(f => f.Role == EntityFileRole.Source
                && filePathLengths.Contains(f.Path.Length))
            .Join(_db.Entities, f => f.EntityId, e => e.Id,
                (f, e) => new ExistingPlayableSourceOwner(f.Path, e.Id, e.KindCode, e.CreatedAt, e.SortOrder))
            .ToListAsync(cancellationToken))
            .Where(row => filePaths.Contains(row.Path, FileSystemPathComparison.Comparer))
            .GroupBy(x => x.Path, FileSystemPathComparison.Comparer)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).ToList(),
                FileSystemPathComparison.Comparer);

        var now = DateTimeOffset.UtcNow;
        var results = new List<Guid>(items.Count);

        foreach (var item in items) {
            Guid? movieId = null;
            if (item.ScanPlacement == PlayableVideoScanPlacement.Movie) {
                if (item.Movie is not { } movie) {
                    throw new InvalidOperationException("Movie scan placement requires movie folder context.");
                }

                movieId = await ResolveMovieFromScanAsync(
                    movie,
                    item.Metadata,
                    item.IsNsfw,
                    now,
                    movieCache,
                    existingMoviesByFolderPath,
                    cancellationToken);
            }

            if (existingEntities.TryGetValue(item.FilePath, out var owners)) {
                var expectedOwners = owners
                    .Where(owner => owner.KindCode == item.MaterializedKind.ToCode())
                    .ToArray();
                var ownerIds = movieId is { } resolvedMovieId
                    ? [resolvedMovieId]
                    : ResolveExistingOwnerIds(item, expectedOwners, itemCountsByPath[item.FilePath]);
                if (ownerIds.Length == 0) {
                    // A physical file can legitimately satisfy several episode positions. A row of a
                    // different direct playable kind is not a substitute for this item's identity.
                    ownerIds = [];
                }

                if (ownerIds.Length > 0) {
                    foreach (var ownerId in ownerIds) {
                        var tracked = await _db.Entities.FindAsync([ownerId], cancellationToken);
                        if (tracked is not null) tracked.UpdatedAt = now;
                        await EnsureEntityFileAsync(
                            ownerId,
                            EntityFileRole.Source,
                            item.FilePath,
                            LibraryScanFileSystem.TryGetFileSize(item.FilePath),
                            now,
                            cancellationToken);
                        await SetEntityLibraryRootAsync(ownerId, item.LibraryRootId, cancellationToken);
                    }
                    if (ownerIds.Length == 1) {
                        await MaterializePlayableStructureAsync(
                            ownerIds[0],
                            item,
                            now,
                            seriesCache,
                            seasonCache,
                            cancellationToken);
                    }
                    results.Add(ownerIds[0]);
                    continue;
                }
            }

            var id = movieId ?? Guid.NewGuid();
            if (movieId is null) {
                _db.Entities.Add(new EntityRow {
                    Id = id,
                    KindCode = item.MaterializedKind.ToCode(),
                    Title = item.Title,
                    IsNsfw = item.IsNsfw,
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }
            await SetEntityLibraryRootAsync(id, item.LibraryRootId, cancellationToken);
            await EnsureEntityFileAsync(
                id,
                EntityFileRole.Source,
                item.FilePath,
                LibraryScanFileSystem.TryGetFileSize(item.FilePath),
                now,
                cancellationToken);
            await MaterializePlayableStructureAsync(
                id,
                item,
                now,
                seriesCache,
                seasonCache,
                cancellationToken);
            results.Add(id);
        }

        await SaveChangesWithLifecycleAsync(cancellationToken);
        return results;
    }

    private static Guid[] ResolveExistingOwnerIds(
        VideoUpsertItem item,
        IReadOnlyList<ExistingPlayableSourceOwner> expectedOwners,
        int itemsForSourcePath) {
        if (item.ScanPlacement != PlayableVideoScanPlacement.Episode ||
            itemsForSourcePath == 1 ||
            expectedOwners.Count <= 1) {
            return expectedOwners.Select(owner => (Guid)owner.Id).ToArray();
        }

        var matches = expectedOwners
            .Where(owner => owner.SortOrder == item.StructuralSortOrder)
            .Select(owner => owner.Id)
            .ToArray();
        if (matches.Length <= 1) {
            return matches;
        }

        throw new InvalidOperationException(
            $"Cannot rescan multi-episode source '{item.FilePath}' because multiple '{item.MaterializedKind.ToCode()}' owners share structural order {item.StructuralSortOrder}.");
    }

    private sealed record ExistingPlayableSourceOwner(
        string Path,
        Guid Id,
        string KindCode,
        DateTimeOffset CreatedAt,
        int? SortOrder);

    private async Task MaterializePlayableStructureAsync(
        Guid entityId,
        VideoUpsertItem item,
        DateTimeOffset now,
        Dictionary<string, Guid> seriesCache,
        Dictionary<(Guid SeriesId, int SeasonNumber), Guid> seasonCache,
        CancellationToken cancellationToken) {
        switch (item.ScanPlacement) {
            case PlayableVideoScanPlacement.Movie:
                if (item.Movie is not { } movie) {
                    throw new InvalidOperationException("Movie scan placement requires movie folder context.");
                }
                await EnsureEntitySourceAsync(entityId, EntitySourceCode.Folder.ToCode(), movie.FolderPath, now, cancellationToken);
                await ClearStructuralChildLinkAsync(entityId, now, cancellationToken);
                return;

            case PlayableVideoScanPlacement.Standalone:
                await ClearStructuralChildLinkAsync(entityId, now, cancellationToken);
                return;

            case PlayableVideoScanPlacement.Episode:
                await MaterializeEpisodeStructureAsync(
                    entityId, item, now, seriesCache, seasonCache, cancellationToken);
                return;

            default:
                throw new ArgumentOutOfRangeException(nameof(item), item.ScanPlacement, "Unsupported playable scan placement.");
        }
    }

    private async Task MaterializeEpisodeStructureAsync(
        Guid entityId,
        VideoUpsertItem item,
        DateTimeOffset now,
        Dictionary<string, Guid> seriesCache,
        Dictionary<(Guid SeriesId, int SeasonNumber), Guid> seasonCache,
        CancellationToken cancellationToken) {
        if (item.Series is not { } series) {
            throw new InvalidOperationException("Episode scan placement requires series context.");
        }

        if (item.EpisodeNumber is { } episodeNumber) {
            await UpsertPositionAsync(entityId, EntityPositionCodes.Episode, episodeNumber, episodeNumber.ToString(), now, cancellationToken);
        }

        if (item.AbsoluteEpisodeNumber is { } absoluteEpisodeNumber) {
            await UpsertPositionAsync(entityId, EntityPositionCodes.AbsoluteEpisode, absoluteEpisodeNumber, absoluteEpisodeNumber.ToString(), now, cancellationToken);
        }

        var seriesId = await UpsertVideoSeriesFromScanAsync(
            series,
            item.IsNsfw,
            now,
            seriesCache,
            cancellationToken);

        if (item.Season is { } season) {
            await UpsertPositionAsync(entityId, EntityPositionCodes.Season, season.SeasonNumber, season.SeasonNumber.ToString(), now, cancellationToken);
            var seasonId = await UpsertVideoSeasonFromScanAsync(
                seriesId,
                season,
                item.IsNsfw,
                now,
                seasonCache,
                cancellationToken);
            var episodeSortOrder = item.EpisodeNumber ?? item.AbsoluteEpisodeNumber ?? 0;
            await UpsertStructuralChildLinkAsync(seasonId, entityId, episodeSortOrder, now, cancellationToken);
            return;
        }

        var sortOrder = item.EpisodeNumber ?? item.AbsoluteEpisodeNumber ?? item.FolderSortOrder ?? 0;
        await UpsertStructuralChildLinkAsync(seriesId, entityId, sortOrder, now, cancellationToken);
    }

    private async Task<Guid> ResolveMovieFromScanAsync(
        MovieScanInfo movie,
        VideoSidecarMetadata? metadata,
        bool isNsfw,
        DateTimeOffset now,
        Dictionary<string, Guid> movieCache,
        IReadOnlyDictionary<string, EntityRow> existingMoviesByFolderPath,
        CancellationToken cancellationToken) {
        if (movieCache.TryGetValue(movie.FolderPath, out var cachedMovieId)) {
            return cachedMovieId;
        }

        existingMoviesByFolderPath.TryGetValue(movie.FolderPath, out var existing);
        var movieId = existing?.Id ?? Guid.NewGuid();

        if (existing is null) {
            _db.Entities.Add(new EntityRow {
                Id = movieId,
                KindCode = EntityKind.Movie.ToCode(),
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

        await EnsureEntitySourceAsync(movieId, EntitySourceCode.Folder.ToCode(), movie.FolderPath, now, cancellationToken);
        movieCache[movie.FolderPath] = movieId;
        return movieId;
    }

    private async Task<IReadOnlyDictionary<string, EntityRow>> LoadExistingMoviesByFolderPathAsync(
        IReadOnlyList<VideoUpsertItem> items,
        CancellationToken cancellationToken) {
        var folderPaths = items
            .Where(item => item.ScanPlacement == PlayableVideoScanPlacement.Movie)
            .Select(item => item.Movie?.FolderPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!)
            .Distinct(FileSystemPathComparison.Comparer)
            .ToArray();
        if (folderPaths.Length == 0) {
            return new Dictionary<string, EntityRow>(FileSystemPathComparison.Comparer);
        }

        var folderPathLengths = folderPaths.Select(path => path.Length).Distinct().ToArray();
        var candidates = await _db.EntitySources
            .Where(source => source.Code == EntitySourceCode.Folder.ToCode()
                && folderPathLengths.Contains(source.Value.Length))
            .Join(
                _db.Entities.Where(entity => entity.KindCode == EntityKind.Movie.ToCode()),
                source => source.EntityId,
                entity => entity.Id,
                (source, entity) => new { source.Value, Entity = entity })
            .ToArrayAsync(cancellationToken);

        return candidates
            .Where(candidate => folderPaths.Contains(candidate.Value, FileSystemPathComparison.Comparer))
            .GroupBy(candidate => candidate.Value, FileSystemPathComparison.Comparer)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(candidate => candidate.Entity.CreatedAt)
                    .ThenBy(candidate => candidate.Entity.Id)
                    .First()
                    .Entity,
                FileSystemPathComparison.Comparer);
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

        var existing = await FindEntityBySourceValueAsync(
            EntityKind.VideoSeries.ToCode(), EntitySourceCode.Folder.ToCode(), series.FolderPath, cancellationToken);
        var seriesId = existing?.Id ?? Guid.NewGuid();

        if (existing is null) {
            _db.Entities.Add(new EntityRow {
                Id = seriesId,
                KindCode = EntityKind.VideoSeries.ToCode(),
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

        await EnsureEntitySourceAsync(seriesId, EntitySourceCode.Folder.ToCode(), series.FolderPath, now, cancellationToken);
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
                && entity.KindCode == EntityKind.VideoSeason.ToCode()
                && entity.SortOrder == season.SeasonNumber)
            .Select(entity => entity.Id)
            .FirstOrDefault();
        var existingSeasonRow = localSeasonId != Guid.Empty
            ? _db.Entities.Local.FirstOrDefault(entity => entity.Id == localSeasonId)
            : await _db.Entities.FirstOrDefaultAsync(entity =>
                entity.ParentEntityId == seriesId
                && entity.KindCode == EntityKind.VideoSeason.ToCode()
                && entity.SortOrder == season.SeasonNumber, cancellationToken);
        var seasonId = existingSeasonRow?.Id ?? Guid.NewGuid();

        if (existingSeasonRow is null) {
            _db.Entities.Add(new EntityRow {
                Id = seasonId,
                KindCode = EntityKind.VideoSeason.ToCode(),
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

        await EnsureEntitySourceAsync(seasonId, EntitySourceCode.Folder.ToCode(), season.FolderPath, now, cancellationToken);
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
