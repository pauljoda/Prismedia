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

    /// <summary>Appends a child node that inherits the parent's graph, origin, and logical lane.</summary>
    Task<JobRunSnapshot> EnqueueChildAsync(
        JobRunSnapshot parent,
        EnqueueJobRequest request,
        CancellationToken cancellationToken) =>
        EnqueueAsync(request, cancellationToken);

    /// <summary>
    /// Appends an explicitly-shaped node to the parent's graph. Implementations must inherit the graph,
    /// lane, origin, initiating user, and top-level target from <paramref name="parent"/> and must reject
    /// dependencies outside that graph.
    /// </summary>
    Task<JobRunSnapshot> AppendChildGraphNodeAsync(
        JobRunSnapshot parent,
        GraphJobNodeRequest request,
        CancellationToken cancellationToken) =>
        EnqueueChildAsync(parent, request.Job, cancellationToken);

    /// <summary>Appends multiple child nodes to the parent's graph.</summary>
    Task<int> EnqueueChildBatchAsync(
        JobRunSnapshot parent,
        IReadOnlyList<EnqueueJobRequest> requests,
        CancellationToken cancellationToken) =>
        EnqueueBatchAsync(requests, cancellationToken);

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

    /// <summary>Compatibility entry point that claims the next background graph node.</summary>
    Task<JobRunSnapshot?> ClaimNextAsync(string workerId, CancellationToken cancellationToken) =>
        Task.FromResult<JobRunSnapshot?>(null);

    /// <summary>
    /// Claims the next dependency-ready node from a durable graph of the requested origin. Interactive
    /// graphs permit one running node per graph; background graphs use the shared configured pool.
    /// </summary>
    Task<JobRunSnapshot?> ClaimNextGraphNodeAsync(
        string workerId,
        JobGraphOrigin origin,
        CancellationToken cancellationToken,
        IReadOnlyCollection<JobResourceClass>? allowedResourceClasses = null) =>
        ClaimNextAsync(workerId, cancellationToken);

    /// <summary>Creates or updates a durable shared resource policy used during graph-node claims.</summary>
    Task DeclareResourceAsync(
        string resourceKey,
        int maxConcurrency,
        TimeSpan minimumStartInterval,
        CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Renews the running-node heartbeat and any durable resource lease owned by it.</summary>
    Task HeartbeatAsync(
        Guid id,
        string workerId,
        CancellationToken cancellationToken) => Task.CompletedTask;

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
