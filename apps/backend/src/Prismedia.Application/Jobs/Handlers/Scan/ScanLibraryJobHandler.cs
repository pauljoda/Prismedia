using Prismedia.Application.Jobs.Handlers;
using Microsoft.Extensions.Logging;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Files;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Application.Jobs.Scanning;
using Prismedia.Domain.Entities;
using System.Text.RegularExpressions;

namespace Prismedia.Application.Jobs.Handlers.Scan;

/// <summary>
/// Discovers video files in a configured library root, creates or updates video entities,
/// removes stale entries, and chains downstream probe/fingerprint/preview jobs.
/// Optimized for throughput: batch entity upserts, batch downstream checks, batch job enqueues.
/// </summary>
public sealed class ScanLibraryJobHandler(
    ILogger<ScanLibraryJobHandler> logger,
    IFileDiscovery fileDiscovery,
    ILibraryScanRootPersistence roots,
    IVideoScanPersistence videos,
    IDownstreamNeedsPersistence downstreamNeeds,
    IScanSnapshotStore? snapshots = null,
    IVideoSidecarMetadataReader? sidecars = null,
    IScanMetadataPersistence? scanMetadata = null,
    IAcquisitionHintApplier? acquisitionHints = null,
    IMediaProcessingStatePersistence? processingState = null,
    VideoScanConcurrencyGate? scanGate = null)
    : ScanJobHandler(logger, fileDiscovery, roots, snapshots, processingState) {
    private const int BatchSize = 50;
    private static readonly Regex SeasonFolderPattern = new(
        @"^(?:Season\s*(?<season>\d{1,3})|S(?<season>\d{1,3}))$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex EpisodeTokenPattern = new(
        @"(?:^|[\s._\-(\[])[Ss](?<season>\d{1,3})[\s._-]*[Ee](?<episode>\d{1,4})(?:\D|$)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex GeneratedVideoArtifactDirectoryPattern = new(
        @"(?:^|[-._\s])(?:trickplay)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public override JobType Type => JobType.ScanLibrary;

    protected override bool IsEligibleRoot(LibraryRootData root) => root.ScanVideos;

    protected override IReadOnlyList<MediaCategory> ScanCategories => [MediaCategory.Video];

    protected override async ValueTask<IAsyncDisposable?> EnterScanScopeAsync(
        LibraryRootData root, CancellationToken cancellationToken) =>
        scanGate is null ? null : await scanGate.EnterAsync(cancellationToken);

    protected override Task OnNoFileChangesAsync(
        JobContext context, LibraryRootData root, CancellationToken cancellationToken) =>
        AutoIdentifyScanEnqueue.EnqueueExistingRootsForUnchangedScanAsync(
            context, Roots, downstreamNeeds, root, ScanCategories, cancellationToken);

    /// <summary>
    /// Materializes only one movie import's exact files through the video scanner's canonical
    /// classification, wanted binding, batch upsert, and downstream planning. No root-wide stale
    /// cleanup runs, so the import cannot remove unrelated videos while proving its own readiness.
    /// </summary>
    public async Task MaterializeImportedPathsAsync(
        JobContext context,
        Guid acquisitionId,
        LibraryRootData root,
        IReadOnlyList<string> placedPaths,
        CancellationToken cancellationToken) {
        if (!root.Enabled || !root.ScanVideos) {
            throw new InvalidOperationException("The imported movie no longer belongs to an enabled video library root.");
        }

        await using var scanLease = scanGate is null ? null : await scanGate.EnterAsync(cancellationToken);
        var files = placedPaths.Select(Path.GetFullPath).ToArray();
        var settings = await Roots.GetSettingsAsync(cancellationToken);
        if (!root.AutoIdentify) {
            settings = settings with { AutoIdentifyEnabled = false };
        }

        var items = new List<VideoUpsertItem>(files.Length);
        foreach (var filePath in files) {
            var sidecar = sidecars is null
                ? null
                : await sidecars.ReadAsync(filePath, cancellationToken);
            var item = BuildVideoUpsertItem(filePath, root, files, sidecar);
            await VideoWantedBinding.BindAsync(
                acquisitionHints,
                item,
                cancellationToken,
                acquisitionId);
            items.Add(item);
        }

        var failedPaths = new List<string>();
        var (entityIds, persistedItems) = await UpsertBatchWithIsolationAsync(
            items,
            failedPaths,
            cancellationToken);
        if (failedPaths.Count > 0 || entityIds.Count != items.Count) {
            throw new InvalidOperationException("One or more imported movie files could not be persisted.");
        }

        if (scanMetadata is not null) {
            for (var index = 0; index < persistedItems.Count; index++) {
                if (persistedItems[index].Metadata is not { } metadata) {
                    continue;
                }

                await scanMetadata.ApplyVideoSidecarMetadataAsync(
                    entityIds[index],
                    metadata,
                    Path.GetFileNameWithoutExtension(persistedItems[index].FilePath),
                    persistedItems[index].IsNsfw,
                    cancellationToken);
            }
        }

        var needs = await downstreamNeeds.CheckDownstreamNeedsBatchAsync(entityIds, cancellationToken);
        var downstreamJobs = new List<EnqueueJobRequest>();
        for (var index = 0; index < entityIds.Count; index++) {
            if (needs.TryGetValue(entityIds[index], out var entityNeeds)) {
                downstreamJobs.AddRange(VideoDownstreamJobPlanner.Build(
                    settings,
                    entityIds[index],
                    persistedItems[index].FilePath,
                    entityNeeds));
            }
        }

        if (downstreamJobs.Count > 0) {
            await ImportedMaterializationHousekeeping.TryAsync(
                logger,
                "Imported movie is ready but its downstream jobs could not be queued.",
                () => context.EnqueueBatchAsync(downstreamJobs, cancellationToken));
        }

        if (acquisitionHints is not null) {
            var owners = await acquisitionHints.ApplyToFolderOwnersAsync(
                cancellationToken,
                acquisitionId);
            foreach (var owner in owners) {
                await ImportedMaterializationHousekeeping.TryAsync(
                    logger,
                    "Imported movie is ready but its identify job could not be queued.",
                    () => context.EnqueueIfNeededAsync(new EnqueueJobRequest(
                        JobType.AutoIdentify,
                        TargetEntityKind: owner.TopLevelKindCode,
                        TargetEntityId: owner.TopLevelEntityId.ToString(),
                        TargetLabel: owner.TopLevelTitle,
                        Priority: JobPriorities.AutoIdentify), cancellationToken));
            }
        }

        await ImportedMaterializationHousekeeping.TryAsync(
            logger,
            "Imported movie is ready but automatic identification housekeeping could not be queued.",
            () => AutoIdentifyScanEnqueue.EnqueueRootsAsync(
                context,
                settings,
                downstreamNeeds,
                entityIds,
                cancellationToken));
    }

    protected override async Task<ScanRootOutcome> ScanRootCoreAsync(
        JobContext context, LibraryRootData root, CancellationToken cancellationToken) {
        var timer = new JobPhaseTimer();

        IReadOnlyList<string> files;
        var excludedPaths = await Roots.GetExcludedPathsForRootAsync(root.Id, cancellationToken);
        using (timer.Phase("discover")) {
            logger.LogInformation("ScanLibrary: discovering videos in {Path}", root.Path);
            files = await FileDiscovery.DiscoverFilesAsync(
                root.Path, MediaCategory.Video, root.Recursive, excludedPaths, cancellationToken);
            logger.LogInformation("ScanLibrary: found {Count} video files in {Label}", files.Count, root.Label);
        }

        var settings = await Roots.GetSettingsAsync(cancellationToken);
        if (!root.AutoIdentify) {
            // Honor this root's Auto Identify opt-out without touching other generation settings.
            settings = settings with { AutoIdentifyEnabled = false };
        }
        var allEntityIds = new List<Guid>(files.Count);
        // Parallel to allEntityIds: the source path behind each persisted entity id. Files whose
        // persistence failed are skipped, so downstream label lookups cannot index `files` directly.
        var scannedPaths = new List<string>(files.Count);
        var failedPaths = new List<string>();
        var validPaths = new HashSet<string>(files.Count, FileSystemPathComparison.Comparer);
        var validMovieFolders = new HashSet<string>(FileSystemPathComparison.Comparer);

        using (timer.Phase("upsert")) {
            for (var batchStart = 0; batchStart < files.Count; batchStart += BatchSize) {
                var batchEnd = Math.Min(batchStart + BatchSize, files.Count);
                var batchItems = new List<VideoUpsertItem>(batchEnd - batchStart);

                for (var i = batchStart; i < batchEnd; i++) {
                    var filePath = files[i];
                    validPaths.Add(filePath);
                    var sidecar = sidecars is null
                        ? null
                        : await sidecars.ReadAsync(filePath, cancellationToken);
                    var item = BuildVideoUpsertItem(filePath, root, files, sidecar);
                    if (item.Movie is { } movie) {
                        validMovieFolders.Add(movie.FolderPath);
                    }

                    await VideoWantedBinding.BindAsync(acquisitionHints, item, cancellationToken);

                    batchItems.Add(item);
                }

                var (entityIds, persistedItems) = await UpsertBatchWithIsolationAsync(batchItems, failedPaths, cancellationToken);
                if (scanMetadata is not null) {
                    for (var i = 0; i < persistedItems.Count && i < entityIds.Count; i++) {
                        if (persistedItems[i].Metadata is not { } metadata) {
                            continue;
                        }

                        await scanMetadata.ApplyVideoSidecarMetadataAsync(
                            entityIds[i],
                            metadata,
                            Path.GetFileNameWithoutExtension(persistedItems[i].FilePath),
                            persistedItems[i].IsNsfw,
                            cancellationToken);
                    }
                }
                allEntityIds.AddRange(entityIds);
                scannedPaths.AddRange(persistedItems.Select(item => item.FilePath));

                await context.ReportProgressAsync(
                    batchEnd * 60 / files.Count,
                    $"Upserted {batchEnd}/{files.Count}", cancellationToken);
            }
        }

        using (timer.Phase("enqueue")) {
            for (var batchStart = 0; batchStart < allEntityIds.Count; batchStart += BatchSize) {
                var batchEnd = Math.Min(batchStart + BatchSize, allEntityIds.Count);
                var batchIds = allEntityIds.GetRange(batchStart, batchEnd - batchStart);

                var needs = await downstreamNeeds.CheckDownstreamNeedsBatchAsync(batchIds, cancellationToken);
                var jobRequests = new List<EnqueueJobRequest>();

                for (var i = 0; i < batchIds.Count; i++) {
                    var entityId = batchIds[i];
                    if (needs.TryGetValue(entityId, out var entityNeeds)) {
                        jobRequests.AddRange(VideoDownstreamJobPlanner.Build(
                            settings,
                            entityId,
                            scannedPaths[batchStart + i],
                            entityNeeds));
                    }
                }

                if (jobRequests.Count > 0) {
                    var enqueued = await context.EnqueueBatchAsync(jobRequests, cancellationToken);
                    logger.LogDebug("ScanLibrary: enqueued {Enqueued}/{Total} downstream jobs for batch", enqueued, jobRequests.Count);
                }

                await context.ReportProgressAsync(
                    60 + (batchEnd * 30 / allEntityIds.Count),
                    $"Enqueued downstream for {batchEnd}/{allEntityIds.Count}", cancellationToken);
            }
        }

        // Acquisition-imported content: stamp the acquisition's provider ids onto the entities owning the
        // imported paths and identify each affected root — deliberately bypassing the global/per-root
        // auto-identify gates, because an acquisition import is explicit user intent and the stamped ids
        // let identify resolve ID-first instead of leaving the imported tree metadata-less. This MUST run
        // before generic auto-identify is queued so a fast worker cannot claim the same root ID-less.
        if (acquisitionHints is not null) {
            foreach (var owner in await acquisitionHints.ApplyToFolderOwnersAsync(cancellationToken)) {
                await context.EnqueueIfNeededAsync(new EnqueueJobRequest(
                    JobType.AutoIdentify,
                    TargetEntityKind: owner.TopLevelKindCode,
                    TargetEntityId: owner.TopLevelEntityId.ToString(),
                    TargetLabel: owner.TopLevelTitle,
                    Priority: JobPriorities.AutoIdentify), cancellationToken);
            }
        }

        // Auto identify the top-level ancestors only (a series rather than each episode), so one job
        // identifies the whole tree and episodes are filled by cascading from it.
        await AutoIdentifyScanEnqueue.EnqueueRootsAsync(context, settings, downstreamNeeds, allEntityIds, cancellationToken);

        int removed;
        int orphans;
        using (timer.Phase("cleanup")) {
            removed = await videos.RemoveStaleVideosByRootAsync(root.Id, validPaths, cancellationToken);
            if (removed > 0)
                logger.LogInformation("ScanLibrary: removed {Count} stale video entities from {Label}", removed, root.Label);

            var staleMovies = await videos.RemoveStaleMoviesByRootAsync(root.Id, validMovieFolders, cancellationToken);
            if (staleMovies > 0)
                logger.LogInformation("ScanLibrary: removed {Count} stale movie entities from {Label}", staleMovies, root.Label);

            var excluded = await Roots.RemoveEntitiesInExcludedPathsAsync(root.Id, cancellationToken);
            if (excluded > 0)
                logger.LogInformation("ScanLibrary: removed {Count} excluded entities from {Label}", excluded, root.Label);

            orphans = await videos.RemoveOrphanSeriesAndSeasonsAsync(cancellationToken);
            if (orphans > 0)
                logger.LogInformation("ScanLibrary: removed {Count} orphan movie/series/season entities", orphans);
        }

        var report = timer.Finish();
        logger.LogInformation(
            "[METRICS] scan-library {Label} — {FileCount} files, {Removed} stale, {Orphans} orphans — {Timing}",
            root.Label, files.Count, removed, orphans, report.ToLogString());

        return failedPaths.Count == 0 ? ScanRootOutcome.Success : new ScanRootOutcome(failedPaths);
    }

    /// <summary>
    /// Upserts a batch, and when the batch as a whole fails, retries its files one by one so a
    /// single bad file is skipped (and reported) instead of aborting the entire scan — one poison
    /// file used to freeze entity creation for the whole library. Returns the persisted ids with
    /// the items they belong to, position-aligned.
    /// </summary>
    private async Task<(IReadOnlyList<Guid> EntityIds, IReadOnlyList<VideoUpsertItem> PersistedItems)> UpsertBatchWithIsolationAsync(
        List<VideoUpsertItem> batchItems, List<string> failedPaths, CancellationToken cancellationToken) {
        try {
            var ids = await videos.UpsertVideosBatchAsync(batchItems, cancellationToken);
            return (ids, batchItems);
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            await videos.DiscardPendingScanChangesAsync(cancellationToken);
            logger.LogWarning(ex,
                "ScanLibrary: batch upsert of {Count} files failed; retrying files individually", batchItems.Count);
        }

        var entityIds = new List<Guid>(batchItems.Count);
        var persisted = new List<VideoUpsertItem>(batchItems.Count);
        foreach (var item in batchItems) {
            try {
                entityIds.AddRange(await videos.UpsertVideosBatchAsync([item], cancellationToken));
                persisted.Add(item);
            } catch (Exception ex) when (ex is not OperationCanceledException) {
                await videos.DiscardPendingScanChangesAsync(cancellationToken);
                failedPaths.Add(item.FilePath);
                logger.LogError(ex, "ScanLibrary: skipping {Path} — persistence failed", item.FilePath);
            }
        }

        return (entityIds, persisted);
    }

    private static VideoUpsertItem BuildVideoUpsertItem(
        string filePath,
        LibraryRootData root,
        IReadOnlyList<string> allFiles,
        VideoSidecarMetadata? metadata = null) {
        var fallbackTitle = Path.GetFileNameWithoutExtension(filePath);
        var title = string.IsNullOrWhiteSpace(metadata?.Title) ? fallbackTitle : metadata.Title.Trim();
        var episodeToken = ParseEpisodeToken(fallbackTitle);
        var parentFolder = Path.GetDirectoryName(filePath);

        if (!string.IsNullOrWhiteSpace(parentFolder)) {
            if (TryGetMovieFolderInfo(filePath, root.Path, allFiles, out var movieFolderPath, out var movieFolderName)) {
                return new VideoUpsertItem(
                    filePath,
                    title,
                    root.Id,
                    root.IsNsfw,
                    Series: null,
                    Season: null,
                    EpisodeNumber: null,
                    AbsoluteEpisodeNumber: null,
                    Metadata: metadata,
                    Movie: new MovieScanInfo(movieFolderPath, string.IsNullOrWhiteSpace(metadata?.Title) ? movieFolderName : metadata.Title.Trim()));
            }

            var parentFolderName = Path.GetFileName(parentFolder);
            if (TryParseSeasonFolder(parentFolderName, out var seasonNumber)) {
                var seriesFolder = Path.GetDirectoryName(parentFolder);
                if (!string.IsNullOrWhiteSpace(seriesFolder) && !SamePath(seriesFolder, root.Path)) {
                    return new VideoUpsertItem(
                        filePath,
                        title,
                        root.Id,
                        root.IsNsfw,
                        new VideoSeriesScanInfo(seriesFolder, Path.GetFileName(seriesFolder)),
                        new VideoSeasonScanInfo(parentFolder, parentFolderName, seasonNumber),
                        episodeToken?.EpisodeNumber,
                        AbsoluteEpisodeNumber: null,
                        Metadata: metadata);
                }
            }

            var grandparentFolder = Path.GetDirectoryName(parentFolder);
            if (episodeToken is not null &&
                !string.IsNullOrWhiteSpace(grandparentFolder) &&
                !SamePath(grandparentFolder, root.Path)) {
                return new VideoUpsertItem(
                    filePath,
                    title,
                    root.Id,
                    root.IsNsfw,
                    new VideoSeriesScanInfo(grandparentFolder, Path.GetFileName(grandparentFolder)),
                    new VideoSeasonScanInfo(parentFolder, parentFolderName, episodeToken.SeasonNumber),
                    episodeToken.EpisodeNumber,
                    AbsoluteEpisodeNumber: null,
                    Metadata: metadata);
            }

            if (episodeToken is not null && !SamePath(parentFolder, root.Path)) {
                return new VideoUpsertItem(
                    filePath,
                    title,
                    root.Id,
                    root.IsNsfw,
                    new VideoSeriesScanInfo(parentFolder, parentFolderName),
                    Season: null,
                    episodeToken.EpisodeNumber,
                    AbsoluteEpisodeNumber: null,
                    Metadata: metadata);
            }

            // A non-root folder holding more than one video, with no season folders or episode tokens, is
            // treated as a series: each loose video becomes an episode ordered by filename. This mirrors the
            // single-video movie rule above — a lone video in a folder is a movie, so a folder with several
            // videos is the series counterpart. Without this, such folders fall through to ungrouped loose
            // videos.
            if (!SamePath(parentFolder, root.Path) &&
                TryGetFolderSeriesPosition(filePath, parentFolder, allFiles, out var folderSortOrder)) {
                return new VideoUpsertItem(
                    filePath,
                    title,
                    root.Id,
                    root.IsNsfw,
                    new VideoSeriesScanInfo(parentFolder, parentFolderName),
                    Season: null,
                    EpisodeNumber: null,
                    AbsoluteEpisodeNumber: null,
                    Metadata: metadata,
                    FolderSortOrder: folderSortOrder);
            }
        }

        return new VideoUpsertItem(filePath, title, root.Id, root.IsNsfw, Metadata: metadata);
    }

    /// <summary>
    /// Determines whether a video belongs to a folder that should become a series because it holds more
    /// than one video, and reports the video's alphabetical position among its sibling videos so episodes
    /// without explicit numbering still order deterministically by filename.
    /// </summary>
    private static bool TryGetFolderSeriesPosition(
        string filePath,
        string parentFolder,
        IReadOnlyList<string> allFiles,
        out int sortOrder) {
        sortOrder = 0;

        var siblings = new List<string>();
        foreach (var candidate in allFiles) {
            var candidateParent = Path.GetDirectoryName(candidate);
            if (!string.IsNullOrWhiteSpace(candidateParent) && SamePath(candidateParent, parentFolder)) {
                siblings.Add(candidate);
            }
        }

        if (siblings.Count < 2) {
            return false;
        }

        siblings.Sort(static (left, right) => string.Compare(
            Path.GetFileName(left), Path.GetFileName(right), StringComparison.OrdinalIgnoreCase));
        var index = siblings.FindIndex(path => SamePath(path, filePath));
        sortOrder = index < 0 ? 0 : index;
        return true;
    }

    private static bool TryGetMovieFolderInfo(
        string filePath,
        string rootPath,
        IReadOnlyList<string> allFiles,
        out string movieFolderPath,
        out string movieFolderName) {
        movieFolderPath = string.Empty;
        movieFolderName = string.Empty;

        var parentFolder = Path.GetDirectoryName(filePath);
        if (string.IsNullOrWhiteSpace(parentFolder) || SamePath(parentFolder, rootPath)) {
            return false;
        }

        var grandparentFolder = Path.GetDirectoryName(parentFolder);
        if (string.IsNullOrWhiteSpace(grandparentFolder) || !SamePath(grandparentFolder, rootPath)) {
            return false;
        }

        movieFolderName = Path.GetFileName(parentFolder);
        var fileTitle = Path.GetFileNameWithoutExtension(filePath);
        // A lone video sitting directly inside a folder under the root is treated as a movie regardless
        // of how the release filename is spelled (folders rarely match release names like
        // "Pokemon.The.First.Movie..." or "sweeney-todd-1982.ia"). Episodic tokens still route the file
        // to series handling instead, and the single-direct-file / no-content-subfolder checks below
        // keep multi-file or season-structured folders out of the movie path.
        if (ParseEpisodeToken(fileTitle) is not null) {
            return false;
        }

        if (Directory.Exists(parentFolder) && HasContentSubdirectories(parentFolder)) {
            return false;
        }

        var directFilesInFolder = 0;
        foreach (var candidate in allFiles) {
            var candidateParent = Path.GetDirectoryName(candidate);
            if (string.IsNullOrWhiteSpace(candidateParent)) {
                continue;
            }

            if (SamePath(candidateParent, parentFolder)) {
                directFilesInFolder++;
                continue;
            }

            if (IsPathUnderRoot(candidate, parentFolder)) {
                return false;
            }
        }

        if (directFilesInFolder != 1) {
            return false;
        }

        movieFolderPath = parentFolder;
        return true;
    }

    private static bool HasContentSubdirectories(string folderPath) {
        foreach (var directory in Directory.EnumerateDirectories(folderPath)) {
            var name = Path.GetFileName(directory);
            // Hidden/dot directories (".thumbs", ".actors", ".AppleDouble", …) and generated
            // artifact directories (a sibling "*.trickplay") are bookkeeping, not real content,
            // so they must not disqualify an otherwise single-file movie folder.
            if (name.StartsWith('.') || GeneratedVideoArtifactDirectoryPattern.IsMatch(name)) {
                continue;
            }

            return true;
        }

        return false;
    }

    private static bool TryParseSeasonFolder(string folderName, out int seasonNumber) {
        var match = SeasonFolderPattern.Match(folderName);
        if (match.Success && int.TryParse(match.Groups["season"].Value, out seasonNumber)) {
            return true;
        }

        seasonNumber = 0;
        return false;
    }

    private static EpisodeToken? ParseEpisodeToken(string fileName) {
        var match = EpisodeTokenPattern.Match(fileName);
        if (!match.Success) {
            return null;
        }

        return int.TryParse(match.Groups["season"].Value, out var seasonNumber) &&
            int.TryParse(match.Groups["episode"].Value, out var episodeNumber)
            ? new EpisodeToken(seasonNumber, episodeNumber)
            : null;
    }

    private static bool SamePath(string left, string right) =>
        FileSystemPathComparison.Equals(
            Path.TrimEndingDirectorySeparator(left),
            Path.TrimEndingDirectorySeparator(right));

    private static bool IsPathUnderRoot(string path, string rootPath) {
        var relative = Path.GetRelativePath(
            Path.TrimEndingDirectorySeparator(rootPath),
            Path.TrimEndingDirectorySeparator(path));
        return relative.Length > 0 &&
            !relative.StartsWith("..", StringComparison.Ordinal) &&
            !Path.IsPathRooted(relative);
    }

    private sealed record EpisodeToken(int SeasonNumber, int EpisodeNumber);
}
