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
    private async Task EnsureVideoSeriesDetailAsync(
        Guid seriesId,
        CancellationToken cancellationToken) {
        var detail = _db.VideoSeriesDetails.Local.FirstOrDefault(row => row.EntityId == seriesId)
            ?? await _db.VideoSeriesDetails.FindAsync([seriesId], cancellationToken);
        if (detail is null) {
            _db.VideoSeriesDetails.Add(new VideoSeriesDetailRow { EntityId = seriesId });
        }
    }

    private async Task EnsureAudioTrackDetailAsync(
        Guid trackId,
        string? sectionLabel,
        int sectionOrder,
        CancellationToken cancellationToken) {
        var detail = _db.AudioTrackDetails.Local.FirstOrDefault(row => row.EntityId == trackId)
            ?? await _db.AudioTrackDetails.FindAsync([trackId], cancellationToken);
        if (detail is null) {
            _db.AudioTrackDetails.Add(new AudioTrackDetailRow {
                EntityId = trackId,
                SectionLabel = sectionLabel,
                SectionOrder = sectionOrder,
            });
            return;
        }

        detail.SectionLabel = sectionLabel;
        detail.SectionOrder = sectionOrder;
    }

    private async Task UpsertPositionAsync(
        Guid entityId,
        string code,
        int value,
        string? label,
        DateTimeOffset now,
        CancellationToken cancellationToken) {
        var position = _db.EntityPositions.Local.FirstOrDefault(row => row.EntityId == entityId && row.Code == code)
            ?? await _db.EntityPositions.FindAsync([entityId, code], cancellationToken);
        if (position is null) {
            _db.EntityPositions.Add(new EntityPositionRow {
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
        var child = _db.Entities.Local.FirstOrDefault(row => row.Id == childId)
            ?? await _db.Entities.FirstOrDefaultAsync(row => row.Id == childId, cancellationToken);
        if (child is null) {
            return;
        }

        var shouldMarkAncestors = ShouldMarkAutoIdentifyAncestors(child, parentId);
        child.ParentEntityId = parentId;
        child.SortOrder = sortOrder;
        child.UpdatedAt = now;

        if (shouldMarkAncestors) {
            await MarkAutoIdentifyAncestorsUnorganizedAsync(parentId, now, cancellationToken);
        }
    }

    private async Task ClearStructuralChildLinkAsync(
        Guid childId,
        DateTimeOffset now,
        CancellationToken cancellationToken) {
        var child = _db.Entities.Local.FirstOrDefault(row => row.Id == childId)
            ?? await _db.Entities.FirstOrDefaultAsync(row => row.Id == childId, cancellationToken);
        if (child is null || child.ParentEntityId is null && child.SortOrder is null) {
            return;
        }

        child.ParentEntityId = null;
        child.SortOrder = null;
        child.UpdatedAt = now;
    }

    private async Task EnsureEntityFileAsync(
        Guid entityId,
        EntityFileRole role,
        string path,
        long? sizeBytes,
        DateTimeOffset now,
        CancellationToken cancellationToken) {
        var file = _db.EntityFiles.Local.FirstOrDefault(row => row.EntityId == entityId && row.Role == role)
            ?? await _db.EntityFiles.FirstOrDefaultAsync(row =>
                row.EntityId == entityId && row.Role == role, cancellationToken);
        if (file is null) {
            _db.EntityFiles.Add(new EntityFileRow {
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
        var source = _db.EntitySources.Local.FirstOrDefault(row => row.EntityId == entityId && row.Code == code)
            ?? await _db.EntitySources.FindAsync([entityId, code], cancellationToken);
        if (source is null) {
            _db.EntitySources.Add(new EntitySourceRow {
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




    // ── Helpers ──

    private async Task<EntityRow?> FindEntityBySourcePath(string kindCode, string path, CancellationToken cancellationToken) {
        return await _db.EntityFiles.AsNoTracking()
            .Where(file => file.Role == EntityFileRole.Source && file.Path == path)
            .Join(
                _db.Entities,
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
        var entityId = await _db.EntitySources.AsNoTracking()
            .Where(source => source.Code == sourceCode && source.Value == value)
            .Select(source => (Guid?)source.EntityId)
            .FirstOrDefaultAsync(cancellationToken);

        if (entityId is null) return null;

        return await _db.Entities
            .FirstOrDefaultAsync(entity => entity.Id == entityId.Value && entity.KindCode == kindCode, cancellationToken);
    }

    private bool ShouldMarkAutoIdentifyAncestors(EntityRow child, Guid? parentId) =>
        parentId is not null &&
        (_db.Entry(child).State == EntityState.Added || child.ParentEntityId != parentId);

    private async Task MarkAutoIdentifyAncestorsUnorganizedAsync(
        Guid? parentId,
        DateTimeOffset now,
        CancellationToken cancellationToken) {
        if (parentId is null) {
            return;
        }

        var current = parentId.Value;
        var guard = 0;
        while (guard++ < 64) {
            var ancestor = await FindMutableEntityAsync(current, cancellationToken);
            if (ancestor is null ||
                string.Equals(ancestor.KindCode, EntityKindRegistry.MusicArtist.Code, StringComparison.OrdinalIgnoreCase)) {
                return;
            }

            if (ancestor.IsOrganized) {
                ancestor.IsOrganized = false;
                ancestor.UpdatedAt = now;
            }

            if (ancestor.ParentEntityId is not { } next) {
                return;
            }

            current = next;
        }
    }

    private async Task<EntityRow?> FindMutableEntityAsync(Guid entityId, CancellationToken cancellationToken) =>
        _db.Entities.Local.FirstOrDefault(row => row.Id == entityId)
        ?? await _db.Entities.FirstOrDefaultAsync(row => row.Id == entityId, cancellationToken);

    private async Task<int> RemoveStaleEntitiesBySourcePath(
        List<Guid> candidateIds, IReadOnlySet<string> validPaths, CancellationToken cancellationToken) {
        if (candidateIds.Count == 0) return 0;

        var staleIds = await GetStaleEntityIdsBySourcePathAsync(candidateIds, validPaths, cancellationToken);
        return await RemoveEntitiesByIdAsync(staleIds, cancellationToken);
    }

    private async Task<int> RemoveStaleContainerEntitiesBySourcePath(
        List<Guid> candidateIds, IReadOnlySet<string> validPaths, CancellationToken cancellationToken) {
        if (candidateIds.Count == 0) return 0;

        var staleIds = await GetStaleEntityIdsBySourcePathAsync(candidateIds, validPaths, cancellationToken);
        if (staleIds.Count == 0) return 0;

        var idsToRemove = await ExpandContainerSubtreeIdsAsync(staleIds, validPaths, cancellationToken);
        return await RemoveEntitiesByIdAsync(idsToRemove, cancellationToken);
    }

    private async Task<List<Guid>> GetStaleEntityIdsBySourcePathAsync(
        List<Guid> candidateIds, IReadOnlySet<string> validPaths, CancellationToken cancellationToken) {
        var sourcePaths = await _db.EntityFiles.AsNoTracking()
            .Where(f => candidateIds.Contains(f.EntityId) && f.Role == EntityFileRole.Source)
            .Select(f => new { f.EntityId, f.Path })
            .ToListAsync(cancellationToken);

        return sourcePaths
            .Where(sp => !validPaths.Contains(sp.Path))
            .Select(sp => sp.EntityId)
            .Distinct()
            .ToList();
    }

    private async Task<List<Guid>> ExpandContainerSubtreeIdsAsync(
        List<Guid> staleContainerIds, IReadOnlySet<string> validPaths, CancellationToken cancellationToken) {
        var allEntityParents = await _db.Entities.AsNoTracking()
            .Select(entity => new { entity.Id, entity.ParentEntityId })
            .ToListAsync(cancellationToken);
        var childrenByParentId = allEntityParents
            .Where(entity => entity.ParentEntityId is not null)
            .GroupBy(entity => entity.ParentEntityId!.Value)
            .ToDictionary(group => group.Key, group => group.Select(entity => entity.Id).ToArray());

        // Bound the candidate rows by this structural closure, then apply host filesystem equality in
        // memory. A database collation cannot decide whether case-only paths are the same on the host.
        var descendantIds = staleContainerIds.ToHashSet();
        var descendantsPending = new Queue<Guid>(staleContainerIds);
        while (descendantsPending.TryDequeue(out var parentId)) {
            if (!childrenByParentId.TryGetValue(parentId, out var childIds)) {
                continue;
            }
            foreach (var childId in childIds.Where(descendantIds.Add)) {
                descendantsPending.Enqueue(childId);
            }
        }

        var descendantIdArray = descendantIds.ToArray();
        var sourceCandidates = await _db.EntityFiles.AsNoTracking()
            .Where(file => file.Role == EntityFileRole.Source
                && descendantIdArray.Contains(file.EntityId))
            .Select(file => new { file.EntityId, file.Path })
            .ToListAsync(cancellationToken);
        var validSourceIds = sourceCandidates
            .Where(file => validPaths.Contains(file.Path))
            .Select(file => file.EntityId)
            .ToHashSet();

        var idsToRemove = staleContainerIds.ToHashSet();
        var pending = new Queue<Guid>(staleContainerIds);

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

        return idsToRemove.ToList();
    }

    private async Task<int> RemoveEntitiesByIdAsync(
        List<Guid> idsToRemove, CancellationToken cancellationToken) {
        if (idsToRemove.Count == 0) return 0;

        var entitiesToRemove = await _db.Entities
            .Where(e => idsToRemove.Contains(e.Id))
            .ToListAsync(cancellationToken);

        _db.Entities.RemoveRange(entitiesToRemove);
        await SaveChangesWithLifecycleAsync(cancellationToken);

        return entitiesToRemove.Count;
    }

    private async Task<List<Guid>> GetLooseRootEntityIdsAsync(Guid rootId, string kindCode, CancellationToken cancellationToken) {
        var rootPath = await _db.LibraryRoots.AsNoTracking()
            .Where(root => root.Id == rootId)
            .Select(root => root.Path)
            .FirstOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(rootPath)) {
            return [];
        }

        var sourceFiles = await _db.EntityFiles.AsNoTracking()
            .Where(file => file.Role == EntityFileRole.Source)
            .Join(
                _db.Entities.AsNoTracking().Where(entity => entity.KindCode == kindCode && entity.ParentEntityId == null),
                file => file.EntityId,
                entity => entity.Id,
                (file, entity) => new { file.EntityId, file.Path })
            .ToListAsync(cancellationToken);

        return sourceFiles
            .Where(file => LibraryScanPathRules.IsDirectChildPath(file.Path, rootPath))
            .Select(file => file.EntityId)
            .Distinct()
            .ToList();
    }

    /// <inheritdoc />
}
