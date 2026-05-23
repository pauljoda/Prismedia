using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs;

/// <summary>
/// Execution context passed to job handlers providing the claimed job snapshot,
/// progress reporting, and the ability to enqueue downstream jobs.
/// </summary>
public sealed class JobContext {
    private readonly IJobQueueService _queue;

    public JobContext(JobRunSnapshot job, IJobQueueService queue) {
        Job = job;
        _queue = queue;
    }

    /// <summary>
    /// The claimed job run this handler is processing.
    /// </summary>
    public JobRunSnapshot Job { get; }

    /// <summary>
    /// Reports progress on the running job for dashboard display.
    /// </summary>
    public Task ReportProgressAsync(int progress, string? message = null, CancellationToken cancellationToken = default) =>
        _queue.UpdateProgressAsync(Job.Id, progress, message, cancellationToken);

    /// <summary>
    /// Enqueues a downstream job, skipping if a pending job already exists for the same type and target.
    /// Returns the snapshot when enqueued, or null when deduplicated away.
    /// </summary>
    public async Task<JobRunSnapshot?> EnqueueIfNeededAsync(EnqueueJobRequest request, CancellationToken cancellationToken = default) {
        if (await _queue.HasPendingAsync(request.Type, request.TargetEntityId, cancellationToken)) {
            return null;
        }

        return await _queue.EnqueueAsync(request, cancellationToken);
    }

    /// <summary>
    /// Enqueues a downstream job unconditionally.
    /// </summary>
    public Task<JobRunSnapshot> EnqueueAsync(EnqueueJobRequest request, CancellationToken cancellationToken = default) =>
        _queue.EnqueueAsync(request, cancellationToken);

    /// <summary>
    /// Enqueues a batch of downstream jobs in a single round-trip, deduplicating against pending jobs.
    /// Returns the number of jobs actually enqueued.
    /// </summary>
    public Task<int> EnqueueBatchAsync(IReadOnlyList<EnqueueJobRequest> requests, CancellationToken cancellationToken = default) =>
        _queue.EnqueueBatchAsync(requests, cancellationToken);
}
