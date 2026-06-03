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
    /// Priority for scan jobs. Scan creates the lightweight entities the UI shows, so it must
    /// out-rank the downstream asset jobs it spawns (probe 40, fingerprint 30, …) — otherwise a new
    /// scan stalls behind a large trickplay/preview backlog and newly added media takes a long time
    /// to appear in the library.
    /// </summary>
    public const int ScanPriority = 60;

    /// <summary>
    /// Enqueues one scan job per enabled media kind. Each scan job covers every enabled library root
    /// of that kind (the scan handler iterates them, skipping unchanged roots via the file snapshot),
    /// so scans are per-kind singletons: a kind that already has a scan queued or running is skipped
    /// and its duplicate dropped. The next scheduler tick or library change re-triggers it.
    /// </summary>
    /// <param name="queue">Durable job queue.</param>
    /// <param name="scanVideos">Whether to enqueue a video scan.</param>
    /// <param name="scanImages">Whether to enqueue an image/gallery scan.</param>
    /// <param name="scanAudio">Whether to enqueue an audio scan.</param>
    /// <param name="scanBooks">Whether to enqueue a book/comic scan.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The number of scan jobs that were newly enqueued.</returns>
    public static async Task<int> QueueScansForKindsAsync(
        IJobQueueService queue,
        bool scanVideos,
        bool scanImages,
        bool scanAudio,
        bool scanBooks,
        CancellationToken cancellationToken) {
        var queued = 0;

        foreach (var type in ScanJobTypesFor(scanVideos, scanImages, scanAudio, scanBooks)) {
            // Drop the duplicate when a scan of this kind is already in flight. The queue enforces the
            // same singleton, so this is also the accurate "did we add one" signal for callers.
            if (await queue.HasPendingAsync(type, null, cancellationToken)) {
                continue;
            }

            await queue.EnqueueAsync(new EnqueueJobRequest(Type: type, Priority: ScanPriority), cancellationToken);
            queued++;
        }

        return queued;
    }
}
