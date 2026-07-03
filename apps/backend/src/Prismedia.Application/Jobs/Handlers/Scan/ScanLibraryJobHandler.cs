using Prismedia.Application.Jobs.Handlers;
using Microsoft.Extensions.Logging;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Jobs.Ports;
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
    IAcquisitionHintApplier? acquisitionHints = null) : ScanJobHandler(logger, fileDiscovery, roots, snapshots) {
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

    protected override Task OnNoFileChangesAsync(
        JobContext context, LibraryRootData root, CancellationToken cancellationToken) =>
        AutoIdentifyScanEnqueue.EnqueueExistingRootsForUnchangedScanAsync(
            context, Roots, downstreamNeeds, root, ScanCategories, cancellationToken);

    protected override async Task ScanRootCoreAsync(
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
        var validPaths = new HashSet<string>(files.Count, StringComparer.OrdinalIgnoreCase);
        var validMovieFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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
                        if (acquisitionHints is not null) {
                            // Bind a request-created wanted Movie to this folder BEFORE the upsert, so the
                            // path-keyed movie upsert finds the wanted entity instead of creating a duplicate.
                            await acquisitionHints.BindWantedEntityAsync(EntityKind.Movie, movie.FolderPath, cancellationToken);
                        }
                    }

                    // Bind the wanted TV tree BEFORE the upserts, top-down: the series grouping by
                    // folder (the ancestor of an imported season/episode hint), then its phantom season
                    // and episode by their sibling positions — so the path/position-keyed upserts find
                    // the request-created entities instead of creating duplicates.
                    if (acquisitionHints is not null && item.Series is { } seriesInfo) {
                        await acquisitionHints.BindWantedParentAsync(EntityKind.VideoSeries, seriesInfo.FolderPath, cancellationToken);
                        if (item.Season is { } seasonInfo) {
                            // A season-pack hint names the wanted season directly; phantom seasons of a
                            // bound series also match by their sibling position.
                            await acquisitionHints.BindWantedEntityAsync(EntityKind.VideoSeason, seasonInfo.FolderPath, cancellationToken);
                            await acquisitionHints.BindWantedChildBySortOrderAsync(
                                EntityKind.VideoSeason, seriesInfo.FolderPath, seasonInfo.SeasonNumber, seasonInfo.FolderPath, cancellationToken);
                            if (item.EpisodeNumber is { } episodeNumber) {
                                await acquisitionHints.BindWantedChildBySortOrderAsync(
                                    EntityKind.Video, seasonInfo.FolderPath, episodeNumber, filePath, cancellationToken);
                            }
                        }

                        // A hint that names this exact file (a single-episode import) binds directly too.
                        await acquisitionHints.BindWantedEntityAsync(EntityKind.Video, filePath, cancellationToken);
                    }

                    batchItems.Add(item);
                }

                var entityIds = await videos.UpsertVideosBatchAsync(batchItems, cancellationToken);
                if (scanMetadata is not null) {
                    for (var i = 0; i < batchItems.Count && i < entityIds.Count; i++) {
                        if (batchItems[i].Metadata is not { } metadata) {
                            continue;
                        }

                        await scanMetadata.ApplyVideoSidecarMetadataAsync(
                            entityIds[i],
                            metadata,
                            Path.GetFileNameWithoutExtension(batchItems[i].FilePath),
                            batchItems[i].IsNsfw,
                            cancellationToken);
                    }
                }
                allEntityIds.AddRange(entityIds);

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
                    var label = Path.GetFileNameWithoutExtension(files[batchStart + i]);
                    var entityIdStr = entityId.ToString();

                    if (!needs.TryGetValue(entityId, out var entityNeeds)) continue;

                    // Queries first (probe feeds preview's technical metadata, then fingerprint),
                    // then the quick grid thumbnail, then subtitle sidecars, leaving the heavy
                    // preview/trickplay generation last so it never delays newer media.
                    if (settings.AutoGenerateMetadata && entityNeeds.NeedsProbe)
                        jobRequests.Add(EnqueueJobRequest.ForEntity(JobType.ProbeVideo, EntityKind.Video, entityIdStr, label, JobPriorities.Probe));

                    if (FingerprintGating.ShouldFingerprint(settings, entityNeeds))
                        jobRequests.Add(EnqueueJobRequest.ForEntity(JobType.FingerprintVideo, EntityKind.Video, entityIdStr, label, JobPriorities.Fingerprint));

                    if (entityNeeds.NeedsSubtitleExtraction)
                        jobRequests.Add(EnqueueJobRequest.ForEntity(JobType.ExtractSubtitles, EntityKind.Video, entityIdStr, label, JobPriorities.Sidecar));

                    var shouldGeneratePreview = settings.AutoGeneratePreview && entityNeeds.NeedsPreview;
                    var shouldGenerateTrickplay = settings.GenerateTrickplay && entityNeeds.NeedsTrickplay;
                    if (shouldGeneratePreview || shouldGenerateTrickplay)
                        jobRequests.Add(EnqueueJobRequest.ForEntity(JobType.GeneratePreview, EntityKind.Video, entityIdStr, label, JobPriorities.Preview));
                    else if (entityNeeds.NeedsGridThumbnail)
                        // Backfill the small grid variant for an existing cover (GeneratePreview, which
                        // also generates it, isn't running because a thumbnail already exists).
                        jobRequests.Add(EnqueueJobRequest.ForEntity(JobType.GenerateGridThumbnail, EntityKind.Video, entityIdStr, label, JobPriorities.Thumbnail));
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
        string.Equals(
            Path.TrimEndingDirectorySeparator(left),
            Path.TrimEndingDirectorySeparator(right),
            StringComparison.OrdinalIgnoreCase);

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
