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
    private static readonly EntityFileRole[] GeneratedVideoPreviewRoles =
    [
        EntityFileRole.Thumbnail,
        EntityFileRole.GridThumbnail,
        EntityFileRole.GridThumbnail2x,
        EntityFileRole.Preview,
        EntityFileRole.Sprite,
        EntityFileRole.Trickplay,
        EntityFileRole.Hls
    ];

    private static readonly EntityFileRole[] GeneratedImagePreviewRoles = [EntityFileRole.Thumbnail, EntityFileRole.Preview];
    private static readonly EntityFileRole[] GeneratedAudioPreviewRoles = [EntityFileRole.Waveform];

    public async Task<IReadOnlyList<Guid>> GetActiveEntityIdsByKindAsync(EntityKind kind, CancellationToken cancellationToken) =>
        await db.Entities
            .Where(e => e.KindCode == EntityKindRegistry.ToCode(kind))
            .Select(e => e.Id)
            .ToListAsync(cancellationToken);

    public string GetCacheBasePath() => assets.CacheRoot;

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
        var roles = GeneratedPreviewRoles(kind);
        if (roles.Length > 0) {
            var files = await db.EntityFiles
                .Where(file => file.EntityId == entityId && roles.Contains(file.Role))
                .ToListAsync(cancellationToken);
            db.EntityFiles.RemoveRange(files);
        }

        if (kind == EntityKind.Video) {
            var trickplayInfos = await db.TrickplayInfos
                .Where(info => info.EntityId == entityId)
                .ToListAsync(cancellationToken);
            db.TrickplayInfos.RemoveRange(trickplayInfos);
        }

        await db.SaveChangesAsync(cancellationToken);
        DeleteGeneratedPreviewFiles(kind, entityId);
    }

    private static EntityFileRole[] GeneratedPreviewRoles(EntityKind kind) =>
        kind switch {
            EntityKind.Video => GeneratedVideoPreviewRoles,
            EntityKind.Image or EntityKind.BookPage => GeneratedImagePreviewRoles,
            EntityKind.AudioTrack => GeneratedAudioPreviewRoles,
            _ => []
        };

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

    private void DeleteGeneratedPreviewFiles(EntityKind kind, Guid entityId) {
        var cacheBase = GetCacheBasePath();
        var id = entityId.ToString();
        switch (kind) {
            case EntityKind.Video:
                HlsAssetService.CancelActiveGenerationsForItem(entityId);
                DeleteFileIfExists(Path.Combine(cacheBase, AssetPaths.Videos, id, AssetPaths.ThumbnailFile));
                DeleteFileIfExists(Path.Combine(cacheBase, AssetPaths.Videos, id, AssetPaths.PreviewFile));
                DeleteFileIfExists(Path.Combine(cacheBase, AssetPaths.Videos, id, AssetPaths.SpriteFile));
                DeleteFileIfExists(Path.Combine(cacheBase, AssetPaths.GridThumbs, id + ".jpg"));
                DeleteFileIfExists(Path.Combine(cacheBase, AssetPaths.GridThumbs, id + "@2x.jpg"));
                DeleteFileIfExists(Path.Combine(cacheBase, AssetPaths.Videos, id, AssetPaths.TrickplayVttFile));
                DeleteDirectoryIfExists(Path.Combine(cacheBase, AssetPaths.Videos, id, AssetPaths.TrickplayFrames));
                DeleteDirectoryIfExists(Path.Combine(cacheBase, AssetPaths.Trickplay, id));
                DeleteDirectoryIfExists(Path.Combine(cacheBase, AssetPaths.Hlsv, id));
                DeleteDirectoryIfExists(Path.Combine(cacheBase, AssetPaths.Hls2, id));
                DeleteDirectoryIfExists(Path.Combine(cacheBase, AssetPaths.Hls, id));
                break;
            case EntityKind.Image:
                DeleteFileIfExists(Path.Combine(cacheBase, AssetPaths.Images, id, AssetPaths.ThumbnailFile));
                DeleteFileIfExists(Path.Combine(cacheBase, AssetPaths.Images, id, AssetPaths.PreviewFile));
                break;
            case EntityKind.BookPage:
                DeleteFileIfExists(Path.Combine(cacheBase, AssetPaths.BookPages, id, AssetPaths.ThumbnailFile));
                break;
            case EntityKind.AudioTrack:
                DeleteFileIfExists(Path.Combine(cacheBase, AssetPaths.AudioTracks, id, AssetPaths.WaveformFile));
                break;
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
