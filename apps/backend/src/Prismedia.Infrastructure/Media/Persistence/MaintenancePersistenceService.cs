using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Videos;

namespace Prismedia.Infrastructure.Media.Persistence;

/// <summary>
/// Infrastructure adapter for <see cref="IMaintenancePersistence"/>.
/// </summary>
public sealed class MaintenancePersistenceService(PrismediaDbContext db, string dataDir) : IMaintenancePersistence {
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

    public string GetCacheBasePath() => Path.Combine(dataDir, "cache");

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

    private void DeleteGeneratedPreviewFiles(EntityKind kind, Guid entityId) {
        var cacheBase = GetCacheBasePath();
        var id = entityId.ToString();
        switch (kind) {
            case EntityKind.Video:
                HlsAssetService.CancelActiveGenerationsForItem(entityId);
                DeleteFileIfExists(Path.Combine(cacheBase, "videos", id, "thumb.jpg"));
                DeleteFileIfExists(Path.Combine(cacheBase, "videos", id, "preview.mp4"));
                DeleteFileIfExists(Path.Combine(cacheBase, "videos", id, "sprite.jpg"));
                DeleteFileIfExists(Path.Combine(cacheBase, "grid-thumbs", id + ".jpg"));
                DeleteFileIfExists(Path.Combine(cacheBase, "grid-thumbs", id + "@2x.jpg"));
                DeleteFileIfExists(Path.Combine(cacheBase, "videos", id, "trickplay.vtt"));
                DeleteDirectoryIfExists(Path.Combine(cacheBase, "videos", id, "trickplay-frames"));
                DeleteDirectoryIfExists(Path.Combine(cacheBase, "trickplay", id));
                DeleteDirectoryIfExists(Path.Combine(cacheBase, "hlsv", id));
                DeleteDirectoryIfExists(Path.Combine(cacheBase, "hls2", id));
                DeleteDirectoryIfExists(Path.Combine(cacheBase, "hls", id));
                break;
            case EntityKind.Image:
                DeleteFileIfExists(Path.Combine(cacheBase, "images", id, "thumb.jpg"));
                DeleteFileIfExists(Path.Combine(cacheBase, "images", id, "preview.mp4"));
                break;
            case EntityKind.BookPage:
                DeleteFileIfExists(Path.Combine(cacheBase, "book-pages", id, "thumb.jpg"));
                break;
            case EntityKind.AudioTrack:
                DeleteFileIfExists(Path.Combine(cacheBase, "audio-tracks", id, "waveform.json"));
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
