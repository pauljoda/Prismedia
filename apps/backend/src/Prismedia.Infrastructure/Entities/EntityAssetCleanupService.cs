using Microsoft.Extensions.Logging;
using Prismedia.Infrastructure.Media.Processing;

namespace Prismedia.Infrastructure.Entities;

/// <summary>
/// Best-effort cleanup of cache and downloaded-artwork files owned by Entity ids. Callers decide which
/// Entity ids are actually being removed and may preserve artwork when an on-disk/wanted Entity survives.
/// Filesystem failures are logged but never roll back an already-committed domain transition.
/// </summary>
public sealed class EntityAssetCleanupService {
    private static readonly string[] GeneratedDirectoryRoots = [
        "videos",
        "images",
        "trickplay",
        "audio-tracks",
        "book-pages",
        "book-covers",
        "hls",
        "hls2",
        "hlsv",
    ];

    private readonly AssetPathService assets;
    private readonly ILogger logger;

    /// <summary>Creates the shared cleanup service from the configured cache root.</summary>
    public EntityAssetCleanupService(
        AssetPathService assets,
        ILogger<EntityAssetCleanupService> logger) {
        this.assets = assets;
        this.logger = logger;
    }

    /// <summary>Internal compatibility constructor for existing services that already own a logger.</summary>
    internal EntityAssetCleanupService(AssetPathService assets, ILogger logger) {
        this.assets = assets;
        this.logger = logger;
    }

    /// <summary>
    /// Removes generated caches for <paramref name="entityIds"/>. When
    /// <paramref name="preserveArtwork"/> is false it also removes grid thumbnails, provider artwork,
    /// and each captured <c>/assets/</c> file path. Paths outside the configured cache root are ignored.
    /// </summary>
    public void Cleanup(
        IReadOnlyCollection<Guid> entityIds,
        bool preserveArtwork,
        IReadOnlyCollection<string>? recordedAssetPaths = null) {
        foreach (var id in entityIds.Distinct()) {
            foreach (var root in GeneratedDirectoryRoots) {
                TryDeleteDirectory(Path.Combine(assets.CacheRoot, root, id.ToString()));
            }

            TryDeleteFile(Path.Combine(assets.CacheRoot, "keyframes", $"{id}.json"));
            if (preserveArtwork) {
                continue;
            }

            TryDeleteFile(assets.GridThumbnailPath(id));
            TryDeleteFile(assets.GridThumbnail2xPath(id));
            TryDeleteDirectory(Path.Combine(assets.CacheRoot, "plugins", "artwork", id.ToString()));
        }

        if (preserveArtwork || recordedAssetPaths is null) {
            return;
        }

        foreach (var path in recordedAssetPaths.Distinct(StringComparer.Ordinal)) {
            if (assets.ResolveAssetDiskPath(path) is { } diskPath) {
                TryDeleteFile(diskPath);
            }
        }
    }

    private void TryDeleteDirectory(string path) {
        try {
            if (Directory.Exists(path)) {
                Directory.Delete(path, recursive: true);
            }
        } catch (Exception exception) {
            logger.LogWarning(exception, "Entity asset cleanup failed to remove cache directory {Path}.", path);
        }
    }

    private void TryDeleteFile(string path) {
        try {
            if (File.Exists(path)) {
                File.Delete(path);
            }
        } catch (Exception exception) {
            logger.LogWarning(exception, "Entity asset cleanup failed to remove cache file {Path}.", path);
        }
    }
}
