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

        // Suppress the work's provider identities FIRST: a monitored parent container (an author, an
        // artist, the series itself) must never re-request what the user just deleted (the "add list
        // exclusion" analog). An explicit future request clears the suppression.
        var providerRefs = await db.EntityExternalIds.AsNoTracking()
            .Where(row => row.EntityId == id)
            .Select(row => new ProviderRef(row.Provider, row.Value))
            .ToArrayAsync(cancellationToken);
        if (providerRefs.Length > 0 && EntityKindRegistry.TryGet(entity.KindCode, out var kind)) {
            await suppressions.SuppressAsync(providerRefs, kind, entity.Title, cancellationToken);
        }

        // Tear down the acquisition pipeline for the whole tree: monitors stop re-searching, and each
        // acquisition's delete also removes its in-flight download (and data) from the download client.
        var monitors = await db.Monitors
            .Where(monitor => monitor.EntityId != null && ids.Contains(monitor.EntityId.Value))
            .ToArrayAsync(cancellationToken);
        if (monitors.Length > 0) {
            db.Monitors.RemoveRange(monitors);
            await db.SaveChangesAsync(cancellationToken);
        }

        foreach (var entityId in ids) {
            foreach (var acquisitionId in await acquisitions.ListIdsForEntityAsync(entityId, cancellationToken)) {
                await acquisitions.DeleteAsync(acquisitionId, cancellationToken);
            }
        }

        var filesDeleted = 0;
        if (deleteFiles) {
            filesDeleted = await DeleteSourcePathsAsync(ids, cancellationToken);
        }

        var rows = await db.Entities.Where(row => ids.Contains(row.Id)).ToArrayAsync(cancellationToken);
        db.Entities.RemoveRange(rows);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "MediaEntityDeletion: deleted \"{Title}\" ({Kind}) — {Rows} entities, {Files} on-disk paths removed.",
            entity.Title, entity.KindCode, rows.Length, filesDeleted);
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
