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

    public async Task<int> RemoveStalePlayableVideosByRootAsync(Guid rootId, IReadOnlySet<string> validPaths, CancellationToken cancellationToken) {
        var playableCodes = EntityKindRegistry.All
            .OfType<IPlayableVideoKindDefinition>()
            .Select(definition => definition.Kind.ToCode())
            .ToArray();
        var videoIds = await _db.EntityLibraryRoots.AsNoTracking()
            .Where(root => root.LibraryRootId == rootId)
            .Join(
                _db.Entities.AsNoTracking().Where(entity => playableCodes.Contains(entity.KindCode)),
                root => root.EntityId,
                entity => entity.Id,
                (root, entity) => entity.Id)
            .ToListAsync(cancellationToken);

        var rootPath = await _db.LibraryRoots.AsNoTracking()
            .Where(root => root.Id == rootId)
            .Select(root => root.Path)
            .FirstOrDefaultAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(rootPath)) {
            var sourceFiles = await _db.EntityFiles.AsNoTracking()
                .Where(file => file.Role == EntityFileRole.Source)
                .Join(
                    _db.Entities.AsNoTracking().Where(entity => playableCodes.Contains(entity.KindCode)),
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

    public async Task<int> RemoveStaleMoviesByRootAsync(Guid rootId, IReadOnlySet<string> validFolderPaths, CancellationToken cancellationToken) {
        var rootPath = await _db.LibraryRoots.AsNoTracking()
            .Where(root => root.Id == rootId)
            .Select(root => root.Path)
            .FirstOrDefaultAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(rootPath)) {
            return 0;
        }

        var movieCode = EntityKindRegistry
            .PlayableVideoKindFor(PlayableVideoScanPlacement.Movie)
            .ToCode();
        var movieSources = await _db.EntitySources.AsNoTracking()
            .Where(source => source.Code == EntitySourceCode.Folder.ToCode())
            .Join(
                _db.Entities.AsNoTracking().Where(entity => entity.KindCode == movieCode),
                source => source.EntityId,
                entity => entity.Id,
                (source, entity) => new { source.EntityId, source.Value })
            .ToListAsync(cancellationToken);
        var movieIds = movieSources
            .Where(source => LibraryScanPathRules.IsPathUnderRoot(source.Value, rootPath))
            .Select(source => source.EntityId)
            .ToList();

        var staleMovieIds = await GetStaleEntityIdsBySourceValueAsync(
            movieIds,
            EntitySourceCode.Folder,
            validFolderPaths,
            cancellationToken);
        if (staleMovieIds.Count == 0) {
            return 0;
        }

        return await RemoveEntitiesByIdAsync(staleMovieIds, cancellationToken);
    }

    public async Task<int> RemoveStaleLooseImagesInRootAsync(Guid rootId, IReadOnlySet<string> validPaths, CancellationToken cancellationToken) {
        var imageIds = await GetLooseRootEntityIdsAsync(rootId, EntityKind.Image.ToCode(), cancellationToken);
        return await RemoveStaleEntitiesBySourcePath(imageIds, validPaths, cancellationToken);
    }

    public async Task<int> RemoveStaleImagesInGalleryAsync(Guid galleryEntityId, IReadOnlySet<string> validPaths, CancellationToken cancellationToken) {
        var childIds = await _db.Entities.AsNoTracking()
            .Where(entity => entity.ParentEntityId == galleryEntityId && entity.KindCode == EntityKind.Image.ToCode())
            .Select(entity => entity.Id)
            .ToListAsync(cancellationToken);

        return await RemoveStaleEntitiesBySourcePath(childIds, validPaths, cancellationToken);
    }

    public async Task<int> RemoveStaleGalleriesInRootAsync(Guid rootId, IReadOnlySet<string> validFolderPaths, CancellationToken cancellationToken) {
        var galleryIds = await DirectRootedKindIdsAsync(rootId, EntityKind.Gallery, cancellationToken);

        return await RemoveStaleContainerEntitiesBySourcePath(galleryIds, validFolderPaths, cancellationToken);
    }

    public async Task<int> RemoveStaleLooseAudioTracksInRootAsync(Guid rootId, IReadOnlySet<string> validPaths, CancellationToken cancellationToken) {
        var trackIds = await GetLooseRootEntityIdsAsync(rootId, EntityKind.AudioTrack.ToCode(), cancellationToken);
        return await RemoveStaleEntitiesBySourcePath(trackIds, validPaths, cancellationToken);
    }

    public async Task<int> RemoveStaleAudioTracksInLibraryAsync(Guid libraryEntityId, IReadOnlySet<string> validPaths, CancellationToken cancellationToken) {
        var childIds = await _db.Entities.AsNoTracking()
            .Where(entity => entity.ParentEntityId == libraryEntityId && entity.KindCode == EntityKind.AudioTrack.ToCode())
            .Select(entity => entity.Id)
            .ToListAsync(cancellationToken);

        return await RemoveStaleEntitiesBySourcePath(childIds, validPaths, cancellationToken);
    }

    public async Task<int> RemoveStaleAudioLibrariesInRootAsync(Guid rootId, IReadOnlySet<string> validFolderPaths, CancellationToken cancellationToken) {
        var libraryIds = await DirectRootedKindIdsAsync(rootId, EntityKind.AudioLibrary, cancellationToken);

        return await RemoveStaleContainerEntitiesBySourcePath(libraryIds, validFolderPaths, cancellationToken);
    }

    public async Task<int> RemoveStaleMusicArtistsInRootAsync(Guid rootId, IReadOnlySet<string> validFolderPaths, CancellationToken cancellationToken) {
        var artistIds = await DirectRootedKindIdsAsync(rootId, EntityKind.MusicArtist, cancellationToken);

        return await RemoveStaleContainerEntitiesBySourcePath(artistIds, validFolderPaths, cancellationToken);
    }

    public async Task<int> RemoveStaleBookVolumesAsync(Guid bookEntityId, IReadOnlySet<string> validFolderPaths, CancellationToken cancellationToken) {
        var volumeIds = await _db.Entities.AsNoTracking()
            .Where(entity => entity.ParentEntityId == bookEntityId && entity.KindCode == EntityKind.BookVolume.ToCode())
            .Select(entity => entity.Id)
            .ToListAsync(cancellationToken);

        return await RemoveStaleEntitiesBySourcePath(volumeIds, validFolderPaths, cancellationToken);
    }

    public async Task<int> RemoveStaleBookChaptersAsync(Guid bookEntityId, IReadOnlySet<string> validArchivePaths, CancellationToken cancellationToken) {
        var chapterIds = await _db.Entities.AsNoTracking()
            .Where(entity => entity.ParentEntityId == bookEntityId && entity.KindCode == EntityKind.BookChapter.ToCode())
            .Select(entity => entity.Id)
            .ToListAsync(cancellationToken);

        return await RemoveStaleEntitiesBySourcePath(chapterIds, validArchivePaths, cancellationToken);
    }

    public async Task<int> RemoveStaleBooksInRootAsync(Guid rootId, IReadOnlySet<string> validPaths, CancellationToken cancellationToken) {
        var bookIds = await DirectRootedKindIdsAsync(rootId, EntityKind.Book, cancellationToken);

        return await RemoveStaleEntitiesBySourcePath(bookIds, validPaths, cancellationToken);
    }

    public async Task<int> RemoveEntitiesOutsideLibraryRootsAsync(CancellationToken cancellationToken) {
        var rootPaths = (await _db.LibraryRoots.AsNoTracking()
                .Select(root => root.Path)
                .ToArrayAsync(cancellationToken))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToArray();

        var sourceRows = await _db.EntityFiles.AsNoTracking()
            .Where(file => file.Role == EntityFileRole.Source)
            .Select(file => new { file.EntityId, file.Path })
            .ToArrayAsync(cancellationToken);

        var validSourceIds = sourceRows
            .Where(file => rootPaths.Any(rootPath => LibraryScanPathRules.IsPathUnderRoot(file.Path, rootPath)))
            .Select(file => file.EntityId)
            .ToHashSet();
        var idsToRemove = sourceRows
            .Select(file => file.EntityId)
            .Distinct()
            .Where(entityId => !validSourceIds.Contains(entityId))
            .ToHashSet();

        if (idsToRemove.Count > 0) {
            var allEntityParents = await _db.Entities.AsNoTracking()
                .Where(entity => entity.ParentEntityId != null)
                .Select(entity => new { entity.Id, ParentEntityId = entity.ParentEntityId!.Value })
                .ToArrayAsync(cancellationToken);
            var childrenByParentId = allEntityParents
                .GroupBy(entity => entity.ParentEntityId)
                .ToDictionary(group => group.Key, group => group.Select(entity => entity.Id).ToArray());
            var pending = new Queue<Guid>(idsToRemove);

            while (pending.Count > 0) {
                var parentId = pending.Dequeue();
                if (!childrenByParentId.TryGetValue(parentId, out var childIds)) {
                    continue;
                }

                foreach (var childId in childIds) {
                    if (validSourceIds.Contains(childId) || !idsToRemove.Add(childId)) {
                        continue;
                    }

                    pending.Enqueue(childId);
                }
            }
        }

        var removed = idsToRemove.Count == 0
            ? 0
            : await RemoveEntitiesByIdAsync(idsToRemove.ToList(), cancellationToken);
        return removed + await RemoveOrphanSeriesAndSeasonsAsync(cancellationToken);
    }

    public Task<int> RemoveOrphanSeriesAndSeasonsAsync(CancellationToken cancellationToken) =>
        DerivedEntityContainerPruner.PruneAsync(_db, SaveChangesWithLifecycleAsync, cancellationToken);

    public async Task<int> RemoveOrphanTagsAsync(CancellationToken cancellationToken) {
        var tagCode = EntityKind.Tag.ToCode();
        var links = _db.EntityRelationshipLinks;

        // Orphan tags are those nothing references (no inbound relationship link) — the same
        // predicate the "No references" grid filter uses. Hard delete; their own outbound links,
        // if any, cascade away.
        var orphanTags = await _db.Entities
            .Where(e => e.KindCode == tagCode && !links.Any(link => link.TargetEntityId == e.Id))
            .ToListAsync(cancellationToken);
        if (orphanTags.Count > 0) {
            _db.Entities.RemoveRange(orphanTags);
            await SaveChangesWithLifecycleAsync(cancellationToken);
        }

        return orphanTags.Count;
    }

}
