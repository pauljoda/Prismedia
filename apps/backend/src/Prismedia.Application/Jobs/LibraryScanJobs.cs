using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs;

/// <summary>
/// Maps a library root's per-kind scan flags to the scan job types that cover them and
/// enqueues those scans. This is the single source of truth shared by library creation,
/// file mutations, and the recurring scheduler so every root-scoped entry point queues exactly the
/// kinds a root has enabled (for example a books-only root yields only
/// <see cref="JobType.ScanBook"/>).
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
        await queue.DeclareResourceAsync(
            JobResourceKeys.LibraryScan,
            maxConcurrency: 1,
            minimumStartInterval: TimeSpan.Zero,
            cancellationToken);

        var targetId = rootId.ToString();
        var payloadJson = new ScanRootPayload(rootId).ToJson();
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
