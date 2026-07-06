using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Settings;
using Prismedia.Contracts.Settings;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Media.Persistence;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Settings;

/// <summary>
/// EF Core adapter for <see cref="ISettingsPersistence"/>. Stores app-setting overrides
/// as raw JSON in <c>app_settings</c> and owns row ↔ contract translation for watched
/// library roots.
/// </summary>
public sealed class EfSettingsPersistence : ISettingsPersistence {
    private readonly PrismediaDbContext _db;

    public EfSettingsPersistence(PrismediaDbContext db) {
        _db = db;
    }

    public async Task<IReadOnlyDictionary<string, string>> LoadSettingOverridesAsync(CancellationToken cancellationToken) =>
        await _db.AppSettings
            .AsNoTracking()
            .ToDictionaryAsync(row => row.Key, row => row.ValueJson, StringComparer.Ordinal, cancellationToken);

    public async Task SaveSettingOverrideAsync(string key, string valueJson, CancellationToken cancellationToken) {
        var now = DateTimeOffset.UtcNow;
        var row = await _db.AppSettings.FindAsync([key], cancellationToken);
        if (row is null) {
            _db.AppSettings.Add(new AppSettingRow {
                Key = key,
                ValueJson = valueJson,
                CreatedAt = now,
                UpdatedAt = now,
            });
        } else {
            row.ValueJson = valueJson;
            row.UpdatedAt = now;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveSettingOverridesAsync(
        IReadOnlyDictionary<string, string> values,
        CancellationToken cancellationToken) {
        await ReplaceSettingOverridesAsync(values, [], cancellationToken);
    }

    public async Task ReplaceSettingOverridesAsync(
        IReadOnlyDictionary<string, string> upserts,
        IReadOnlyCollection<string> deletes,
        CancellationToken cancellationToken) {
        var deleteKeys = deletes
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .ToHashSet(StringComparer.Ordinal);
        var keys = upserts.Keys.Concat(deleteKeys).Distinct(StringComparer.Ordinal).ToArray();
        if (keys.Length == 0) {
            return;
        }

        var existing = await _db.AppSettings
            .Where(row => keys.Contains(row.Key))
            .ToDictionaryAsync(row => row.Key, StringComparer.Ordinal, cancellationToken);
        var now = DateTimeOffset.UtcNow;

        foreach (var key in deleteKeys.Where(key => !upserts.ContainsKey(key))) {
            if (existing.TryGetValue(key, out var row)) {
                _db.AppSettings.Remove(row);
            }
        }

        foreach (var (key, valueJson) in upserts) {
            if (existing.TryGetValue(key, out var row)) {
                row.ValueJson = valueJson;
                row.UpdatedAt = now;
            } else {
                _db.AppSettings.Add(new AppSettingRow {
                    Key = key,
                    ValueJson = valueJson,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteSettingOverrideAsync(string key, CancellationToken cancellationToken) {
        var row = await _db.AppSettings.FindAsync([key], cancellationToken);
        if (row is null) {
            return;
        }

        _db.AppSettings.Remove(row);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LibraryRoot>> ListLibraryRootsAsync(CancellationToken cancellationToken) {
        return await _db.LibraryRoots
            .AsNoTracking()
            .OrderBy(root => root.Label)
            .ThenBy(root => root.Path)
            .Select(root => ToContract(root))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<LibraryRoot?> GetLibraryRootAsync(Guid id, CancellationToken cancellationToken) {
        var row = await _db.LibraryRoots.AsNoTracking().FirstOrDefaultAsync(root => root.Id == id, cancellationToken);
        return row is null ? null : ToContract(row);
    }

    public async Task<LibraryRoot> AddLibraryRootAsync(LibraryRoot state, CancellationToken cancellationToken) {
        var row = new LibraryRootRow {
            Id = state.Id,
            Path = state.Path,
            Label = state.Label,
            Enabled = state.Enabled,
            Recursive = state.Recursive,
            ScanVideos = state.ScanVideos,
            ScanImages = state.ScanImages,
            ScanAudio = state.ScanAudio,
            ScanBooks = state.ScanBooks,
            IsNsfw = state.IsNsfw,
            AutoIdentify = state.AutoIdentify,
            LastScannedAt = state.LastScannedAt,
            CreatedByUserId = state.CreatedByUserId,
            CreatedAt = state.CreatedAt,
            UpdatedAt = state.UpdatedAt,
        };

        _db.LibraryRoots.Add(row);
        await _db.SaveChangesAsync(cancellationToken);
        return ToContract(row);
    }

    public async Task<LibraryRoot> SaveLibraryRootAsync(LibraryRoot state, CancellationToken cancellationToken) {
        var row = await _db.LibraryRoots.FindAsync([state.Id], cancellationToken)
            ?? throw new InvalidOperationException($"Library root '{state.Id}' was not found.");

        row.Path = state.Path;
        row.Label = state.Label;
        row.Enabled = state.Enabled;
        row.Recursive = state.Recursive;
        row.ScanVideos = state.ScanVideos;
        row.ScanImages = state.ScanImages;
        row.ScanAudio = state.ScanAudio;
        row.ScanBooks = state.ScanBooks;
        row.IsNsfw = state.IsNsfw;
        row.AutoIdentify = state.AutoIdentify;
        row.LastScannedAt = state.LastScannedAt;
        row.UpdatedAt = state.UpdatedAt;

        await _db.SaveChangesAsync(cancellationToken);
        return ToContract(row);
    }

    public async Task<bool> DeleteLibraryRootAsync(Guid id, CancellationToken cancellationToken) {
        if (!_db.Database.IsRelational() || _db.Database.CurrentTransaction is not null) {
            return await DeleteLibraryRootCoreAsync(id, cancellationToken);
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var deleted = await DeleteLibraryRootCoreAsync(id, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return deleted;
    }

    private async Task<bool> DeleteLibraryRootCoreAsync(Guid id, CancellationToken cancellationToken) {
        var row = await _db.LibraryRoots.FindAsync([id], cancellationToken);
        if (row is null) {
            return false;
        }

        await DeleteEntitiesForLibraryRootAsync(row, cancellationToken);
        _db.LibraryRoots.Remove(row);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task DeleteEntitiesForLibraryRootAsync(LibraryRootRow root, CancellationToken cancellationToken) {
        var entityIds = new HashSet<Guid>();
        var containerIds = new HashSet<Guid>();

        foreach (var entityId in await _db.VideoDetails.AsNoTracking()
            .Where(detail => detail.LibraryRootId == root.Id)
            .Select(detail => detail.EntityId)
            .ToArrayAsync(cancellationToken)) {
            entityIds.Add(entityId);
        }

        foreach (var entityId in await _db.GalleryDetails.AsNoTracking()
            .Where(detail => detail.LibraryRootId == root.Id)
            .Select(detail => detail.EntityId)
            .ToArrayAsync(cancellationToken)) {
            entityIds.Add(entityId);
            containerIds.Add(entityId);
        }

        foreach (var entityId in await _db.BookDetails.AsNoTracking()
            .Where(detail => detail.LibraryRootId == root.Id)
            .Select(detail => detail.EntityId)
            .ToArrayAsync(cancellationToken)) {
            entityIds.Add(entityId);
            containerIds.Add(entityId);
        }

        foreach (var entityId in await _db.MusicArtistDetails.AsNoTracking()
            .Where(detail => detail.LibraryRootId == root.Id)
            .Select(detail => detail.EntityId)
            .ToArrayAsync(cancellationToken)) {
            entityIds.Add(entityId);
            containerIds.Add(entityId);
        }

        foreach (var entityId in await _db.AudioLibraryDetails.AsNoTracking()
            .Where(detail => detail.LibraryRootId == root.Id)
            .Select(detail => detail.EntityId)
            .ToArrayAsync(cancellationToken)) {
            entityIds.Add(entityId);
            containerIds.Add(entityId);
        }

        if (!string.IsNullOrWhiteSpace(root.Path)) {
            var sourceFiles = await _db.EntityFiles.AsNoTracking()
                .Where(file => file.Role == EntityFileRole.Source)
                .Select(file => new { file.EntityId, file.Path })
                .ToArrayAsync(cancellationToken);
            foreach (var file in sourceFiles.Where(file => LibraryScanPathRules.IsPathUnderRoot(file.Path, root.Path))) {
                entityIds.Add(file.EntityId);
            }
        }

        if (containerIds.Count > 0) {
            var childrenByParentId = await LoadChildrenByParentIdAsync(cancellationToken);
            var pending = new Queue<Guid>(containerIds);
            while (pending.Count > 0) {
                var parentId = pending.Dequeue();
                if (!childrenByParentId.TryGetValue(parentId, out var childIds)) {
                    continue;
                }

                foreach (var childId in childIds) {
                    if (entityIds.Add(childId)) {
                        pending.Enqueue(childId);
                    }
                }
            }
        }

        if (entityIds.Count > 0) {
            var entities = await _db.Entities
                .Where(entity => entityIds.Contains(entity.Id))
                .ToArrayAsync(cancellationToken);
            _db.Entities.RemoveRange(entities);
            await _db.SaveChangesAsync(cancellationToken);
        }

        await RemoveOrphanMediaContainersAsync(cancellationToken);
    }

    private async Task RemoveOrphanMediaContainersAsync(CancellationToken cancellationToken) {
        var containerCodes = new[] {
            EntityKindRegistry.Movie.Code,
            EntityKindRegistry.VideoSeason.Code,
            EntityKindRegistry.VideoSeries.Code,
        };

        while (true) {
            var orphanContainers = await _db.Entities
                .Where(entity => containerCodes.Contains(entity.KindCode)
                    && !_db.Entities.Any(child => child.ParentEntityId == entity.Id))
                .ToArrayAsync(cancellationToken);
            if (orphanContainers.Length == 0) {
                return;
            }

            _db.Entities.RemoveRange(orphanContainers);
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<Dictionary<Guid, Guid[]>> LoadChildrenByParentIdAsync(CancellationToken cancellationToken) {
        var childRows = await _db.Entities.AsNoTracking()
            .Where(entity => entity.ParentEntityId != null)
            .Select(entity => new { entity.Id, ParentEntityId = entity.ParentEntityId!.Value })
            .ToArrayAsync(cancellationToken);
        return childRows
            .GroupBy(entity => entity.ParentEntityId)
            .ToDictionary(group => group.Key, group => group.Select(entity => entity.Id).ToArray());
    }

    private static LibraryRoot ToContract(LibraryRootRow row) =>
        new(
            row.Id,
            row.Path,
            row.Label,
            row.Enabled,
            row.Recursive,
            row.ScanVideos,
            row.ScanImages,
            row.ScanAudio,
            row.ScanBooks,
            row.IsNsfw,
            row.LastScannedAt,
            row.CreatedAt,
            row.UpdatedAt,
            row.AutoIdentify,
            row.CreatedByUserId);
}
