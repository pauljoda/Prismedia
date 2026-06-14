using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs;

/// <summary>
/// Application port for durable background job queue operations.
/// </summary>
public interface IJobQueueService {
    /// <summary>
    /// Lists active and recent background job runs for operational surfaces.
    /// </summary>
    Task<IReadOnlyList<JobRunSnapshot>> ListAsync(bool hideNsfw, CancellationToken cancellationToken);

    /// <summary>
    /// Enqueues a new background job run with default settings.
    /// </summary>
    Task<JobRunSnapshot> EnqueueAsync(JobType type, CancellationToken cancellationToken);

    /// <summary>
    /// Enqueues a new background job run with full target and payload control.
    /// </summary>
    Task<JobRunSnapshot> EnqueueAsync(EnqueueJobRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Checks whether a queued or running job already exists for the given type and optional target.
    /// Used to prevent duplicate work.
    /// </summary>
    Task<bool> HasPendingAsync(JobType type, string? targetEntityId, CancellationToken cancellationToken);

    /// <summary>
    /// Enqueues multiple jobs in a single database round-trip, skipping any that
    /// already have a pending run for the same type and target entity.
    /// </summary>
    Task<int> EnqueueBatchAsync(IReadOnlyList<EnqueueJobRequest> requests, CancellationToken cancellationToken);

    /// <summary>
    /// Cancels queued or running jobs, optionally scoped to one typed operation.
    /// </summary>
    Task<int> CancelAsync(JobType? type, CancellationToken cancellationToken);

    /// <summary>
    /// Cancels one queued or running job run by identifier.
    /// </summary>
    Task<bool> CancelRunAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Checks whether a claimed job run has been cancelled by an operator while a handler is still running.
    /// </summary>
    Task<bool> IsRunCancelledAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(false);

    /// <summary>
    /// Clears failed jobs from the active failure list, optionally scoped to one typed operation.
    /// </summary>
    Task<int> ClearFailuresAsync(JobType? type, CancellationToken cancellationToken);

    /// <summary>
    /// Claims the next available queued job for one worker using atomic row locking. When
    /// <paramref name="lane"/> is set, only jobs explicitly assigned to that queue lane are eligible.
    /// </summary>
    Task<JobRunSnapshot?> ClaimNextAsync(string workerId, CancellationToken cancellationToken, JobRunLane? lane = null);

    /// <summary>
    /// Requeues running jobs whose worker lease is stale and not owned by the current worker process.
    /// </summary>
    Task<int> RecoverStaleRunningAsync(string currentWorkerId, TimeSpan staleAfter, CancellationToken cancellationToken);

    /// <summary>
    /// Updates progress on a running job for dashboard display.
    /// </summary>
    Task UpdateProgressAsync(Guid id, int progress, string? message, CancellationToken cancellationToken);

    /// <summary>
    /// Marks a running job complete.
    /// </summary>
    Task CompleteAsync(Guid id, string? message, CancellationToken cancellationToken);

    /// <summary>
    /// Marks a running job failed and schedules a retry when attempts remain.
    /// </summary>
    Task FailAsync(Guid id, string message, TimeSpan retryDelay, CancellationToken cancellationToken);

    /// <summary>
    /// Returns a claimed job to the queue without consuming the claim as a failed attempt.
    /// Use for local capacity throttles such as provider slots, not for work that actually ran.
    /// </summary>
    Task DeferAsync(Guid id, string message, TimeSpan retryDelay, CancellationToken cancellationToken) =>
        FailAsync(id, message, retryDelay, cancellationToken);

    /// <summary>
    /// Returns aggregate counts of job runs grouped by type code and status code,
    /// so the dashboard can display accurate totals without fetching all rows.
    /// </summary>
    Task<IReadOnlyList<JobQueueCount>> GetQueueCountsAsync(bool hideNsfw, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes completed and cancelled job runs older than the retention period.
    /// </summary>
    Task<int> PruneHistoryAsync(TimeSpan retention, CancellationToken cancellationToken);
}
