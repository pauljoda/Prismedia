using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs;

/// <summary>
/// Maps a library root's per-kind scan flags to the scan job types that cover them and
/// enqueues those scans. This is the single source of truth shared by library creation,
/// file mutations, and the recurring scheduler so every entry point queues exactly the
/// kinds a root has enabled (for example a books-only root yields only
/// <see cref="JobType.ScanBook"/>).
/// </summary>
public static class LibraryScanJobs {
    /// <summary>Target entity kind recorded on root-scoped scan jobs for dashboard display and deduplication.</summary>
    public const string TargetKind = "library-root";

    /// <summary>
    /// Yields the scan job type that covers each media kind enabled on a library root.
    /// </summary>
    /// <param name="scanVideos">Whether the root scans for video files.</param>
    /// <param name="scanImages">Whether the root scans for image galleries.</param>
    /// <param name="scanAudio">Whether the root scans for audio tracks.</param>
    /// <param name="scanBooks">Whether the root scans for book and comic archives.</param>
    /// <returns>The scan job types to enqueue, one per enabled kind.</returns>
    public static IEnumerable<JobType> ScanJobTypesFor(
        bool scanVideos,
        bool scanImages,
        bool scanAudio,
        bool scanBooks) {
        if (scanVideos) yield return JobType.ScanLibrary;
        if (scanImages) yield return JobType.ScanGallery;
        if (scanAudio) yield return JobType.ScanAudio;
        if (scanBooks) yield return JobType.ScanBook;
    }

    /// <summary>
    /// Enqueues a scan job for each enabled media kind on a library root, skipping kinds
    /// that already have a queued or running scan for the same root.
    /// </summary>
    /// <param name="queue">Durable job queue.</param>
    /// <param name="rootId">Library root to scan.</param>
    /// <param name="label">Human-readable root label shown on the dashboard.</param>
    /// <param name="scanVideos">Whether the root scans for video files.</param>
    /// <param name="scanImages">Whether the root scans for image galleries.</param>
    /// <param name="scanAudio">Whether the root scans for audio tracks.</param>
    /// <param name="scanBooks">Whether the root scans for book and comic archives.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The number of scan jobs that were enqueued.</returns>
    public static async Task<int> QueueRootScansAsync(
        IJobQueueService queue,
        Guid rootId,
        string label,
        bool scanVideos,
        bool scanImages,
        bool scanAudio,
        bool scanBooks,
        CancellationToken cancellationToken) {
        var targetId = rootId.ToString();
        var queued = 0;

        foreach (var type in ScanJobTypesFor(scanVideos, scanImages, scanAudio, scanBooks)) {
            if (await queue.HasPendingAsync(type, targetId, cancellationToken)) {
                continue;
            }

            await queue.EnqueueAsync(
                new EnqueueJobRequest(
                    Type: type,
                    PayloadJson: new ScanRootPayload(rootId).ToJson(),
                    TargetEntityKind: TargetKind,
                    TargetEntityId: targetId,
                    TargetLabel: label),
                cancellationToken);
            queued++;
        }

        return queued;
    }
}
