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
public sealed class LibraryScanPersistenceService(PrismediaDbContext db) : ILibraryScanPersistence {
    // ── Library roots & settings ──

    public async Task<LibraryRootData?> GetLibraryRootAsync(Guid rootId, CancellationToken cancellationToken) {
        var row = await db.LibraryRoots.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == rootId, cancellationToken);
        return row is null ? null : ToData(row);
    }

    public async Task<IReadOnlyList<LibraryRootData>> GetEnabledRootsAsync(CancellationToken cancellationToken) {
        return await db.LibraryRoots.AsNoTracking()
            .Where(r => r.Enabled)
            .Select(r => ToData(r))
            .ToListAsync(cancellationToken);
    }

    public async Task<LibrarySettingsData> GetSettingsAsync(CancellationToken cancellationToken) {
        var settings = await new SettingsService(new EfSettingsPersistence(db))
            .GetGenerationSettingsAsync(cancellationToken);
        return new LibrarySettingsData(
            settings.AutoGenerateMetadata,
            settings.AutoGenerateFingerprints,
            settings.GeneratePhash,
            settings.AutoGeneratePreview,
            settings.GenerateTrickplay,
            settings.TrickplayIntervalSeconds,
            settings.PreviewClipDurationSeconds,
            settings.ThumbnailQuality,
            settings.TrickplayQuality);
    }

    public async Task UpdateRootLastScannedAsync(Guid rootId, CancellationToken cancellationToken) {
        var row = await db.LibraryRoots.FindAsync([rootId], cancellationToken);
        if (row is not null) {
            row.LastScannedAt = DateTimeOffset.UtcNow;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    // ── Entity upsert ──

    public async Task<Guid> UpsertVideoAsync(string filePath, string title, Guid libraryRootId, bool isNsfw, CancellationToken cancellationToken) {
        var existing = await FindEntityBySourcePath(EntityKindRegistry.Video.Code, filePath, cancellationToken);
        if (existing is not null) {
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            return existing.Id;
        }

        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();

        db.Entities.Add(new EntityRow { Id = id, KindCode = EntityKindRegistry.Video.Code, Title = title, CreatedAt = now, UpdatedAt = now });
        db.VideoDetails.Add(new VideoDetailRow { EntityId = id, LibraryRootId = libraryRootId });
        db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(),
            EntityId = id,
            Role = EntityFileRole.Source,
            Path = filePath,
            SizeBytes = TryGetFileSize(filePath),
            CreatedAt = now,
            UpdatedAt = now
        });
        if (isNsfw) {
            db.EntityFlags.Add(new EntityFlagRow { EntityId = id, IsNsfw = true, UpdatedAt = now });
        }

        await db.SaveChangesAsync(cancellationToken);
        return id;
    }

    public async Task<Guid> UpsertImageAsync(string filePath, string title, Guid? galleryEntityId, long? sizeBytes, int sortOrder, bool isNsfw, CancellationToken cancellationToken) {
        var existing = await FindEntityBySourcePath(EntityKindRegistry.Image.Code, filePath, cancellationToken);
        if (existing is not null) {
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            if (galleryEntityId is not null) {
                await UpsertStructuralChildLinkAsync(
                    galleryEntityId.Value,
                    existing.Id,
                    sortOrder,
                    DateTimeOffset.UtcNow,
                    cancellationToken);
            }

            await db.SaveChangesAsync(cancellationToken);
            return existing.Id;
        }

        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();

        db.Entities.Add(new EntityRow { Id = id, KindCode = EntityKindRegistry.Image.Code, Title = title, ParentEntityId = galleryEntityId, SortOrder = galleryEntityId is null ? null : sortOrder, CreatedAt = now, UpdatedAt = now });
        db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(),
            EntityId = id,
            Role = EntityFileRole.Source,
            Path = filePath,
            SizeBytes = sizeBytes,
            CreatedAt = now,
            UpdatedAt = now
        });

        if (galleryEntityId is not null) {
            await UpsertStructuralChildLinkAsync(
                galleryEntityId.Value,
                id,
                sortOrder,
                now,
                cancellationToken);
        }

        if (isNsfw) {
            db.EntityFlags.Add(new EntityFlagRow { EntityId = id, IsNsfw = true, UpdatedAt = now });
        }

        await db.SaveChangesAsync(cancellationToken);
        return id;
    }

    public async Task<Guid> UpsertGalleryAsync(string folderPath, string title, Guid libraryRootId, bool isNsfw, CancellationToken cancellationToken) {
        var existing = await FindEntityBySourcePath(EntityKindRegistry.Gallery.Code, folderPath, cancellationToken);
        if (existing is not null) {
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            var detail = await db.GalleryDetails.FindAsync([existing.Id], cancellationToken);
            if (detail is not null) detail.LibraryRootId = libraryRootId;
            await db.SaveChangesAsync(cancellationToken);
            return existing.Id;
        }

        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();

        db.Entities.Add(new EntityRow { Id = id, KindCode = EntityKindRegistry.Gallery.Code, Title = title, CreatedAt = now, UpdatedAt = now });
        db.GalleryDetails.Add(new GalleryDetailRow { EntityId = id, GalleryType = GalleryType.Folder, LibraryRootId = libraryRootId });
        db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(),
            EntityId = id,
            Role = EntityFileRole.Source,
            Path = folderPath,
            CreatedAt = now,
            UpdatedAt = now
        });
        if (isNsfw) {
            db.EntityFlags.Add(new EntityFlagRow { EntityId = id, IsNsfw = true, UpdatedAt = now });
        }

        await db.SaveChangesAsync(cancellationToken);
        return id;
    }

    public async Task<Guid> UpsertAudioTrackAsync(string filePath, string title, Guid audioLibraryId, int sortOrder, bool isNsfw, CancellationToken cancellationToken) {
        var existing = await FindEntityBySourcePath(EntityKindRegistry.AudioTrack.Code, filePath, cancellationToken);
        if (existing is not null) {
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            await UpsertStructuralChildLinkAsync(
                audioLibraryId,
                existing.Id,
                sortOrder,
                DateTimeOffset.UtcNow,
                cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return existing.Id;
        }

        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();

        db.Entities.Add(new EntityRow { Id = id, KindCode = EntityKindRegistry.AudioTrack.Code, Title = title, ParentEntityId = audioLibraryId, SortOrder = sortOrder, CreatedAt = now, UpdatedAt = now });
        db.AudioTrackDetails.Add(new AudioTrackDetailRow { EntityId = id });
        db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(),
            EntityId = id,
            Role = EntityFileRole.Source,
            Path = filePath,
            SizeBytes = TryGetFileSize(filePath),
            CreatedAt = now,
            UpdatedAt = now
        });
        await UpsertStructuralChildLinkAsync(
            audioLibraryId,
            id,
            sortOrder,
            now,
            cancellationToken);
        if (isNsfw) {
            db.EntityFlags.Add(new EntityFlagRow { EntityId = id, IsNsfw = true, UpdatedAt = now });
        }

        await db.SaveChangesAsync(cancellationToken);
        return id;
    }

    public async Task<Guid> UpsertAudioLibraryAsync(string folderPath, string title, Guid libraryRootId, bool isNsfw, CancellationToken cancellationToken) {
        var existing = await FindEntityBySourcePath(EntityKindRegistry.AudioLibrary.Code, folderPath, cancellationToken);
        if (existing is not null) {
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            var detail = await db.AudioLibraryDetails.FindAsync([existing.Id], cancellationToken);
            if (detail is not null) detail.LibraryRootId = libraryRootId;
            else db.AudioLibraryDetails.Add(new AudioLibraryDetailRow { EntityId = existing.Id, LibraryRootId = libraryRootId });
            await db.SaveChangesAsync(cancellationToken);
            return existing.Id;
        }

        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();

        db.Entities.Add(new EntityRow { Id = id, KindCode = EntityKindRegistry.AudioLibrary.Code, Title = title, CreatedAt = now, UpdatedAt = now });
        db.AudioLibraryDetails.Add(new AudioLibraryDetailRow { EntityId = id, LibraryRootId = libraryRootId });
        db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(),
            EntityId = id,
            Role = EntityFileRole.Source,
            Path = folderPath,
            CreatedAt = now,
            UpdatedAt = now
        });
        if (isNsfw) {
            db.EntityFlags.Add(new EntityFlagRow { EntityId = id, IsNsfw = true, UpdatedAt = now });
        }

        await db.SaveChangesAsync(cancellationToken);
        return id;
    }

    public async Task<Guid> UpsertBookAsync(string sourcePath, string title, Guid libraryRootId, bool isNsfw, CancellationToken cancellationToken) {
        var existing = await FindEntityBySourcePath(EntityKindRegistry.Book.Code, sourcePath, cancellationToken);
        if (existing is not null) {
            existing.Title = title;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            var detail = await db.BookDetails.FindAsync([existing.Id], cancellationToken);
            if (detail is not null) detail.LibraryRootId = libraryRootId;
            await db.SaveChangesAsync(cancellationToken);
            return existing.Id;
        }

        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();

        db.Entities.Add(new EntityRow { Id = id, KindCode = EntityKindRegistry.Book.Code, Title = title, CreatedAt = now, UpdatedAt = now });
        db.BookDetails.Add(new BookDetailRow { EntityId = id, BookType = BookType.Book, LibraryRootId = libraryRootId });
        db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(),
            EntityId = id,
            Role = EntityFileRole.Source,
            Path = sourcePath,
            SizeBytes = TryGetFileSize(sourcePath),
            CreatedAt = now,
            UpdatedAt = now
        });
        if (isNsfw) {
            db.EntityFlags.Add(new EntityFlagRow { EntityId = id, IsNsfw = true, UpdatedAt = now });
        }

        await db.SaveChangesAsync(cancellationToken);
        return id;
    }

    public async Task<Guid> UpsertBookVolumeAsync(string folderPath, string title, Guid bookEntityId, int sortOrder, bool isNsfw, CancellationToken cancellationToken) {
        var existing = await FindEntityBySourcePath(EntityKindRegistry.BookVolume.Code, folderPath, cancellationToken);
        if (existing is not null) {
            existing.Title = title;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            await UpsertStructuralChildLinkAsync(
                bookEntityId,
                existing.Id,
                sortOrder,
                DateTimeOffset.UtcNow,
                cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return existing.Id;
        }

        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();

        db.Entities.Add(new EntityRow { Id = id, KindCode = EntityKindRegistry.BookVolume.Code, Title = title, ParentEntityId = bookEntityId, SortOrder = sortOrder, CreatedAt = now, UpdatedAt = now });
        db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(),
            EntityId = id,
            Role = EntityFileRole.Source,
            Path = folderPath,
            CreatedAt = now,
            UpdatedAt = now
        });
        await UpsertStructuralChildLinkAsync(
            bookEntityId,
            id,
            sortOrder,
            now,
            cancellationToken);
        if (isNsfw) {
            db.EntityFlags.Add(new EntityFlagRow { EntityId = id, IsNsfw = true, UpdatedAt = now });
        }

        await db.SaveChangesAsync(cancellationToken);
        return id;
    }

    public async Task<Guid> UpsertBookChapterAsync(string archivePath, string title, Guid parentEntityId, int sortOrder, int pageCount, bool isNsfw, CancellationToken cancellationToken) {
        var existing = await FindEntityBySourcePath(EntityKindRegistry.BookChapter.Code, archivePath, cancellationToken);
        if (existing is not null) {
            existing.Title = title;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            await UpsertStructuralChildLinkAsync(
                parentEntityId,
                existing.Id,
                sortOrder,
                DateTimeOffset.UtcNow,
                cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return existing.Id;
        }

        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();

        db.Entities.Add(new EntityRow { Id = id, KindCode = EntityKindRegistry.BookChapter.Code, Title = title, ParentEntityId = parentEntityId, SortOrder = sortOrder, CreatedAt = now, UpdatedAt = now });
        db.BookChapterDetails.Add(new BookChapterDetailRow { EntityId = id });
        db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(),
            EntityId = id,
            Role = EntityFileRole.Source,
            Path = archivePath,
            CreatedAt = now,
            UpdatedAt = now
        });
        await UpsertStructuralChildLinkAsync(
            parentEntityId,
            id,
            sortOrder,
            now,
            cancellationToken);
        if (isNsfw) {
            db.EntityFlags.Add(new EntityFlagRow { EntityId = id, IsNsfw = true, UpdatedAt = now });
        }

        await db.SaveChangesAsync(cancellationToken);
        return id;
    }

    public async Task<Guid> UpsertBookPageAsync(string filePath, string title, Guid bookEntityId, Guid chapterEntityId, int sortOrder, bool isNsfw, CancellationToken cancellationToken) {
        var existing = await FindEntityBySourcePath(EntityKindRegistry.BookPage.Code, filePath, cancellationToken);
        if (existing is not null) {
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            await UpsertStructuralChildLinkAsync(
                chapterEntityId,
                existing.Id,
                sortOrder,
                DateTimeOffset.UtcNow,
                cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return existing.Id;
        }

        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();

        db.Entities.Add(new EntityRow { Id = id, KindCode = EntityKindRegistry.BookPage.Code, Title = title, ParentEntityId = chapterEntityId, SortOrder = sortOrder, CreatedAt = now, UpdatedAt = now });
        db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(),
            EntityId = id,
            Role = EntityFileRole.Source,
            Path = filePath,
            CreatedAt = now,
            UpdatedAt = now
        });
        await UpsertStructuralChildLinkAsync(
            chapterEntityId,
            id,
            sortOrder,
            now,
            cancellationToken);
        if (isNsfw) {
            db.EntityFlags.Add(new EntityFlagRow { EntityId = id, IsNsfw = true, UpdatedAt = now });
        }

        await db.SaveChangesAsync(cancellationToken);
        return id;
    }

    // ── Stale entity cleanup ──

    public async Task<int> RemoveStaleVideosByRootAsync(Guid rootId, IReadOnlySet<string> validPaths, CancellationToken cancellationToken) {
        var videoIds = await db.VideoDetails.AsNoTracking()
            .Where(vd => vd.LibraryRootId == rootId)
            .Select(vd => vd.EntityId)
            .ToListAsync(cancellationToken);

        var rootPath = await db.LibraryRoots.AsNoTracking()
            .Where(root => root.Id == rootId)
            .Select(root => root.Path)
            .FirstOrDefaultAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(rootPath)) {
            var videoCode = EntityKindRegistry.Video.Code;
            var sourceFiles = await db.EntityFiles.AsNoTracking()
                .Where(file => file.Role == EntityFileRole.Source)
                .Join(
                    db.Entities.AsNoTracking().Where(entity => entity.KindCode == videoCode),
                    file => file.EntityId,
                    entity => entity.Id,
                    (file, entity) => new { file.EntityId, file.Path })
                .ToListAsync(cancellationToken);

            videoIds.AddRange(sourceFiles
                .Where(file => IsPathUnderRoot(file.Path, rootPath))
                .Select(file => file.EntityId));
        }

        videoIds = videoIds.Distinct().ToList();
        return await RemoveStaleEntitiesBySourcePath(videoIds, validPaths, cancellationToken);
    }

    public async Task<int> RemoveStaleImagesInGalleryAsync(Guid galleryEntityId, IReadOnlySet<string> validPaths, CancellationToken cancellationToken) {
        var childIds = await db.Entities.AsNoTracking()
            .Where(entity => entity.ParentEntityId == galleryEntityId && entity.KindCode == EntityKindRegistry.Image.Code)
            .Select(entity => entity.Id)
            .ToListAsync(cancellationToken);

        return await RemoveStaleEntitiesBySourcePath(childIds, validPaths, cancellationToken);
    }

    public async Task<int> RemoveStaleGalleriesInRootAsync(Guid rootId, IReadOnlySet<string> validFolderPaths, CancellationToken cancellationToken) {
        var galleryIds = await db.GalleryDetails.AsNoTracking()
            .Where(gd => gd.LibraryRootId == rootId)
            .Select(gd => gd.EntityId)
            .ToListAsync(cancellationToken);

        return await RemoveStaleEntitiesBySourcePath(galleryIds, validFolderPaths, cancellationToken);
    }

    public async Task<int> RemoveStaleAudioTracksInLibraryAsync(Guid libraryEntityId, IReadOnlySet<string> validPaths, CancellationToken cancellationToken) {
        var childIds = await db.Entities.AsNoTracking()
            .Where(entity => entity.ParentEntityId == libraryEntityId && entity.KindCode == EntityKindRegistry.AudioTrack.Code)
            .Select(entity => entity.Id)
            .ToListAsync(cancellationToken);

        return await RemoveStaleEntitiesBySourcePath(childIds, validPaths, cancellationToken);
    }

    public async Task<int> RemoveStaleAudioLibrariesInRootAsync(Guid rootId, IReadOnlySet<string> validFolderPaths, CancellationToken cancellationToken) {
        var libraryIds = await db.AudioLibraryDetails.AsNoTracking()
            .Where(ald => ald.LibraryRootId == rootId)
            .Select(ald => ald.EntityId)
            .ToListAsync(cancellationToken);

        return await RemoveStaleEntitiesBySourcePath(libraryIds, validFolderPaths, cancellationToken);
    }

    public async Task<int> RemoveStaleBookVolumesAsync(Guid bookEntityId, IReadOnlySet<string> validFolderPaths, CancellationToken cancellationToken) {
        var volumeIds = await db.Entities.AsNoTracking()
            .Where(entity => entity.ParentEntityId == bookEntityId && entity.KindCode == EntityKindRegistry.BookVolume.Code)
            .Select(entity => entity.Id)
            .ToListAsync(cancellationToken);

        return await RemoveStaleEntitiesBySourcePath(volumeIds, validFolderPaths, cancellationToken);
    }

    public async Task<int> RemoveStaleBookChaptersAsync(Guid bookEntityId, IReadOnlySet<string> validArchivePaths, CancellationToken cancellationToken) {
        var chapterIds = await db.Entities.AsNoTracking()
            .Where(entity => entity.ParentEntityId == bookEntityId && entity.KindCode == EntityKindRegistry.BookChapter.Code)
            .Select(entity => entity.Id)
            .ToListAsync(cancellationToken);

        return await RemoveStaleEntitiesBySourcePath(chapterIds, validArchivePaths, cancellationToken);
    }

    public async Task<int> RemoveStaleBooksInRootAsync(Guid rootId, IReadOnlySet<string> validPaths, CancellationToken cancellationToken) {
        var bookIds = await db.BookDetails.AsNoTracking()
            .Where(bd => bd.LibraryRootId == rootId)
            .Select(bd => bd.EntityId)
            .ToListAsync(cancellationToken);

        return await RemoveStaleEntitiesBySourcePath(bookIds, validPaths, cancellationToken);
    }

    public async Task<int> RemoveOrphanSeriesAndSeasonsAsync(CancellationToken cancellationToken) {
        var seasonCode = EntityKindRegistry.VideoSeason.Code;
        var seriesCode = EntityKindRegistry.VideoSeries.Code;

        var orphanSeasons = await db.Entities
            .Where(e => e.KindCode == seasonCode
                && !db.Entities.Any(child => child.ParentEntityId == e.Id))
            .ToListAsync(cancellationToken);
        db.Entities.RemoveRange(orphanSeasons);

        if (orphanSeasons.Count > 0)
            await db.SaveChangesAsync(cancellationToken);

        var orphanSeries = await db.Entities
            .Where(e => e.KindCode == seriesCode
                && !db.Entities.Any(child => child.ParentEntityId == e.Id))
            .ToListAsync(cancellationToken);
        db.Entities.RemoveRange(orphanSeries);

        if (orphanSeries.Count > 0)
            await db.SaveChangesAsync(cancellationToken);

        return orphanSeasons.Count + orphanSeries.Count;
    }

    // ── Batch upsert ──

    public async Task<IReadOnlyList<Guid>> UpsertVideosBatchAsync(
        IReadOnlyList<VideoUpsertItem> items, CancellationToken cancellationToken) {
        if (items.Count == 0) return [];

        var filePaths = items.Select(i => i.FilePath).ToList();
        var seriesCache = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        var seasonCache = new Dictionary<(Guid SeriesId, int SeasonNumber), Guid>();

        var existingEntities = await db.EntityFiles.AsNoTracking()
            .Where(f => f.Role == EntityFileRole.Source && filePaths.Contains(f.Path))
            .Join(db.Entities, f => f.EntityId, e => e.Id,
                (f, e) => new { f.Path, e.Id, Entity = e })
            .ToDictionaryAsync(x => x.Path, x => x, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var results = new List<Guid>(items.Count);

        foreach (var item in items) {
            if (existingEntities.TryGetValue(item.FilePath, out var existing)) {
                var tracked = await db.Entities.FindAsync([existing.Id], cancellationToken);
                if (tracked is not null) tracked.UpdatedAt = now;
                await MaterializeVideoHierarchyAsync(
                    existing.Id,
                    item,
                    now,
                    seriesCache,
                    seasonCache,
                    cancellationToken);
                results.Add(existing.Id);
                continue;
            }

            var id = Guid.NewGuid();
            db.Entities.Add(new EntityRow { Id = id, KindCode = EntityKindRegistry.Video.Code, Title = item.Title, CreatedAt = now, UpdatedAt = now });
            db.VideoDetails.Add(new VideoDetailRow { EntityId = id, LibraryRootId = item.LibraryRootId });
            db.EntityFiles.Add(new EntityFileRow {
                Id = Guid.NewGuid(),
                EntityId = id,
                Role = EntityFileRole.Source,
                Path = item.FilePath,
                SizeBytes = TryGetFileSize(item.FilePath),
                CreatedAt = now,
                UpdatedAt = now
            });
            if (item.IsNsfw) {
                db.EntityFlags.Add(new EntityFlagRow { EntityId = id, IsNsfw = true, UpdatedAt = now });
            }
            await MaterializeVideoHierarchyAsync(
                id,
                item,
                now,
                seriesCache,
                seasonCache,
                cancellationToken);
            results.Add(id);
        }

        await db.SaveChangesAsync(cancellationToken);
        return results;
    }

    private async Task MaterializeVideoHierarchyAsync(
        Guid videoId,
        VideoUpsertItem item,
        DateTimeOffset now,
        Dictionary<string, Guid> seriesCache,
        Dictionary<(Guid SeriesId, int SeasonNumber), Guid> seasonCache,
        CancellationToken cancellationToken) {
        if (item.EpisodeNumber is { } episodeNumber) {
            await UpsertPositionAsync(videoId, "episode", episodeNumber, episodeNumber.ToString(), now, cancellationToken);
        }

        if (item.AbsoluteEpisodeNumber is { } absoluteEpisodeNumber) {
            await UpsertPositionAsync(videoId, "absolute-episode", absoluteEpisodeNumber, absoluteEpisodeNumber.ToString(), now, cancellationToken);
        }

        if (item.Series is null) {
            return;
        }

        var seriesId = await UpsertVideoSeriesFromScanAsync(
            item.Series,
            item.IsNsfw,
            now,
            seriesCache,
            cancellationToken);

        if (item.Season is { } season) {
            await UpsertPositionAsync(videoId, "season", season.SeasonNumber, season.SeasonNumber.ToString(), now, cancellationToken);
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

        var sortOrder = item.EpisodeNumber ?? item.AbsoluteEpisodeNumber ?? 0;
        await UpsertStructuralChildLinkAsync(
            seriesId,
            videoId,
            sortOrder,
            now,
            cancellationToken);
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
            db.Entities.Add(new EntityRow {
                Id = seriesId,
                KindCode = EntityKindRegistry.VideoSeries.Code,
                Title = series.Title,
                CreatedAt = now,
                UpdatedAt = now
            });
        } else {
            var tracked = await db.Entities.FindAsync([seriesId], cancellationToken);
            if (tracked is not null) tracked.UpdatedAt = now;
        }

        await EnsureEntityFileAsync(seriesId, EntityFileRole.Source, series.FolderPath, sizeBytes: null, now, cancellationToken);
        await EnsureEntitySourceAsync(seriesId, "folder", series.FolderPath, now, cancellationToken);
        await EnsureVideoSeriesDetailAsync(seriesId, cancellationToken);
        if (isNsfw) {
            await EnsureEntityFlagAsync(seriesId, now, cancellationToken);
        }

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

        var localSeasonId = db.Entities.Local
            .Where(entity => entity.ParentEntityId == seriesId
                && entity.KindCode == EntityKindRegistry.VideoSeason.Code
                && entity.SortOrder == season.SeasonNumber)
            .Select(entity => entity.Id)
            .FirstOrDefault();
        var existingSeasonRow = localSeasonId != Guid.Empty
            ? db.Entities.Local.FirstOrDefault(entity => entity.Id == localSeasonId)
            : await db.Entities.FirstOrDefaultAsync(entity =>
                entity.ParentEntityId == seriesId
                && entity.KindCode == EntityKindRegistry.VideoSeason.Code
                && entity.SortOrder == season.SeasonNumber, cancellationToken);
        var seasonId = existingSeasonRow?.Id ?? Guid.NewGuid();

        if (existingSeasonRow is null) {
            db.Entities.Add(new EntityRow {
                Id = seasonId,
                KindCode = EntityKindRegistry.VideoSeason.Code,
                Title = season.Title,
                ParentEntityId = seriesId,
                SortOrder = season.SeasonNumber,
                CreatedAt = now,
                UpdatedAt = now
            });
        } else {
            existingSeasonRow.Title = season.Title;
            existingSeasonRow.ParentEntityId = seriesId;
            existingSeasonRow.SortOrder = season.SeasonNumber;
            existingSeasonRow.UpdatedAt = now;
        }

        await EnsureEntityFileAsync(seasonId, EntityFileRole.Source, season.FolderPath, sizeBytes: null, now, cancellationToken);
        await EnsureEntitySourceAsync(seasonId, "folder", season.FolderPath, now, cancellationToken);
        await UpsertPositionAsync(seasonId, "season", season.SeasonNumber, season.SeasonNumber.ToString(), now, cancellationToken);
        await UpsertStructuralChildLinkAsync(
            seriesId,
            seasonId,
            season.SeasonNumber,
            now,
            cancellationToken);
        if (isNsfw) {
            await EnsureEntityFlagAsync(seasonId, now, cancellationToken);
        }

        seasonCache[cacheKey] = seasonId;
        return seasonId;
    }

    private async Task EnsureVideoSeriesDetailAsync(
        Guid seriesId,
        CancellationToken cancellationToken) {
        var detail = db.VideoSeriesDetails.Local.FirstOrDefault(row => row.EntityId == seriesId)
            ?? await db.VideoSeriesDetails.FindAsync([seriesId], cancellationToken);
        if (detail is null) {
            db.VideoSeriesDetails.Add(new VideoSeriesDetailRow { EntityId = seriesId });
        }
    }

    private async Task UpsertPositionAsync(
        Guid entityId,
        string code,
        int value,
        string? label,
        DateTimeOffset now,
        CancellationToken cancellationToken) {
        var position = db.EntityPositions.Local.FirstOrDefault(row => row.EntityId == entityId && row.Code == code)
            ?? await db.EntityPositions.FindAsync([entityId, code], cancellationToken);
        if (position is null) {
            db.EntityPositions.Add(new EntityPositionRow {
                EntityId = entityId,
                Code = code,
                Value = value,
                Label = label,
                UpdatedAt = now
            });
            return;
        }

        position.Value = value;
        position.Label = label;
        position.UpdatedAt = now;
    }

    private async Task UpsertStructuralChildLinkAsync(
        Guid parentId,
        Guid childId,
        int sortOrder,
        DateTimeOffset now,
        CancellationToken cancellationToken) {
        var child = db.Entities.Local.FirstOrDefault(row => row.Id == childId)
            ?? await db.Entities.FirstOrDefaultAsync(row => row.Id == childId, cancellationToken);
        if (child is null) {
            return;
        }

        child.ParentEntityId = parentId;
        child.SortOrder = sortOrder;
        child.UpdatedAt = now;

    }

    private async Task EnsureEntityFileAsync(
        Guid entityId,
        EntityFileRole role,
        string path,
        long? sizeBytes,
        DateTimeOffset now,
        CancellationToken cancellationToken) {
        var file = db.EntityFiles.Local.FirstOrDefault(row => row.EntityId == entityId && row.Role == role)
            ?? await db.EntityFiles.FirstOrDefaultAsync(row =>
                row.EntityId == entityId && row.Role == role, cancellationToken);
        if (file is null) {
            db.EntityFiles.Add(new EntityFileRow {
                Id = Guid.NewGuid(),
                EntityId = entityId,
                Role = role,
                Path = path,
                SizeBytes = sizeBytes,
                CreatedAt = now,
                UpdatedAt = now
            });
            return;
        }

        file.Path = path;
        file.SizeBytes = sizeBytes;
        file.UpdatedAt = now;
    }

    private async Task EnsureEntitySourceAsync(
        Guid entityId,
        string code,
        string value,
        DateTimeOffset now,
        CancellationToken cancellationToken) {
        var source = db.EntitySources.Local.FirstOrDefault(row => row.EntityId == entityId && row.Code == code)
            ?? await db.EntitySources.FindAsync([entityId, code], cancellationToken);
        if (source is null) {
            db.EntitySources.Add(new EntitySourceRow {
                EntityId = entityId,
                Code = code,
                Value = value,
                UpdatedAt = now
            });
            return;
        }

        source.Value = value;
        source.UpdatedAt = now;
    }

    private async Task EnsureEntityFlagAsync(Guid entityId, DateTimeOffset now, CancellationToken cancellationToken) {
        var flag = db.EntityFlags.Local.FirstOrDefault(row => row.EntityId == entityId)
            ?? await db.EntityFlags.FindAsync([entityId], cancellationToken);
        if (flag is null) {
            db.EntityFlags.Add(new EntityFlagRow { EntityId = entityId, IsNsfw = true, UpdatedAt = now });
            return;
        }

        flag.IsNsfw = true;
        flag.UpdatedAt = now;
    }

    public async Task<IReadOnlyDictionary<Guid, DownstreamNeeds>> CheckDownstreamNeedsBatchAsync(
        IReadOnlyList<Guid> entityIds, CancellationToken cancellationToken) {
        if (entityIds.Count == 0) return new Dictionary<Guid, DownstreamNeeds>();

        var ids = entityIds.ToList();

        var hasTechnical = (await db.EntityTechnical.AsNoTracking()
            .Where(t => ids.Contains(t.EntityId) && t.DurationSeconds != null)
            .Select(t => t.EntityId)
            .ToListAsync(cancellationToken)).ToHashSet();

        var hasMediaSource = (await db.MediaSources.AsNoTracking()
            .Where(source => ids.Contains(source.EntityId) && source.DurationSeconds != null)
            .Select(source => source.EntityId)
            .ToListAsync(cancellationToken)).ToHashSet();

        var hasFingerprint = (await db.EntityFileFingerprints.AsNoTracking()
            .Where(f => ids.Contains(f.EntityId) && f.Algorithm == FingerprintAlgorithm.Md5)
            .Select(f => f.EntityId)
            .ToListAsync(cancellationToken)).ToHashSet();

        var hasThumbnail = (await db.EntityFiles.AsNoTracking()
            .Where(f => ids.Contains(f.EntityId) && f.Role == EntityFileRole.Thumbnail)
            .Select(f => f.EntityId)
            .ToListAsync(cancellationToken)).ToHashSet();

        var hasTrickplay = (await db.TrickplayInfos.AsNoTracking()
            .Where(t => ids.Contains(t.EntityId) && t.ThumbnailCount > 0)
            .Select(t => t.EntityId)
            .ToListAsync(cancellationToken)).ToHashSet();

        var subtitlesExtracted = (await db.VideoDetails.AsNoTracking()
            .Where(v => ids.Contains(v.EntityId) && v.SubtitlesExtractedAt != null)
            .Select(v => v.EntityId)
            .ToListAsync(cancellationToken)).ToHashSet();
        var subtitleRows = await db.EntitySubtitles.AsNoTracking()
            .Where(subtitle => ids.Contains(subtitle.EntityId))
            .Select(subtitle => new { subtitle.EntityId, subtitle.StoragePath })
            .ToListAsync(cancellationToken);
        var subtitlesByEntity = subtitleRows
            .GroupBy(subtitle => subtitle.EntityId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var hasUsableSubtitleState = subtitlesExtracted
            .Where(id => !subtitlesByEntity.TryGetValue(id, out var rows) ||
                rows.All(row => File.Exists(row.StoragePath)))
            .ToHashSet();

        var result = new Dictionary<Guid, DownstreamNeeds>(ids.Count);
        foreach (var id in ids) {
            result[id] = new DownstreamNeeds(
                NeedsProbe: !hasTechnical.Contains(id) || !hasMediaSource.Contains(id),
                NeedsFingerprint: !hasFingerprint.Contains(id),
                NeedsPreview: !hasThumbnail.Contains(id),
                NeedsTrickplay: !hasTrickplay.Contains(id),
                NeedsSubtitleExtraction: !hasUsableSubtitleState.Contains(id));
        }

        return result;
    }

    // ── Reads for downstream chaining decisions ──

    public Task<bool> HasEntityTechnicalAsync(Guid entityId, CancellationToken cancellationToken) =>
        db.EntityTechnical.AnyAsync(t => t.EntityId == entityId && t.DurationSeconds != null, cancellationToken);

    public Task<bool> HasEntityFingerprintAsync(Guid entityId, FingerprintAlgorithm algorithm, CancellationToken cancellationToken) =>
        db.EntityFileFingerprints.AnyAsync(f => f.EntityId == entityId && f.Algorithm == algorithm, cancellationToken);

    public Task<bool> HasEntityFileAsync(Guid entityId, EntityFileRole role, CancellationToken cancellationToken) =>
        db.EntityFiles.AnyAsync(f => f.EntityId == entityId && f.Role == role, cancellationToken);

    public async Task<bool> HasSubtitlesExtractedAsync(Guid entityId, CancellationToken cancellationToken) {
        var detail = await db.VideoDetails.AsNoTracking()
            .FirstOrDefaultAsync(v => v.EntityId == entityId, cancellationToken);
        return detail?.SubtitlesExtractedAt is not null;
    }

    // ── Entity technical / file / fingerprint writes ──

    public async Task UpsertEntityTechnicalAsync(Guid entityId, double? duration, int? width, int? height,
        double? frameRate, int? bitRate, int? sampleRate, int? channels,
        string? codec, string? container, string? format, CancellationToken cancellationToken) {
        var existing = await db.EntityTechnical.FindAsync([entityId], cancellationToken);
        var now = DateTimeOffset.UtcNow;

        if (existing is not null) {
            existing.DurationSeconds = duration ?? existing.DurationSeconds;
            existing.Width = width ?? existing.Width;
            existing.Height = height ?? existing.Height;
            existing.FrameRate = frameRate ?? existing.FrameRate;
            existing.BitRate = bitRate ?? existing.BitRate;
            existing.SampleRate = sampleRate ?? existing.SampleRate;
            existing.Channels = channels ?? existing.Channels;
            existing.Codec = codec ?? existing.Codec;
            existing.Container = container ?? existing.Container;
            existing.Format = format ?? existing.Format;
            existing.UpdatedAt = now;
        } else {
            db.EntityTechnical.Add(new EntityTechnicalRow {
                EntityId = entityId,
                DurationSeconds = duration,
                Width = width,
                Height = height,
                FrameRate = frameRate,
                BitRate = bitRate,
                SampleRate = sampleRate,
                Channels = channels,
                Codec = codec,
                Container = container,
                Format = format,
                UpdatedAt = now
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpsertMediaSourceAsync(
        Guid entityId,
        string path,
        MediaSourceProbeData source,
        IReadOnlyList<MediaStreamProbeData> streams,
        CancellationToken cancellationToken) {
        var now = DateTimeOffset.UtcNow;
        var sourceFileId = await GetSourceFileIdAsync(entityId, cancellationToken);
        var existing = await db.MediaSources
            .FirstOrDefaultAsync(row => row.EntityId == entityId && row.Path == path, cancellationToken);

        if (existing is null) {
            existing = new MediaSourceRow {
                Id = Guid.NewGuid(),
                EntityId = entityId,
                Path = path,
                Protocol = "File",
                CreatedAt = now
            };
            db.MediaSources.Add(existing);
        }

        existing.EntityFileId = sourceFileId;
        existing.Container = source.Container ?? existing.Container;
        existing.Name = Path.GetFileName(path);
        existing.SizeBytes = source.SizeBytes ?? existing.SizeBytes ?? TryGetFileSize(path);
        existing.DurationSeconds = source.DurationSeconds ?? existing.DurationSeconds;
        existing.BitRate = source.BitRate ?? existing.BitRate;
        existing.VideoCodec = source.VideoCodec ?? existing.VideoCodec;
        existing.AudioCodec = source.AudioCodec ?? existing.AudioCodec;
        existing.Width = source.Width ?? existing.Width;
        existing.Height = source.Height ?? existing.Height;
        existing.FrameRate = source.FrameRate ?? existing.FrameRate;
        existing.UpdatedAt = now;

        var previousStreams = await db.MediaStreams
            .Where(row => row.MediaSourceId == existing.Id)
            .ToListAsync(cancellationToken);
        db.MediaStreams.RemoveRange(previousStreams);

        foreach (var stream in streams) {
            db.MediaStreams.Add(new MediaStreamRow {
                Id = Guid.NewGuid(),
                MediaSourceId = existing.Id,
                EntityId = entityId,
                StreamIndex = stream.StreamIndex,
                Type = stream.Type,
                Codec = stream.Codec,
                Language = stream.Language,
                Title = stream.Title,
                Width = stream.Width,
                Height = stream.Height,
                FrameRate = stream.FrameRate,
                BitRate = stream.BitRate,
                SampleRate = stream.SampleRate,
                Channels = stream.Channels,
                PixelFormat = stream.PixelFormat,
                BitDepth = stream.BitDepth,
                ColorRange = stream.ColorRange,
                ColorSpace = stream.ColorSpace,
                ColorTransfer = stream.ColorTransfer,
                ColorPrimaries = stream.ColorPrimaries,
                DvProfile = stream.DvProfile,
                DvLevel = stream.DvLevel,
                RpuPresentFlag = stream.RpuPresentFlag,
                ElPresentFlag = stream.ElPresentFlag,
                BlPresentFlag = stream.BlPresentFlag,
                DvBlSignalCompatibilityId = stream.DvBlSignalCompatibilityId,
                Hdr10PlusPresentFlag = stream.Hdr10PlusPresentFlag,
                IsDefault = stream.IsDefault,
                IsForced = stream.IsForced,
                CreatedAt = now
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpsertTrickplayInfoAsync(
        Guid entityId,
        TrickplayInfoData info,
        CancellationToken cancellationToken) {
        var now = DateTimeOffset.UtcNow;
        var existing = await db.TrickplayInfos.FindAsync([entityId, info.Width], cancellationToken);
        if (existing is null) {
            existing = new TrickplayInfoRow {
                EntityId = entityId,
                Width = info.Width,
                CreatedAt = now
            };
            db.TrickplayInfos.Add(existing);
        }

        existing.Height = info.Height;
        existing.TileWidth = info.TileWidth;
        existing.TileHeight = info.TileHeight;
        existing.ThumbnailCount = info.ThumbnailCount;
        existing.IntervalSeconds = info.IntervalSeconds;
        existing.Bandwidth = info.Bandwidth;
        existing.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpsertEntityFileAsync(Guid entityId, EntityFileRole role, string path, string? mimeType, long? sizeBytes, CancellationToken cancellationToken) {
        var existing = await db.EntityFiles
            .FirstOrDefaultAsync(f => f.EntityId == entityId && f.Role == role, cancellationToken);
        var now = DateTimeOffset.UtcNow;

        if (existing is not null) {
            // Scan-generated writes never overwrite user-uploaded custom assets.
            if (existing.Source == "custom")
                return;

            existing.Path = path;
            existing.MimeType = mimeType ?? existing.MimeType;
            existing.SizeBytes = sizeBytes ?? existing.SizeBytes;
            existing.UpdatedAt = now;
        } else {
            db.EntityFiles.Add(new EntityFileRow {
                Id = Guid.NewGuid(),
                EntityId = entityId,
                Role = role,
                Path = path,
                MimeType = mimeType,
                SizeBytes = sizeBytes,
                Source = "scan",
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpsertEntityFingerprintAsync(Guid entityId, FingerprintAlgorithm algorithm, string value, Guid? entityFileId, CancellationToken cancellationToken) {
        var existing = await db.EntityFileFingerprints
            .FirstOrDefaultAsync(f => f.EntityId == entityId && f.Algorithm == algorithm, cancellationToken);

        if (existing is not null) {
            existing.Value = value;
            existing.EntityFileId = entityFileId;
        } else {
            db.EntityFileFingerprints.Add(new EntityFileFingerprintRow {
                Id = Guid.NewGuid(),
                EntityId = entityId,
                EntityFileId = entityFileId,
                Algorithm = algorithm,
                Value = value,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<Guid?> GetSourceFileIdAsync(Guid entityId, CancellationToken cancellationToken) {
        return await db.EntityFiles.AsNoTracking()
            .Where(f => f.EntityId == entityId && f.Role == EntityFileRole.Source)
            .Select(f => (Guid?)f.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<string?> GetSourceFilePathAsync(Guid entityId, CancellationToken cancellationToken) {
        return await db.EntityFiles.AsNoTracking()
            .Where(f => f.EntityId == entityId && f.Role == EntityFileRole.Source)
            .Select(f => f.Path)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task MarkSubtitlesExtractedAsync(Guid entityId, CancellationToken cancellationToken) {
        var detail = await db.VideoDetails.FindAsync([entityId], cancellationToken);
        if (detail is not null) {
            detail.SubtitlesExtractedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task UpsertSubtitleAsync(Guid entityId, string language, string? label, string format,
        EntitySubtitleSource source, string storagePath, string sourceFormat, int streamIndex, CancellationToken cancellationToken) {
        var langKey = language;
        var streamKey = streamIndex.ToString();

        var streamMatch = await db.EntitySubtitles
            .FirstOrDefaultAsync(s => s.EntityId == entityId && s.Source == source
                && s.SourcePath == streamKey, cancellationToken);
        if (streamMatch is not null) {
            if (!string.Equals(streamMatch.Language, langKey, StringComparison.Ordinal)) {
                var languageConflict = await db.EntitySubtitles
                    .FirstOrDefaultAsync(s => s.EntityId == entityId && s.Source == source
                        && s.Language == langKey && s.Id != streamMatch.Id, cancellationToken);

                if (languageConflict is not null) {
                    langKey = streamMatch.Language;
                }
            }

            streamMatch.Language = langKey;
            streamMatch.Label = label;
            streamMatch.Format = format;
            streamMatch.StoragePath = storagePath;
            streamMatch.SourceFormat = sourceFormat;
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        var existing = await db.EntitySubtitles
            .FirstOrDefaultAsync(s => s.EntityId == entityId && s.Language == langKey
                && s.Source == source, cancellationToken);

        if (existing is not null) {
            langKey = $"{language}.{streamIndex}";
            var duplicate = await db.EntitySubtitles
                .AnyAsync(s => s.EntityId == entityId && s.Language == langKey
                    && s.Source == source, cancellationToken);
            if (duplicate)
                return;
        }

        db.EntitySubtitles.Add(new EntitySubtitleRow {
            Id = Guid.NewGuid(),
            EntityId = entityId,
            Language = langKey,
            Label = label,
            Format = format,
            Source = source,
            StoragePath = storagePath,
            SourceFormat = sourceFormat,
            SourcePath = streamKey,
            CreatedAt = DateTimeOffset.UtcNow
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpsertAudioTrackTagsAsync(Guid entityId, string? artist, string? album, CancellationToken cancellationToken) {
        var detail = await db.AudioTrackDetails.FindAsync([entityId], cancellationToken);
        if (detail is null) return;

        if (artist is not null) detail.EmbeddedArtist = artist;
        if (album is not null) detail.EmbeddedAlbum = album;

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<EntityTechnicalData?> GetEntityTechnicalAsync(Guid entityId, CancellationToken cancellationToken) {
        var row = await db.EntityTechnical.AsNoTracking()
            .FirstOrDefaultAsync(t => t.EntityId == entityId, cancellationToken);
        if (row is null) return null;

        return new EntityTechnicalData(row.DurationSeconds, row.Width, row.Height, row.FrameRate,
            row.BitRate, row.SampleRate, row.Channels, row.Codec, row.Container);
    }

    // ── Helpers ──

    private async Task<EntityRow?> FindEntityBySourcePath(string kindCode, string path, CancellationToken cancellationToken) {
        return await db.EntityFiles.AsNoTracking()
            .Where(file => file.Role == EntityFileRole.Source && file.Path == path)
            .Join(
                db.Entities,
                file => file.EntityId,
                entity => entity.Id,
                (file, entity) => entity)
            .FirstOrDefaultAsync(entity => entity.KindCode == kindCode, cancellationToken);
    }

    private async Task<EntityRow?> FindEntityBySourceValueAsync(
        string kindCode,
        string sourceCode,
        string value,
        CancellationToken cancellationToken) {
        var entityId = await db.EntitySources.AsNoTracking()
            .Where(source => source.Code == sourceCode && source.Value == value)
            .Select(source => (Guid?)source.EntityId)
            .FirstOrDefaultAsync(cancellationToken);

        if (entityId is null) return null;

        return await db.Entities
            .FirstOrDefaultAsync(entity => entity.Id == entityId.Value && entity.KindCode == kindCode, cancellationToken);
    }

    private async Task<int> RemoveStaleEntitiesBySourcePath(
        List<Guid> candidateIds, IReadOnlySet<string> validPaths, CancellationToken cancellationToken) {
        if (candidateIds.Count == 0) return 0;

        var sourcePaths = await db.EntityFiles.AsNoTracking()
            .Where(f => candidateIds.Contains(f.EntityId) && f.Role == EntityFileRole.Source)
            .Select(f => new { f.EntityId, f.Path })
            .ToListAsync(cancellationToken);

        var staleIds = sourcePaths
            .Where(sp => !validPaths.Contains(sp.Path))
            .Select(sp => sp.EntityId)
            .ToList();

        if (staleIds.Count == 0) return 0;

        var entitiesToRemove = await db.Entities
            .Where(e => staleIds.Contains(e.Id))
            .ToListAsync(cancellationToken);

        db.Entities.RemoveRange(entitiesToRemove);
        await db.SaveChangesAsync(cancellationToken);

        return entitiesToRemove.Count;
    }

    private static bool IsPathUnderRoot(string path, string rootPath) {
        var normalizedPath = NormalizePath(path);
        var normalizedRoot = NormalizePath(rootPath);

        if (string.IsNullOrWhiteSpace(normalizedPath) || string.IsNullOrWhiteSpace(normalizedRoot)) {
            return false;
        }

        return normalizedPath.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
            normalizedPath.StartsWith(normalizedRoot + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path) =>
        path.Replace('\\', '/').TrimEnd('/');

    private static long? TryGetFileSize(string path) {
        try { return new FileInfo(path).Length; } catch { return null; }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<EntityRefreshTarget>> GetEntityTreeAsync(
        Guid entityId, CancellationToken cancellationToken) {
        var root = await db.Entities.AsNoTracking()
            .Where(e => e.Id == entityId && e.DeletedAt == null)
            .Select(e => new EntityRefreshTarget(e.Id, e.KindCode, e.Title))
            .FirstOrDefaultAsync(cancellationToken);
        if (root is null) return [];

        var result = new List<EntityRefreshTarget> { root };
        var parentIds = new List<Guid> { entityId };

        // Walk up to 3 levels of children (series → seasons → episodes).
        for (var depth = 0; depth < 3 && parentIds.Count > 0; depth++) {
            var children = await db.Entities.AsNoTracking()
                .Where(e => e.DeletedAt == null && e.ParentEntityId != null && parentIds.Contains(e.ParentEntityId.Value))
                .Select(e => new EntityRefreshTarget(e.Id, e.KindCode, e.Title))
                .ToArrayAsync(cancellationToken);
            if (children.Length == 0) break;
            result.AddRange(children);
            parentIds = children.Select(c => c.Id).ToList();
        }

        return result;
    }

    private static LibraryRootData ToData(LibraryRootRow row) =>
        new(row.Id, row.Path, row.Label, row.Enabled, row.Recursive,
            row.ScanVideos, row.ScanImages, row.ScanAudio, row.ScanBooks, row.IsNsfw);
}
