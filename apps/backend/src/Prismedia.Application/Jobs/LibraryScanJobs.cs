using Prismedia.Domain.Entities;
using Prismedia.Application.Jobs.Ports;

namespace Prismedia.Application.Jobs;

/// <summary>
/// Maps a library root's per-kind scan flags to the scan job types that cover them and
/// enqueues those scans. This is the single source of truth shared by library creation,
/// file mutations, and the recurring scheduler so every root-scoped entry point queues exactly the
/// kinds a root has enabled. The existing book-library setting enables both the prose-book and
/// serialized-comic scanners while keeping their snapshots and materialization contracts separate.
/// </summary>
public static class LibraryScanJobs {
    /// <summary>Target entity kind recorded on root-scoped scan jobs for dashboard display and deduplication.</summary>
    public const string TargetKind = JobTargetKinds.LibraryRoot;

    /// <summary>
    /// Yields the scan job type that covers each media kind enabled on a library root.
    /// </summary>
    /// <param name="selection">The media families included in the scan.</param>
    /// <returns>The scan job types to enqueue, one per enabled kind.</returns>
    public static IEnumerable<JobType> ScanJobTypesFor(LibraryScanSelection selection) {
        if (selection.Videos) yield return JobType.ScanLibrary;
        if (selection.Images) yield return JobType.ScanGallery;
        if (selection.Audio) yield return JobType.ScanAudio;
        if (selection.Books) yield return JobType.ScanBook;
        if (selection.Comics) yield return JobType.ScanComic;
    }

    /// <summary>
    /// Enqueues the selected scan kinds for one exact root. Root-targeted scans deduplicate per kind
    /// and share a single durable resource so large roots and media families reconcile one at a time.
    /// </summary>
    public static async Task<int> QueueScansForRootAsync(
        IJobQueueService queue,
        Guid rootId,
        string rootLabel,
        LibraryScanSelection selection,
        CancellationToken cancellationToken) {
        return await QueueRootJobsAsync(
            queue,
            rootId,
            rootLabel,
            selection,
            changesOnly: false,
            cancellationToken);
    }

    /// <summary>
    /// Enqueues deep integrity scans for one root: a full reconciliation plus the library-wide
    /// orphan and outside-root cleanups that ordinary scans skip. Scheduled on the integrity
    /// cadence rather than the routine scan interval.
    /// </summary>
    public static async Task<int> QueueDeepScansForRootAsync(
        IJobQueueService queue,
        Guid rootId,
        string rootLabel,
        LibraryScanSelection selection,
        CancellationToken cancellationToken) {
        return await QueueRootJobsAsync(
            queue,
            rootId,
            rootLabel,
            selection,
            changesOnly: false,
            cancellationToken,
            deep: true);
    }

    /// <summary>
    /// Durably records each observed filesystem path for the scan kinds whose media families can
    /// own it (see <see cref="MediaScanKindRouter"/>), then ensures one change-only job per routed
    /// kind is queued. A touched video therefore queues only the video scan instead of one job per
    /// enabled family. New observations remain in the ledger even when a matching job is already
    /// running; the filesystem monitor will queue the continuation after it completes.
    /// </summary>
    public static async Task<int> QueueChangedPathsForRootAsync(
        IJobQueueService queue,
        ILibraryFileChangeIntake changes,
        Guid rootId,
        string rootLabel,
        LibraryScanSelection selection,
        IReadOnlyCollection<string> absolutePaths,
        CancellationToken cancellationToken) {
        if (absolutePaths.Count == 0) {
            return 0;
        }

        var routed = MediaScanKindRouter.Route(selection, absolutePaths);
        if (routed.Count == 0) {
            return 0;
        }

        foreach (var (type, paths) in routed) {
            await changes.RecordAsync(
                rootId,
                type.ToCode(),
                paths,
                cancellationToken);
        }
        return await QueueRootJobsAsync(
            queue,
            rootId,
            rootLabel,
            MediaScanKindRouter.SelectionFor(routed),
            changesOnly: true,
            cancellationToken);
    }

    /// <summary>Ensures change-only jobs exist for media kinds that still have durable path intents.</summary>
    public static async Task<int> QueuePendingChangesForRootAsync(
        IJobQueueService queue,
        ILibraryFileChangeIntake changes,
        Guid rootId,
        string rootLabel,
        LibraryScanSelection selection,
        CancellationToken cancellationToken) {
        var pendingSelection = new LibraryScanSelection(
            Videos: selection.Videos && await changes.HasPendingAsync(rootId, JobType.ScanLibrary.ToCode(), cancellationToken),
            Images: selection.Images && await changes.HasPendingAsync(rootId, JobType.ScanGallery.ToCode(), cancellationToken),
            Audio: selection.Audio && await changes.HasPendingAsync(rootId, JobType.ScanAudio.ToCode(), cancellationToken),
            Books: selection.Books && await changes.HasPendingAsync(rootId, JobType.ScanBook.ToCode(), cancellationToken),
            Comics: selection.Comics && await changes.HasPendingAsync(rootId, JobType.ScanComic.ToCode(), cancellationToken));
        if (pendingSelection.IsEmpty) {
            return 0;
        }
        return await QueueRootJobsAsync(
            queue,
            rootId,
            rootLabel,
            pendingSelection,
            changesOnly: true,
            cancellationToken);
    }

    private static async Task<int> QueueRootJobsAsync(
        IJobQueueService queue,
        Guid rootId,
        string rootLabel,
        LibraryScanSelection selection,
        bool changesOnly,
        CancellationToken cancellationToken,
        bool deep = false) {
        await queue.DeclareResourceAsync(
            JobResourceKeys.LibraryScan,
            maxConcurrency: 1,
            minimumStartInterval: TimeSpan.Zero,
            cancellationToken);

        var targetId = rootId.ToString();
        var payloadJson = new ScanRootPayload(rootId, changesOnly, deep).ToJson();
        var queued = 0;
        foreach (var type in ScanJobTypesFor(selection)) {
            if (await queue.HasPendingAsync(type, targetId, cancellationToken)) {
                continue;
            }

            await queue.EnqueueAsync(new EnqueueJobRequest(
                type,
                payloadJson,
                TargetKind,
                targetId,
                rootLabel,
                ResourceKey: JobResourceKeys.LibraryScan), cancellationToken);
            queued++;
        }

        return queued;
    }

    /// <summary>
    /// Enqueues legacy aggregate scan jobs, one per enabled media kind. New callers with a known root
    /// should use <see cref="QueueScansForRootAsync"/> so unrelated roots are never traversed.
    /// </summary>
    /// <param name="queue">Durable job queue.</param>
    /// <param name="selection">The media families whose scans should be enqueued.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The number of scan jobs that were newly enqueued.</returns>
    public static async Task<int> QueueScansForKindsAsync(
        IJobQueueService queue,
        LibraryScanSelection selection,
        CancellationToken cancellationToken) {
        var queued = 0;

        foreach (var type in ScanJobTypesFor(selection)) {
            // Drop the duplicate when a scan of this kind is already in flight. The queue enforces the
            // same singleton, so this is also the accurate "did we add one" signal for callers.
            if (await queue.HasPendingAsync(type, null, cancellationToken)) {
                continue;
            }

            await queue.EnqueueAsync(new EnqueueJobRequest(Type: type), cancellationToken);
            queued++;
        }

        return queued;
    }
}
