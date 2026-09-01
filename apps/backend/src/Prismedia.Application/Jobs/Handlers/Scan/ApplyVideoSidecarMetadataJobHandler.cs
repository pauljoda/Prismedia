using Microsoft.Extensions.Logging;
using Prismedia.Application.Files;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Application.Jobs.Scanning;
using Prismedia.Contracts.Media;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers.Scan;

/// <summary>
/// Applies high-cardinality descriptive NFO/JSON metadata after a full video scan has made every
/// source visible and queued its playback-critical probe. The work remains durable and blocks Auto
/// Identify, but no longer extends the fresh-library readiness path.
/// </summary>
[JobDefinition(
    JobType.ApplyVideoSidecarMetadata,
    ResourceClass = JobResourceClass.StandardCpu,
    Importance = JobNodeImportance.BestEffort,
    BlocksAutoIdentify = true)]
public sealed class ApplyVideoSidecarMetadataJobHandler(
    ILogger<ApplyVideoSidecarMetadataJobHandler> logger,
    IFileDiscovery fileDiscovery,
    ILibraryScanRootPersistence roots,
    IVideoScanPersistence videos,
    IVideoSidecarMetadataReader sidecars,
    IScanMetadataPersistence metadataPersistence) : IJobHandler {
    private const int BatchSize = 50;

    /// <inheritdoc />
    public async Task HandleAsync(JobContext context, CancellationToken cancellationToken) {
        if (!Guid.TryParse(context.Job.TargetEntityId, out var rootId)) {
            throw new InvalidOperationException("Video sidecar metadata requires a library-root target.");
        }

        var timer = new JobPhaseTimer();
        LibraryRootData? root;
        using (timer.Phase("root-load")) {
            root = await roots.GetLibraryRootAsync(rootId, cancellationToken);
        }
        if (root is null || !root.Enabled || !root.ScanVideos) {
            await context.ReportProgressAsync(100, "Library root no longer requires video metadata", cancellationToken);
            return;
        }

        IReadOnlySet<string> excludedPaths;
        IReadOnlyList<string> files;
        using (timer.Phase("excluded-paths")) {
            excludedPaths = await roots.GetExcludedPathsForRootAsync(root.Id, cancellationToken);
        }
        using (timer.Phase("discover")) {
            files = await fileDiscovery.DiscoverFilesAsync(
                root.Path,
                MediaCategory.Video,
                root.Recursive,
                excludedPaths,
                cancellationToken);
        }

        var applied = 0;
        for (var batchStart = 0; batchStart < files.Count; batchStart += BatchSize) {
            var batchEnd = Math.Min(batchStart + BatchSize, files.Count);
            var batchPaths = files.Skip(batchStart).Take(batchEnd - batchStart).ToArray();
            IReadOnlyList<PlayableVideoSourceOwner> owners;
            using (timer.Phase("owner-load")) {
                owners = await videos.ListPlayableVideoSourceOwnersAsync(batchPaths, cancellationToken);
            }
            var ownersByPath = owners
                .GroupBy(owner => owner.FilePath, FileSystemPathComparison.Comparer)
                .ToDictionary(group => group.Key, group => group.ToArray(), FileSystemPathComparison.Comparer);
            var applyItems = new List<VideoSidecarApplyItem>();

            using (timer.Phase("sidecar-read")) {
                foreach (var filePath in batchPaths) {
                    var metadata = await sidecars.ReadAsync(filePath, cancellationToken);
                    if (metadata is null || !ownersByPath.TryGetValue(filePath, out var sourceOwners)) {
                        continue;
                    }

                    var fallbackTitle = Path.GetFileNameWithoutExtension(filePath);
                    applyItems.AddRange(sourceOwners.Select(owner => new VideoSidecarApplyItem(
                        owner.EntityId,
                        metadata,
                        fallbackTitle,
                        root.IsNsfw)));
                }
            }

            using (timer.Phase("metadata-apply")) {
                await metadataPersistence.ApplyVideoSidecarMetadataBatchAsync(applyItems, cancellationToken);
            }
            using (timer.Phase("batch-complete")) {
                await metadataPersistence.CompleteScanBatchAsync(cancellationToken);
            }
            applied += applyItems.Count;
            await context.ReportProgressAsync(
                files.Count == 0 ? 100 : batchEnd * 100 / files.Count,
                $"Applied sidecar metadata for {applied} source owners",
                cancellationToken);
        }

        if (files.Count == 0) {
            await context.ReportProgressAsync(100, "No video sidecars to apply", cancellationToken);
        }

        logger.LogInformation(
            "[METRICS] apply-video-sidecar-metadata {Label} — files={Files} owners={Owners} — {Timing}",
            root.Label,
            files.Count,
            applied,
            timer.Finish().ToLogString());
    }
}
