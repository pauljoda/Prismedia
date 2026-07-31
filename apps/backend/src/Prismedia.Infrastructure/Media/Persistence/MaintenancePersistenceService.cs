using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Files;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Contracts.Media;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Media.Processing;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Videos;

namespace Prismedia.Infrastructure.Media.Persistence;

/// <summary>
/// Infrastructure adapter for <see cref="IMaintenancePersistence"/>.
/// </summary>
public sealed class MaintenancePersistenceService(
    PrismediaDbContext db,
    AssetPathService assets) : IMaintenancePersistence {
    private static readonly TimeSpan SubtitleOrphanGracePeriod = TimeSpan.FromHours(1);
    public async Task<IReadOnlyList<Guid>> GetActiveEntityIdsByKindAsync(EntityKind kind, CancellationToken cancellationToken) =>
        await db.Entities
            .Where(e => e.KindCode == EntityKindRegistry.ToCode(kind))
            .Select(e => e.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> GetActiveEntityIdsByKindsAsync(
        IReadOnlyCollection<EntityKind> kinds,
        CancellationToken cancellationToken) {
        var codes = kinds.Select(EntityKindRegistry.ToCode).ToArray();
        return await db.Entities
            .Where(entity => codes.Contains(entity.KindCode))
            .Select(entity => entity.Id)
            .ToListAsync(cancellationToken);
    }

    public Task<int> ValidateGeneratedAssetsAsync(
        GeneratedAssetFamily family,
        IReadOnlyCollection<Guid> activeEntityIds,
        CancellationToken cancellationToken) {
        var missing = 0;
        foreach (var id in activeEntityIds) {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var path in GeneratedAssetFamilyCatalog.ExpectedPaths(assets, family, id)) {
                if (!File.Exists(path)) {
                    missing++;
                }
            }
        }
        return Task.FromResult(missing);
    }

    public Task<int> CleanupOrphanedGeneratedAssetsAsync(
        GeneratedAssetFamily family,
        IReadOnlyCollection<Guid> activeEntityIds,
        CancellationToken cancellationToken) {
        var active = new HashSet<string>(activeEntityIds.Select(id => id.ToString()), StringComparer.OrdinalIgnoreCase);
        return Task.FromResult(GeneratedAssetFamilyCatalog.CleanupOrphanedAssets(assets, family, active, cancellationToken));
    }

    /// <inheritdoc />
    public async Task<int> CleanupOrphanedSubtitleAssetsAsync(CancellationToken cancellationToken) {
        var rows = await db.EntitySubtitles.AsNoTracking()
            .Select(subtitle => new {
                subtitle.Source,
                subtitle.StoragePath,
                subtitle.SourceFormat,
                subtitle.SourcePath
            })
            .ToArrayAsync(cancellationToken);
        var retained = new HashSet<string>(FileSystemPathComparison.Comparer);
        foreach (var row in rows) {
            AddRootedPath(row.StoragePath, retained);
            AddRootedPath(row.SourcePath, retained);
            if (row.Source == EntitySubtitleSource.Embedded &&
                SubtitleFormats.IsStyled(row.SourceFormat) &&
                Path.IsPathRooted(row.StoragePath)) {
                AddRootedPath(
                    Path.ChangeExtension(
                        row.StoragePath,
                        SubtitleFileExtensions.ForFormat(row.SourceFormat)),
                    retained);
            }
        }

        var videosDirectory = Path.Combine(assets.CacheRoot, AssetPaths.Videos);
        if (!IsOrdinaryDirectory(videosDirectory)) {
            return 0;
        }

        var removed = 0;
        var cutoff = DateTime.UtcNow - SubtitleOrphanGracePeriod;
        foreach (var entityDirectory in TryEnumerateDirectories(videosDirectory)) {
            cancellationToken.ThrowIfCancellationRequested();
            var subtitleDirectory = Path.Combine(entityDirectory, AssetPaths.Subtitles);
            if (!IsOrdinaryDirectory(entityDirectory) || !IsOrdinaryDirectory(subtitleDirectory)) {
                continue;
            }

            foreach (var path in TryEnumerateFiles(subtitleDirectory)) {
                cancellationToken.ThrowIfCancellationRequested();
                if (retained.Contains(path) || !assets.IsSubtitleAssetPath(path)) {
                    continue;
                }

                try {
                    if (File.GetLastWriteTimeUtc(path) > cutoff) {
                        continue;
                    }

                    File.Delete(path);
                    removed++;
                } catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) {
                    // Maintenance is best effort; a later pass retries any inaccessible generation.
                }
            }
        }

        return removed;
    }

    public async Task ClearGeneratedPreviewAssetsAsync(
        EntityKind kind,
        Guid entityId,
        CancellationToken cancellationToken) {
        var roles = EntityKindRegistry.Describe(kind).Processing.GeneratedFileRoles.ToArray();
        if (roles.Length > 0) {
            var files = await db.EntityFiles
                .Where(file => file.EntityId == entityId && roles.Contains(file.Role)
                    && file.Source == FileSourceKind.Scan.ToCode())
                .ToListAsync(cancellationToken);
            db.EntityFiles.RemoveRange(files);
        }

        if (EntityKindRegistry.Describe(kind).Processing.AssetFamily == GeneratedAssetFamily.Video) {
            var trickplayInfos = await db.TrickplayInfos
                .Where(info => info.EntityId == entityId)
                .ToListAsync(cancellationToken);
            db.TrickplayInfos.RemoveRange(trickplayInfos);
        }

        await db.SaveChangesAsync(cancellationToken);
        var family = EntityKindRegistry.Describe(kind).Processing.AssetFamily;
        if (family != GeneratedAssetFamily.None) {
            var protectedPaths = (await db.EntityFiles.AsNoTracking()
                .Where(file => file.EntityId == entityId && file.Source != FileSourceKind.Scan.ToCode())
                .Select(file => file.Path)
                .ToArrayAsync(cancellationToken))
                .ToHashSet(FileSystemPathComparison.Comparer);
            GeneratedAssetFamilyCatalog.DeleteGeneratedAssets(
                assets, family, entityId,
                path => { if (!protectedPaths.Contains(path)) DeleteFileIfExists(path); },
                DeleteDirectoryIfExists);
        }
    }

    private static void AddRootedPath(string? path, ISet<string> paths) {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path)) {
            return;
        }

        try {
            paths.Add(Path.GetFullPath(path));
        } catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException) {
            // Malformed historical rows do not authorize retaining an arbitrary cache file.
        }
    }

    private static bool IsOrdinaryDirectory(string path) {
        try {
            var attributes = File.GetAttributes(path);
            return attributes.HasFlag(FileAttributes.Directory) &&
                !attributes.HasFlag(FileAttributes.ReparsePoint);
        } catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) {
            return false;
        }
    }

    private static IReadOnlyList<string> TryEnumerateDirectories(string path) {
        try {
            return Directory.GetDirectories(path);
        } catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) {
            return [];
        }
    }

    private static IReadOnlyList<string> TryEnumerateFiles(string path) {
        try {
            return Directory.GetFiles(path);
        } catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) {
            return [];
        }
    }

    private static void DeleteFileIfExists(string path) {
        if (!File.Exists(path)) {
            return;
        }

        try {
            var deletePath = $"{path}.deleting-{Guid.NewGuid():N}";
            File.Move(path, deletePath, overwrite: true);
            File.Delete(deletePath);
        } catch (IOException) {
            TryDeleteFile(path);
        } catch (UnauthorizedAccessException) {
            TryDeleteFile(path);
        }
    }

    private static void DeleteDirectoryIfExists(string path) {
        if (!Directory.Exists(path)) {
            return;
        }

        try {
            var deletePath = $"{path}.deleting-{Guid.NewGuid():N}";
            Directory.Move(path, deletePath);
            TryDeleteDirectory(deletePath);
        } catch (IOException) {
            TryDeleteDirectory(path);
        } catch (UnauthorizedAccessException) {
            TryDeleteDirectory(path);
        }
    }

    private static void TryDeleteFile(string path) {
        try {
            if (File.Exists(path)) {
                File.Delete(path);
            }
        } catch (IOException) {
            // A worker may be replacing the asset concurrently; the queued rebuild will retry.
        } catch (UnauthorizedAccessException) {
            // Keep rebuild enqueueing non-fatal even when a stale file cannot be removed.
        }
    }

    private static void TryDeleteDirectory(string path) {
        try {
            if (Directory.Exists(path)) {
                Directory.Delete(path, recursive: true);
            }
        } catch (IOException) {
            // A worker may be writing files concurrently; the queued rebuild will retry.
        } catch (UnauthorizedAccessException) {
            // Keep rebuild enqueueing non-fatal even when a stale directory cannot be removed.
        }
    }
}
