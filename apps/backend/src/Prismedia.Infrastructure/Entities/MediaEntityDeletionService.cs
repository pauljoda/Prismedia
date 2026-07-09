using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Entities;
using Prismedia.Application.Files;
using Prismedia.Application.Jobs;
using Prismedia.Application.Requests;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Entities;

/// <summary>
/// EF-backed implementation of <see cref="IMediaEntityDeletionService"/>: resolves the entity's
/// descendant tree, tears down acquisition state (monitors, downloads), suppresses provider identities
/// against re-request, deletes source paths from disk through the managed storage (watched roots only),
/// hard-deletes the rows (capability tables cascade), and queues the affected kinds' scans to settle
/// bookkeeping.
/// </summary>
public sealed class MediaEntityDeletionService(
    PrismediaDbContext db,
    IFilesPersistence roots,
    IManagedFileStorage storage,
    IWantedSuppressionStore suppressions,
    IAcquisitionRequestService acquisitions,
    IJobQueueService jobs,
    ILogger<MediaEntityDeletionService> logger) : IMediaEntityDeletionService {
    /// <summary>How deep the descendant walk goes (a series → season → episode tree is three levels; books and audio are shallower).</summary>
    private const int MaxDescendantDepth = 6;

    /// <summary>
    /// Kind codes deletable through this service: the file-backed media kinds whose grids represent
    /// real things on disk. Taxonomy kinds (tag/person/studio) keep the detach-only
    /// <see cref="EntityManagementService"/> path, and collections keep their own delete.
    /// </summary>
    private static readonly HashSet<string> DeletableKindCodes = new(StringComparer.OrdinalIgnoreCase) {
        EntityKindRegistry.Video.Code,
        EntityKindRegistry.VideoSeries.Code,
        EntityKindRegistry.VideoSeason.Code,
        EntityKindRegistry.Movie.Code,
        EntityKindRegistry.Gallery.Code,
        EntityKindRegistry.Image.Code,
        EntityKindRegistry.Book.Code,
        EntityKindRegistry.BookAuthor.Code,
        EntityKindRegistry.BookVolume.Code,
        EntityKindRegistry.AudioLibrary.Code,
        EntityKindRegistry.AudioTrack.Code,
        EntityKindRegistry.MusicArtist.Code,
    };

    /// <summary>Whether the given kind code may be deleted (with files) through this service.</summary>
    public static bool IsDeletableKind(string? kind) =>
        !string.IsNullOrWhiteSpace(kind) && DeletableKindCodes.Contains(kind);

    /// <inheritdoc />
    public async Task<MediaEntityDeleteResult> DeleteAsync(Guid id, bool deleteFiles, CancellationToken cancellationToken) {
        var entity = await db.Entities.AsNoTracking()
            .Where(row => row.Id == id)
            .Select(row => new { row.KindCode, row.Title })
            .FirstOrDefaultAsync(cancellationToken);
        if (entity is null) {
            return new MediaEntityDeleteResult(false, "The entity no longer exists.");
        }

        if (!IsDeletableKind(entity.KindCode)) {
            return new MediaEntityDeleteResult(false, $"Entities of kind '{entity.KindCode}' cannot be deleted this way.");
        }

        var ids = await CollectTreeAsync(id, cancellationToken);

        // Delete manages DISK state; monitoring is managed separately and decides what deletion means.
        // An active container monitor on the entity or any ancestor says "keep chasing this content" —
        // so the delete reverts to wanted instead of removing from the library.
        var watchedIds = ids.Concat(await CollectAncestorsAsync(id, cancellationToken)).ToArray();
        var reverting = await db.Monitors.AsNoTracking().AnyAsync(
            monitor => monitor.Status == MonitorStatus.Active
                && monitor.EntityId != null
                && watchedIds.Contains(monitor.EntityId.Value),
            cancellationToken);

        // Tear down in-flight acquisitions either way: their downloads (and data) leave the client, and
        // their per-item helper monitors go with them (in revert mode the surviving container monitor
        // re-requests each gap with a fresh acquisition+monitor pair; stale ones would only linger paused).
        var acquisitionIds = new List<Guid>();
        foreach (var entityId in ids) {
            acquisitionIds.AddRange(await acquisitions.ListIdsForEntityAsync(entityId, cancellationToken));
        }

        var doomedMonitors = await db.Monitors
            .Where(monitor =>
                (monitor.AcquisitionId != null && acquisitionIds.Contains(monitor.AcquisitionId.Value))
                // Container monitors inside the tree are removed only on full removal — reverting keeps
                // them, since they ARE the loop that re-acquires the reverted content.
                || (!reverting && monitor.EntityId != null && ids.Contains(monitor.EntityId.Value)))
            .ToArrayAsync(cancellationToken);
        if (doomedMonitors.Length > 0) {
            db.Monitors.RemoveRange(doomedMonitors);
            await db.SaveChangesAsync(cancellationToken);
        }

        foreach (var acquisitionId in acquisitionIds) {
            await acquisitions.DeleteAsync(acquisitionId, cancellationToken);
        }

        var filesDeleted = 0;
        if (deleteFiles) {
            filesDeleted = await DeleteSourcePathsAsync(ids, cancellationToken);
        }

        if (reverting) {
            // Revert: the entities stay in the library as wanted placeholders (source bindings cleared),
            // and NOTHING is suppressed — the monitoring loop is supposed to re-acquire this content.
            var sourceRows = await db.EntityFiles
                .Where(file => ids.Contains(file.EntityId) && file.Role == EntityFileRole.Source)
                .ToArrayAsync(cancellationToken);
            db.EntityFiles.RemoveRange(sourceRows);
            var rows = await db.Entities.Where(row => ids.Contains(row.Id)).ToArrayAsync(cancellationToken);
            var now = DateTimeOffset.UtcNow;
            foreach (var row in rows) {
                row.IsWanted = true;
                row.UpdatedAt = now;
            }

            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation(
                "MediaEntityDeletion: reverted \"{Title}\" ({Kind}) to wanted — {Rows} entities kept, {Files} on-disk paths removed.",
                entity.Title, entity.KindCode, rows.Length, filesDeleted);
            return new MediaEntityDeleteResult(true, FilesDeleted: filesDeleted, Reverted: true);
        }

        // Full removal: suppress the work's provider identities so a monitored parent container (an
        // author, an artist) never re-requests what the user just deleted (the "add list exclusion"
        // analog). An explicit future request clears the suppression.
        var providerRefs = await db.EntityExternalIds.AsNoTracking()
            .Where(row => row.EntityId == id)
            .Select(row => new ProviderRef(row.Provider, row.Value))
            .ToArrayAsync(cancellationToken);
        if (providerRefs.Length > 0 && EntityKindRegistry.TryGet(entity.KindCode, out var kind)) {
            await suppressions.SuppressAsync(providerRefs, kind, entity.Title, cancellationToken);
        }

        var removedRows = await db.Entities.Where(row => ids.Contains(row.Id)).ToArrayAsync(cancellationToken);
        db.Entities.RemoveRange(removedRows);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "MediaEntityDeletion: deleted \"{Title}\" ({Kind}) — {Rows} entities, {Files} on-disk paths removed.",
            entity.Title, entity.KindCode, removedRows.Length, filesDeleted);
        return new MediaEntityDeleteResult(true, FilesDeleted: filesDeleted);
    }

    /// <summary>The entity and every descendant (breadth-first over ParentEntityId, bounded).</summary>
    private async Task<List<Guid>> CollectTreeAsync(Guid id, CancellationToken cancellationToken) {
        var ids = new List<Guid> { id };
        var frontier = new List<Guid> { id };
        for (var depth = 0; depth < MaxDescendantDepth && frontier.Count > 0; depth++) {
            frontier = await db.Entities.AsNoTracking()
                .Where(row => row.ParentEntityId != null && frontier.Contains(row.ParentEntityId.Value))
                .Select(row => row.Id)
                .ToListAsync(cancellationToken);
            ids.AddRange(frontier);
        }

        return ids;
    }

    /// <summary>The entity's ancestor chain (bounded walk up ParentEntityId), for the monitored-ancestor check.</summary>
    private async Task<List<Guid>> CollectAncestorsAsync(Guid id, CancellationToken cancellationToken) {
        var ancestors = new List<Guid>();
        var currentId = await db.Entities.AsNoTracking()
            .Where(row => row.Id == id)
            .Select(row => row.ParentEntityId)
            .FirstOrDefaultAsync(cancellationToken);
        for (var depth = 0; currentId is { } parentId && depth < MaxDescendantDepth; depth++) {
            ancestors.Add(parentId);
            currentId = await db.Entities.AsNoTracking()
                .Where(row => row.Id == parentId)
                .Select(row => row.ParentEntityId)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return ancestors;
    }

    /// <summary>
    /// Permanently deletes the tree's source paths from disk. Only the top-level paths are deleted
    /// (a series folder subsumes its season folders and episode files), and only paths inside a watched
    /// library root — anything else is skipped and logged, never deleted. A path already gone counts as
    /// done. Enabled roots whose kinds were touched get a scan queued to settle scan bookkeeping.
    /// </summary>
    private async Task<int> DeleteSourcePathsAsync(List<Guid> ids, CancellationToken cancellationToken) {
        var paths = await db.EntityFiles.AsNoTracking()
            .Where(file => ids.Contains(file.EntityId) && file.Role == EntityFileRole.Source)
            .Select(file => file.Path)
            .Distinct()
            .ToListAsync(cancellationToken);
        if (paths.Count == 0) {
            return 0;
        }

        var allRoots = await roots.ListRootsAsync(cancellationToken);
        var deleted = 0;
        var touchedRoots = new List<FileLibraryRoot>();
        foreach (var path in TopLevelPaths(paths)) {
            var root = allRoots.FirstOrDefault(root => IsUnder(root.Path, path));
            if (root is null) {
                logger.LogWarning("MediaEntityDeletion: skipping \"{Path}\" — not under any watched library root.", path);
                continue;
            }

            try {
                await storage.DeleteAsync(
                    new ResolvedFilePath(root, Path.GetRelativePath(root.Path, path), path),
                    cancellationToken);
                deleted++;
                touchedRoots.Add(root);
            } catch (OperationCanceledException) {
                throw;
            } catch (FileNotFoundException) {
                deleted++;
            } catch (DirectoryNotFoundException) {
                deleted++;
            } catch (Exception ex) {
                logger.LogWarning(ex, "MediaEntityDeletion: failed to delete \"{Path}\".", path);
            }
        }

        var scanRoots = touchedRoots.GroupBy(root => root.Id).Select(group => group.First()).Where(root => root.Enabled).ToArray();
        if (scanRoots.Length > 0) {
            await LibraryScanJobs.QueueScansForKindsAsync(
                jobs,
                scanRoots.Any(root => root.ScanVideos),
                scanRoots.Any(root => root.ScanImages),
                scanRoots.Any(root => root.ScanAudio),
                scanRoots.Any(root => root.ScanBooks),
                cancellationToken);
        }

        return deleted;
    }

    /// <summary>Drops paths contained in another listed path, so a folder delete isn't repeated for its children.</summary>
    private static IEnumerable<string> TopLevelPaths(IReadOnlyList<string> paths) {
        var kept = new List<string>();
        foreach (var path in paths.OrderBy(path => path.Length)) {
            if (!kept.Any(top => IsUnder(top, path))) {
                kept.Add(path);
                yield return path;
            }
        }
    }

    /// <summary>True when <paramref name="path"/> equals or lives under <paramref name="parent"/>.</summary>
    private static bool IsUnder(string parent, string path) {
        var normalizedParent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));
        var normalizedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        return normalizedPath.Equals(normalizedParent, StringComparison.Ordinal)
            || normalizedPath.StartsWith(normalizedParent + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }
}
