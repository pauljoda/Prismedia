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
    // ── Library roots & settings ──

    public async Task<LibraryRootData?> GetLibraryRootAsync(Guid rootId, CancellationToken cancellationToken) {
        var row = await _db.LibraryRoots.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == rootId, cancellationToken);
        return row is null ? null : ToData(row);
    }

    public async Task<IReadOnlyList<LibraryRootData>> GetEnabledRootsAsync(CancellationToken cancellationToken) {
        return await _db.LibraryRoots.AsNoTracking()
            .Where(r => r.Enabled)
            .Select(r => ToData(r))
            .ToListAsync(cancellationToken);
    }

    public async Task<LibrarySettingsData> GetSettingsAsync(CancellationToken cancellationToken) {
        var settingsService = new SettingsService(new EfSettingsPersistence(_db));
        var settings = await settingsService.GetGenerationSettingsAsync(cancellationToken);
        var autoIdentify = await settingsService.GetAutoIdentifySettingsAsync(cancellationToken);
        var removeOrphanTags = await settingsService.GetRemoveOrphanTagsAsync(cancellationToken);
        return new LibrarySettingsData(
            settings.AutoGenerateMetadata,
            settings.AutoGenerateOshash,
            settings.AutoGenerateMd5,
            settings.AutoGeneratePreview,
            settings.GenerateTrickplay,
            settings.TrickplayIntervalSeconds,
            settings.PreviewClipDurationSeconds,
            settings.ThumbnailQuality,
            settings.TrickplayQuality,
            autoIdentify.Enabled,
            autoIdentify.EntityKinds,
            removeOrphanTags,
            autoIdentify.UnorganizedOnly);
    }

    public async Task UpdateRootLastScannedAsync(Guid rootId, CancellationToken cancellationToken) {
        var row = await _db.LibraryRoots.FindAsync([rootId], cancellationToken);
        if (row is not null) {
            row.LastScannedAt = DateTimeOffset.UtcNow;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            await SaveChangesWithLifecycleAsync(cancellationToken);
        }
    }

    public async Task<IReadOnlySet<string>> GetExcludedPathsForRootAsync(Guid rootId, CancellationToken cancellationToken) {
        var rootPath = await _db.LibraryRoots.AsNoTracking()
            .Where(root => root.Id == rootId)
            .Select(root => root.Path)
            .FirstOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(rootPath)) {
            return new HashSet<string>(FileSystemPathComparison.Comparer);
        }

        var exclusions = await _db.MediaFileIgnores.AsNoTracking()
            .Where(row => row.LibraryRootId == rootId)
            .Select(row => row.Path)
            .ToArrayAsync(cancellationToken);

        return exclusions
            .Select(path => Path.GetFullPath(Path.Combine(rootPath, path)))
            .ToHashSet(FileSystemPathComparison.Comparer);
    }

    public async Task<int> RemoveEntitiesInExcludedPathsAsync(Guid rootId, CancellationToken cancellationToken) {
        var excludedPaths = await GetExcludedPathsForRootAsync(rootId, cancellationToken);
        if (excludedPaths.Count == 0) {
            return 0;
        }

        var sourceRows = await _db.EntityFiles.AsNoTracking()
            .Where(file => file.Role == EntityFileRole.Source)
            .Select(file => new { file.EntityId, file.Path })
            .ToArrayAsync(cancellationToken);
        var excludedEntityIds = sourceRows
            .Where(file => excludedPaths.Any(excluded => LibraryScanPathRules.IsPathCoveredByExclusion(file.Path, excluded)))
            .Select(file => file.EntityId)
            .Distinct()
            .ToArray();
        if (excludedEntityIds.Length == 0) {
            return 0;
        }

        var entities = await _db.Entities
            .Where(entity => excludedEntityIds.Contains(entity.Id))
            .ToArrayAsync(cancellationToken);
        _db.Entities.RemoveRange(entities);
        await SaveChangesWithLifecycleAsync(cancellationToken);
        return entities.Length;
    }


    private static LibraryRootData ToData(LibraryRootRow row) =>
        new(row.Id, row.Path, row.Label, row.Enabled, row.Recursive,
            row.ScanVideos, row.ScanImages, row.ScanAudio, row.ScanBooks, row.IsNsfw, row.AutoIdentify);
}
