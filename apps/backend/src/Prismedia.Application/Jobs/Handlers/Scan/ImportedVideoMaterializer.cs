using Microsoft.Extensions.Logging;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Entities;
using Prismedia.Application.Files;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers.Scan;

/// <summary>
/// Reconciles the exact episode files placed by a TV acquisition before the acquisition may become
/// Imported. This is intentionally narrower than a library scan: it creates the playable Entity tree,
/// consumes acquisition identity hints, and queues downstream work without snapshots or stale cleanup.
/// </summary>
public sealed class ImportedVideoMaterializer(
    IVideoScanPersistence videos,
    IAcquisitionHintApplier acquisitionHints,
    ILogger<ImportedVideoMaterializer> logger,
    IMaintenancePersistence? maintenance = null,
    IScanSnapshotStore? snapshots = null,
    IEntityHierarchyReader? hierarchy = null) : IImportedVideoMaterializer {
    public async Task<ImportedEntityMaterializationResult> MaterializeAsync(
        JobContext context,
        ImportedTvMaterializationRequest request,
        CancellationToken cancellationToken) {
        if (request.Episodes.Count == 0) {
            throw new InvalidOperationException("A TV import cannot complete without placed episode files.");
        }

        var seriesFolder = Path.GetFullPath(request.SeriesFolderPath);
        EnsureInsideRoot(seriesFolder, request.Root.Path);

        var items = new List<VideoUpsertItem>(request.Episodes.Count);
        var replacementOwnerIds = new HashSet<Guid>();
        var replacementPaths = new HashSet<string>(FileSystemPathComparison.Comparer);
        var readyOwnerSources = new Dictionary<Guid, string>();
        foreach (var episode in request.Episodes) {
            var filePath = Path.GetFullPath(episode.FilePath);
            EnsureInsideRoot(filePath, request.Root.Path);
            if (!File.Exists(filePath)) {
                throw new FileNotFoundException("A placed TV episode disappeared before it could be cataloged.", filePath);
            }

            var seasonFolder = Path.GetDirectoryName(filePath)
                ?? throw new InvalidOperationException($"The imported episode has no season folder: {filePath}");
            if (!string.IsNullOrWhiteSpace(episode.PreviousFilePath)) {
                var previousFilePath = Path.GetFullPath(episode.PreviousFilePath);
                replacementPaths.Add(filePath);
                foreach (var ownerId in await videos.RebindPlayableVideoSourceAsync(
                             previousFilePath,
                             filePath,
                             cancellationToken)) {
                    replacementOwnerIds.Add(ownerId);
                    readyOwnerSources[ownerId] = filePath;
                }
            }

            var item = new VideoUpsertItem(
                filePath,
                Path.GetFileNameWithoutExtension(filePath),
                request.Root.Id,
                request.Root.IsNsfw,
                Series: new VideoSeriesScanInfo(seriesFolder, Path.GetFileName(seriesFolder)),
                Season: new VideoSeasonScanInfo(seasonFolder, Path.GetFileName(seasonFolder), episode.SeasonNumber),
                EpisodeNumber: episode.EpisodeNumber,
                ScanPlacement: PlayableVideoScanPlacement.Episode);
            await VideoWantedBinding.BindAsync(
                acquisitionHints,
                item,
                cancellationToken,
                request.AcquisitionId);
            foreach (var coveredEpisode in episode.CoveredEpisodeNumbers) {
                if (await acquisitionHints.BindWantedChildBySortOrderAsync(
                    EntityKind.VideoEpisode,
                    seasonFolder,
                    coveredEpisode,
                    filePath,
                    cancellationToken) is { } coveredOwnerId) {
                    readyOwnerSources[coveredOwnerId] = filePath;
                }
            }

            items.Add(item);
        }

        IReadOnlyList<Guid> entityIds;
        try {
            entityIds = await videos.UpsertVideosBatchAsync(items, cancellationToken);
        } catch {
            await videos.DiscardPendingScanChangesAsync(CancellationToken.None);
            throw;
        }

        if (entityIds.Count != items.Count) {
            throw new InvalidOperationException(
                $"Cataloged {entityIds.Count} of {items.Count} imported TV files; the acquisition remains incomplete.");
        }
        for (var index = 0; index < entityIds.Count; index++) {
            readyOwnerSources[entityIds[index]] = items[index].FilePath;
        }

        // The positional upsert returns one ID per physical file, while a multi-episode source can own
        // several episode Entities. Reload every owner after the commit so every covered episode gets
        // downstream readiness work. This also recovers replacement owners on a retry after Rebind
        // committed but the first batch upsert failed.
        foreach (var owner in await videos.ListPlayableVideoSourceOwnersAsync(
                     items.Select(item => item.FilePath).ToArray(),
                     cancellationToken)) {
            var sourcePath = Path.GetFullPath(owner.FilePath);
            readyOwnerSources[owner.EntityId] = sourcePath;
            if (replacementPaths.Contains(sourcePath)) {
                replacementOwnerIds.Add(owner.EntityId);
            }
        }

        if (maintenance is not null) {
            foreach (var ownerId in replacementOwnerIds) {
                await maintenance.ClearGeneratedPreviewAssetsAsync(
                    EntityKind.VideoEpisode,
                    ownerId,
                    cancellationToken);
            }
        }

        var readyEntityIds = readyOwnerSources.Keys.ToArray();
        if (snapshots is not null) {
            await ImportedScanSnapshot.ApplyAsync(
                snapshots,
                request.Root.Id,
                JobType.ScanLibrary,
                items.Select(item => item.FilePath).ToArray(),
                request.Episodes
                    .Where(episode => !string.IsNullOrWhiteSpace(episode.PreviousFilePath))
                    .Select(episode => episode.PreviousFilePath!)
                    .ToArray(),
                cancellationToken);
        }
        var touchedAncestors = new HashSet<Guid>();
        if (hierarchy is not null) {
            foreach (var entityId in readyEntityIds) {
                touchedAncestors.UnionWith(await hierarchy.ListAncestorIdsAsync(entityId, cancellationToken));
            }
        }

        // An acquisition is explicit user intent. Stamp its identities before publishing any job, then
        // identify only the exact episodes materialized above. The payload permits this intentional child
        // target while ordinary scans remain root-only and continue to cascade from a series.
        await acquisitionHints.ApplyToFolderOwnersAsync(cancellationToken, request.AcquisitionId);
        logger.LogInformation(
            "Materialized {Count} imported TV file(s) in {SeriesFolder} before marking the acquisition imported.",
            entityIds.Count,
            seriesFolder);
        return new ImportedEntityMaterializationResult(
            readyEntityIds.Select(entityId => new ImportedEntityReference(entityId, EntityKind.VideoEpisode)).ToArray(),
            touchedAncestors.ToArray(),
            items.Select(item => item.FilePath).ToArray(),
            request.Episodes
                .Where(episode => !string.IsNullOrWhiteSpace(episode.PreviousFilePath))
                .Select(episode => Path.GetFullPath(episode.PreviousFilePath!))
                .ToArray(),
            []);
    }

    private static void EnsureInsideRoot(string candidate, string rootPath) {
        var root = Path.GetFullPath(rootPath);
        var normalizedRoot = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(normalizedRoot, FileSystemPathComparison.Comparison)) {
            throw new InvalidOperationException($"The imported path is outside its video library root: {candidate}");
        }
    }
}
