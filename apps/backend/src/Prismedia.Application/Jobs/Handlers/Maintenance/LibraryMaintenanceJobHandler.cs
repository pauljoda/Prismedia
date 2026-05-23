using Microsoft.Extensions.Logging;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers.Maintenance;

/// <summary>
/// Performs library maintenance: validates generated cache assets exist on disk
/// and removes orphaned cache directories whose entities no longer exist.
/// </summary>
public sealed class LibraryMaintenanceJobHandler(
    ILogger<LibraryMaintenanceJobHandler> logger,
    IMaintenancePersistence persistence,
    IMediaAssetGenerator assets) : IJobHandler {
    public JobType Type => JobType.LibraryMaintenance;

    private static readonly (EntityKind Kind, string CacheSubdir)[] AssetKinds =
    [
        (EntityKind.Video, "videos"),
        (EntityKind.Image, "images"),
        (EntityKind.BookPage, "book-pages"),
        (EntityKind.AudioTrack, "audio-tracks")
    ];

    public async Task HandleAsync(JobContext context, CancellationToken cancellationToken) {
        await context.ReportProgressAsync(5, "Starting maintenance", cancellationToken);

        var totalOrphansRemoved = 0;
        var totalMissingAssets = 0;
        var progressPerKind = 90 / AssetKinds.Length;

        for (var i = 0; i < AssetKinds.Length; i++) {
            var (kind, cacheSubdir) = AssetKinds[i];

            var entityIds = await persistence.GetActiveEntityIdsByKindAsync(kind, cancellationToken);
            var activeIdSet = new HashSet<string>(entityIds.Select(id => id.ToString()), StringComparer.OrdinalIgnoreCase);

            var missing = ValidateAssets(kind, entityIds);
            totalMissingAssets += missing;

            var orphans = CleanOrphanedCacheDirs(cacheSubdir, activeIdSet);
            totalOrphansRemoved += orphans;

            var progress = 5 + ((i + 1) * progressPerKind);
            await context.ReportProgressAsync(progress,
                $"{kind}: {entityIds.Count} entities, {missing} missing assets, {orphans} orphans cleaned",
                cancellationToken);
        }

        logger.LogInformation(
            "LibraryMaintenance complete: {MissingAssets} missing assets found, {OrphansRemoved} orphaned cache dirs removed",
            totalMissingAssets, totalOrphansRemoved);

        await context.ReportProgressAsync(100,
            $"Maintenance complete: {totalMissingAssets} missing, {totalOrphansRemoved} orphans cleaned",
            cancellationToken);
    }

    private int ValidateAssets(EntityKind kind, IReadOnlyList<Guid> entityIds) {
        var missing = 0;

        foreach (var id in entityIds) {
            var expectedPaths = GetExpectedAssetPaths(kind, id);
            foreach (var path in expectedPaths) {
                if (!File.Exists(path)) {
                    missing++;
                    logger.LogDebug("Missing asset for {Kind} {EntityId}: {Path}", kind, id, path);
                }
            }
        }

        return missing;
    }

    private IReadOnlyList<string> GetExpectedAssetPaths(EntityKind kind, Guid entityId) {
        return kind switch {
            EntityKind.Video => [assets.VideoThumbnailPath(entityId)],
            EntityKind.Image => [assets.ImageThumbnailPath(entityId)],
            EntityKind.BookPage => [assets.BookPageThumbnailPath(entityId)],
            EntityKind.AudioTrack => [assets.AudioWaveformPath(entityId)],
            _ => []
        };
    }

    private int CleanOrphanedCacheDirs(string cacheSubdir, HashSet<string> activeIdSet) {
        var cacheBase = persistence.GetCacheBasePath();
        var kindCacheDir = Path.Combine(cacheBase, cacheSubdir);
        if (!Directory.Exists(kindCacheDir)) return 0;

        var orphans = 0;
        foreach (var dir in Directory.GetDirectories(kindCacheDir)) {
            var dirName = Path.GetFileName(dir);
            if (activeIdSet.Contains(dirName)) continue;

            try {
                Directory.Delete(dir, recursive: true);
                orphans++;
                logger.LogInformation("Removed orphaned cache dir: {Path}", dir);
            } catch (Exception ex) {
                logger.LogWarning(ex, "Failed to remove orphaned cache dir: {Path}", dir);
            }
        }

        return orphans;
    }
}
