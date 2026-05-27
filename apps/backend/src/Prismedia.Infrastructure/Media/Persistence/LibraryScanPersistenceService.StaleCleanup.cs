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
    // ── Stale entity cleanup ──

    public async Task<int> RemoveStaleVideosByRootAsync(Guid rootId, IReadOnlySet<string> validPaths, CancellationToken cancellationToken) {
        var videoIds = await _db.VideoDetails.AsNoTracking()
            .Where(vd => vd.LibraryRootId == rootId)
            .Select(vd => vd.EntityId)
            .ToListAsync(cancellationToken);

        var rootPath = await _db.LibraryRoots.AsNoTracking()
            .Where(root => root.Id == rootId)
            .Select(root => root.Path)
            .FirstOrDefaultAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(rootPath)) {
            var videoCode = EntityKindRegistry.Video.Code;
            var sourceFiles = await _db.EntityFiles.AsNoTracking()
                .Where(file => file.Role == EntityFileRole.Source)
                .Join(
                    _db.Entities.AsNoTracking().Where(entity => entity.KindCode == videoCode),
                    file => file.EntityId,
                    entity => entity.Id,
                    (file, entity) => new { file.EntityId, file.Path })
                .ToListAsync(cancellationToken);

            videoIds.AddRange(sourceFiles
                .Where(file => LibraryScanPathRules.IsPathUnderRoot(file.Path, rootPath))
                .Select(file => file.EntityId));
        }

        videoIds = videoIds.Distinct().ToList();
        return await RemoveStaleEntitiesBySourcePath(videoIds, validPaths, cancellationToken);
    }

    public async Task<int> RemoveStaleLooseImagesInRootAsync(Guid rootId, IReadOnlySet<string> validPaths, CancellationToken cancellationToken) {
        var imageIds = await GetLooseRootEntityIdsAsync(rootId, EntityKindRegistry.Image.Code, cancellationToken);
        return await RemoveStaleEntitiesBySourcePath(imageIds, validPaths, cancellationToken);
    }

    public async Task<int> RemoveStaleImagesInGalleryAsync(Guid galleryEntityId, IReadOnlySet<string> validPaths, CancellationToken cancellationToken) {
        var childIds = await _db.Entities.AsNoTracking()
            .Where(entity => entity.ParentEntityId == galleryEntityId && entity.KindCode == EntityKindRegistry.Image.Code)
            .Select(entity => entity.Id)
            .ToListAsync(cancellationToken);

        return await RemoveStaleEntitiesBySourcePath(childIds, validPaths, cancellationToken);
    }

    public async Task<int> RemoveStaleGalleriesInRootAsync(Guid rootId, IReadOnlySet<string> validFolderPaths, CancellationToken cancellationToken) {
        var galleryIds = await _db.GalleryDetails.AsNoTracking()
            .Where(gd => gd.LibraryRootId == rootId)
            .Select(gd => gd.EntityId)
            .ToListAsync(cancellationToken);

        return await RemoveStaleContainerEntitiesBySourcePath(galleryIds, validFolderPaths, cancellationToken);
    }

    public async Task<int> RemoveStaleLooseAudioTracksInRootAsync(Guid rootId, IReadOnlySet<string> validPaths, CancellationToken cancellationToken) {
        var trackIds = await GetLooseRootEntityIdsAsync(rootId, EntityKindRegistry.AudioTrack.Code, cancellationToken);
        return await RemoveStaleEntitiesBySourcePath(trackIds, validPaths, cancellationToken);
    }

    public async Task<int> RemoveStaleAudioTracksInLibraryAsync(Guid libraryEntityId, IReadOnlySet<string> validPaths, CancellationToken cancellationToken) {
        var childIds = await _db.Entities.AsNoTracking()
            .Where(entity => entity.ParentEntityId == libraryEntityId && entity.KindCode == EntityKindRegistry.AudioTrack.Code)
            .Select(entity => entity.Id)
            .ToListAsync(cancellationToken);

        return await RemoveStaleEntitiesBySourcePath(childIds, validPaths, cancellationToken);
    }

    public async Task<int> RemoveStaleAudioLibrariesInRootAsync(Guid rootId, IReadOnlySet<string> validFolderPaths, CancellationToken cancellationToken) {
        var libraryIds = await _db.AudioLibraryDetails.AsNoTracking()
            .Where(ald => ald.LibraryRootId == rootId)
            .Select(ald => ald.EntityId)
            .ToListAsync(cancellationToken);

        return await RemoveStaleContainerEntitiesBySourcePath(libraryIds, validFolderPaths, cancellationToken);
    }

    public async Task<int> RemoveStaleBookVolumesAsync(Guid bookEntityId, IReadOnlySet<string> validFolderPaths, CancellationToken cancellationToken) {
        var volumeIds = await _db.Entities.AsNoTracking()
            .Where(entity => entity.ParentEntityId == bookEntityId && entity.KindCode == EntityKindRegistry.BookVolume.Code)
            .Select(entity => entity.Id)
            .ToListAsync(cancellationToken);

        return await RemoveStaleEntitiesBySourcePath(volumeIds, validFolderPaths, cancellationToken);
    }

    public async Task<int> RemoveStaleBookChaptersAsync(Guid bookEntityId, IReadOnlySet<string> validArchivePaths, CancellationToken cancellationToken) {
        var chapterIds = await _db.Entities.AsNoTracking()
            .Where(entity => entity.ParentEntityId == bookEntityId && entity.KindCode == EntityKindRegistry.BookChapter.Code)
            .Select(entity => entity.Id)
            .ToListAsync(cancellationToken);

        return await RemoveStaleEntitiesBySourcePath(chapterIds, validArchivePaths, cancellationToken);
    }

    public async Task<int> RemoveStaleBooksInRootAsync(Guid rootId, IReadOnlySet<string> validPaths, CancellationToken cancellationToken) {
        var bookIds = await _db.BookDetails.AsNoTracking()
            .Where(bd => bd.LibraryRootId == rootId)
            .Select(bd => bd.EntityId)
            .ToListAsync(cancellationToken);

        return await RemoveStaleEntitiesBySourcePath(bookIds, validPaths, cancellationToken);
    }

    public async Task<int> RemoveOrphanSeriesAndSeasonsAsync(CancellationToken cancellationToken) {
        var seasonCode = EntityKindRegistry.VideoSeason.Code;
        var seriesCode = EntityKindRegistry.VideoSeries.Code;

        var orphanSeasons = await _db.Entities
            .Where(e => e.KindCode == seasonCode
                && !_db.Entities.Any(child => child.ParentEntityId == e.Id))
            .ToListAsync(cancellationToken);
        _db.Entities.RemoveRange(orphanSeasons);

        if (orphanSeasons.Count > 0)
            await _db.SaveChangesAsync(cancellationToken);

        var orphanSeries = await _db.Entities
            .Where(e => e.KindCode == seriesCode
                && !_db.Entities.Any(child => child.ParentEntityId == e.Id))
            .ToListAsync(cancellationToken);
        _db.Entities.RemoveRange(orphanSeries);

        if (orphanSeries.Count > 0)
            await _db.SaveChangesAsync(cancellationToken);

        return orphanSeasons.Count + orphanSeries.Count;
    }

}
