using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Files;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Media.Persistence;

public sealed partial class LibraryScanPersistenceService {
    /// <inheritdoc />
    public async Task InvalidateSubtitleStateAsync(
        IReadOnlyCollection<VideoSubtitleSidecarState> states,
        CancellationToken cancellationToken) {
        if (states.Count == 0) {
            return;
        }

        var signatures = states
            .GroupBy(state => state.EntityId)
            .ToDictionary(group => group.Key, group => group.Last().Signature);
        var ids = signatures.Keys.ToArray();
        var details = await _db.EntitySubtitleStates
            .Where(detail => ids.Contains(detail.EntityId))
            .ToListAsync(cancellationToken);
        var changed = false;
        foreach (var detail in details) {
            if (!signatures.TryGetValue(detail.EntityId, out var currentSignature) ||
                string.Equals(detail.SubtitleSidecarSignature, currentSignature, StringComparison.Ordinal)) {
                continue;
            }

            if (detail.SubtitlesExtractedAt is not null) {
                detail.SubtitlesExtractedAt = null;
                changed = true;
            }
        }

        if (changed) {
            await SaveChangesWithLifecycleAsync(cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<VideoRefreshSourceTarget>> GetVideoTargetsInRootAsync(
        Guid rootId,
        CancellationToken cancellationToken) {
        var root = await _db.LibraryRoots.AsNoTracking()
            .Where(row => row.Id == rootId)
            .Select(row => new { row.Path, row.Recursive })
            .FirstOrDefaultAsync(cancellationToken);
        if (root is null) {
            return [];
        }

        var scannedPaths = (await _db.ScannedFiles.AsNoTracking()
                .Where(row =>
                    row.LibraryRootId == rootId &&
                    row.ScanKind == JobType.ScanLibrary.ToCode())
                .Select(row => row.Path)
                .ToListAsync(cancellationToken))
            .ToHashSet(FileSystemPathComparison.Comparer);
        var candidates = await _db.VideoDetails.AsNoTracking()
            .Join(
                _db.Entities.AsNoTracking(),
                detail => detail.EntityId,
                entity => entity.Id,
                (detail, entity) => new { Detail = detail, Entity = entity })
            .GroupJoin(
                _db.EntityLibraryRoots.AsNoTracking(),
                joined => joined.Entity.Id,
                root => root.EntityId,
                (joined, roots) => new { joined.Detail, joined.Entity, Root = roots.FirstOrDefault() })
            .Join(
                _db.EntityFiles.AsNoTracking().Where(file => file.Role == EntityFileRole.Source),
                joined => joined.Entity.Id,
                file => file.EntityId,
                (joined, file) => new {
                    LibraryRootId = joined.Root == null ? (Guid?)null : joined.Root.LibraryRootId,
                    joined.Entity.Id,
                    joined.Entity.Title,
                    SourcePath = file.Path
                })
            .ToListAsync(cancellationToken);

        return candidates
            .Where(target =>
                target.LibraryRootId == rootId ||
                target.LibraryRootId is null &&
                (scannedPaths.Contains(target.SourcePath) || IsSourcePathCoveredByRoot(target.SourcePath, root.Path, root.Recursive)))
            .Select(target => new VideoRefreshSourceTarget(target.Id, target.Title, target.SourcePath))
            .DistinctBy(target => (target.Id, target.SourcePath))
            .OrderBy(target => target.SourcePath)
            .ThenBy(target => target.Id)
            .ToArray();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<VideoRecoveryTarget>> GetVideoRecoveryTargetsInRootAsync(
        Guid rootId,
        CancellationToken cancellationToken) {
        var targets = await GetVideoTargetsInRootAsync(rootId, cancellationToken);
        if (targets.Count == 0) {
            return [];
        }

        var needs = await CheckDownstreamNeedsBatchAsync(
            targets.Select(target => target.Id).Distinct().ToArray(),
            cancellationToken);
        return targets
            .Where(target => needs.ContainsKey(target.Id))
            .Select(target => new VideoRecoveryTarget(
                target.Id,
                target.Title,
                target.SourcePath,
                needs[target.Id]))
            .ToArray();
    }

    private static bool IsSourcePathCoveredByRoot(string sourcePath, string rootPath, bool recursive) =>
        recursive
            ? LibraryScanPathRules.IsPathUnderRoot(sourcePath, rootPath)
            : LibraryScanPathRules.IsDirectChildPath(sourcePath, rootPath);
}
