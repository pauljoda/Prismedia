using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Prismedia.Application.Health;
using Prismedia.Application.Settings;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs;

/// <summary>
/// Hosted application service that claims durable queue jobs and dispatches them
/// to typed handlers with configurable concurrency.
/// </summary>
public sealed class QueueWorker(
    IServiceScopeFactory scopeFactory,
    WorkerRuntimeIdentity workerIdentity,
    ILogger<QueueWorker> logger,
    TimeSpan? concurrencyRefreshInterval = null) : BackgroundService {
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Extra worker slots reserved for direct foreground identify jobs. Priority only orders claims,
    /// so a long-running scan occupying every regular slot would still make a manual identify wait
    /// for it to finish; the lane lets direct manual identify work start immediately instead.
    /// </summary>
    private const int ForegroundLaneSlots = 1;
    private static readonly TimeSpan StaleLeaseTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan StaleLeaseRecoveryInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan JobCancellationPollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan DefaultConcurrencyRefreshInterval = TimeSpan.FromSeconds(15);
    private readonly TimeSpan _concurrencyRefreshInterval =
        concurrencyRefreshInterval ?? DefaultConcurrencyRefreshInterval;
    private readonly string _workerId = workerIdentity.WorkerId;

    /// <summary>
    /// Runs the worker loop until the host shuts down. Supports concurrent job processing
    /// controlled by the BackgroundWorkerConcurrency library setting.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        var concurrency = await LoadConcurrencyAsync(stoppingToken);
        var runningJobs = new HashSet<Task>();
        var nextConcurrencyRefreshAt = DateTimeOffset.UtcNow.Add(_concurrencyRefreshInterval);

        logger.LogInformation(
            "Prismedia .NET worker {WorkerId} started with concurrency {Concurrency}.",
            _workerId, concurrency);

        var nextRecoveryAt = DateTimeOffset.MinValue;
        while (!stoppingToken.IsCancellationRequested) {
            RemoveCompletedJobs(runningJobs);

            var now = DateTimeOffset.UtcNow;
            if (now >= nextConcurrencyRefreshAt) {
                var nextConcurrency = await LoadConcurrencyAsync(stoppingToken);
                if (nextConcurrency != concurrency) {
                    logger.LogInformation(
                        "Prismedia .NET worker {WorkerId} concurrency changed from {PreviousConcurrency} to {Concurrency}.",
                        _workerId, concurrency, nextConcurrency);
                    concurrency = nextConcurrency;
                }

                nextConcurrencyRefreshAt = now.Add(_concurrencyRefreshInterval);
            }

            if (runningJobs.Count >= concurrency + ForegroundLaneSlots) {
                await WaitForCapacityOrRefreshAsync(runningJobs, nextConcurrencyRefreshAt, stoppingToken);
                continue;
            }

            // With every regular slot busy, only the reserved foreground lane remains: claim
            // exclusively direct manual identify work so bulk/background jobs cannot fill it.
            var foregroundIdentifyOnly = runningJobs.Count >= concurrency;

            JobRunSnapshot? job;
            try {
                await using var claimScope = scopeFactory.CreateAsyncScope();
                var queue = claimScope.ServiceProvider.GetRequiredService<IJobQueueService>();
                now = DateTimeOffset.UtcNow;
                if (now >= nextRecoveryAt) {
                    var recovered = await queue.RecoverStaleRunningAsync(_workerId, StaleLeaseTimeout, stoppingToken);
                    if (recovered > 0) {
                        logger.LogWarning("Recovered {JobCount} stale running job leases.", recovered);
                    }

                    nextRecoveryAt = now.Add(StaleLeaseRecoveryInterval);
                }

                job = await queue.ClaimNextAsync(
                    _workerId,
                    stoppingToken,
                    foregroundIdentifyOnly ? JobRunLane.ForegroundIdentify : null);
            } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                throw;
            } catch (Exception ex) {
                logger.LogError(ex, "Failed to claim next job.");
                await WaitForCapacityOrRefreshAsync(runningJobs, nextConcurrencyRefreshAt, stoppingToken);
                continue;
            }

            if (job is null) {
                await WaitForCapacityOrRefreshAsync(runningJobs, nextConcurrencyRefreshAt, stoppingToken);
                continue;
            }

            runningJobs.Add(Task.Run(() => ProcessJobAsync(job, stoppingToken), stoppingToken));
        }

        if (runningJobs.Count > 0) {
            await Task.WhenAll(runningJobs);
        }
    }

    private static void RemoveCompletedJobs(HashSet<Task> runningJobs) {
        runningJobs.RemoveWhere(job => job.IsCompleted);
    }

    private static async Task WaitForCapacityOrRefreshAsync(
        HashSet<Task> runningJobs,
        DateTimeOffset nextConcurrencyRefreshAt,
        CancellationToken stoppingToken) {
        var delay = NextCheckDelay(nextConcurrencyRefreshAt);
        try {
            if (runningJobs.Count == 0) {
                await Task.Delay(delay, stoppingToken);
                return;
            }

            var jobCompletion = Task.WhenAny(runningJobs);
            var refreshDelay = Task.Delay(delay, stoppingToken);
            var completed = await Task.WhenAny(jobCompletion, refreshDelay);
            if (completed == jobCompletion) {
                var finishedJob = await jobCompletion;
                runningJobs.Remove(finishedJob);
            }
        } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
            // Host is shutting down; return so the worker loop exits cleanly.
        }
    }

    private static TimeSpan NextCheckDelay(DateTimeOffset nextConcurrencyRefreshAt) {
        var untilRefresh = nextConcurrencyRefreshAt - DateTimeOffset.UtcNow;
        if (untilRefresh <= TimeSpan.Zero) return TimeSpan.Zero;
        return untilRefresh < IdleDelay ? untilRefresh : IdleDelay;
    }

    private async Task ProcessJobAsync(JobRunSnapshot job, CancellationToken stoppingToken) {
        await using var scope = scopeFactory.CreateAsyncScope();
        var queue = scope.ServiceProvider.GetRequiredService<IJobQueueService>();
        var handlers = scope.ServiceProvider.GetServices<IJobHandler>();
        var handler = handlers.FirstOrDefault(h => h.Type == job.Type);

        if (handler is null) {
            logger.LogWarning("No handler registered for job type '{JobType}'.", job.Type.ToCode());
            await queue.FailAsync(
                job.Id,
                $"No handler registered for job type '{job.Type.ToCode()}'.",
                RetryDelay,
                stoppingToken);
            return;
        }

        var timer = new JobPhaseTimer();
        using var jobCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        using var monitorCancellation = new CancellationTokenSource();
        var cancellationMonitor = MonitorJobCancellationAsync(job.Id, jobCancellation, monitorCancellation.Token);
        try {
            var context = new JobContext(job, queue);
            await handler.HandleAsync(context, jobCancellation.Token);
            jobCancellation.Token.ThrowIfCancellationRequested();
            await queue.CompleteAsync(job.Id, "Completed", stoppingToken);

            var report = timer.Finish();
            logger.LogInformation(
                "[METRICS] {JobType} {Label} completed — {Timing}",
                job.Type.ToCode(), job.TargetLabel ?? job.Id.ToString(), report.ToLogString());
        } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
            logger.LogInformation("Job {JobId} cancelled due to worker shutdown.", job.Id);
        } catch (OperationCanceledException) when (jobCancellation.IsCancellationRequested) {
            logger.LogInformation("Job {JobId} cancelled by operator request.", job.Id);
        } catch (JobRetryLaterException ex) {
            var report = timer.Finish();
            logger.LogDebug(
                "[METRICS] {JobType} {Label} deferred after {Elapsed:F2}s — {Timing}: {Message}",
                job.Type.ToCode(), job.TargetLabel ?? job.Id.ToString(),
                report.Total.TotalSeconds, report.ToLogString(), ex.Message);
            await queue.DeferAsync(job.Id, ex.Message, ex.RetryDelay, stoppingToken);
        } catch (Exception ex) {
            var report = timer.Finish();
            logger.LogError(ex,
                "[METRICS] {JobType} {Label} FAILED after {Elapsed:F2}s — {Timing}",
                job.Type.ToCode(), job.TargetLabel ?? job.Id.ToString(),
                report.Total.TotalSeconds, report.ToLogString());
            await queue.FailAsync(job.Id, ex.Message, RetryDelay, stoppingToken);
        } finally {
            await StopJobCancellationMonitorAsync(monitorCancellation, cancellationMonitor);
        }
    }

    private async Task MonitorJobCancellationAsync(
        Guid jobId,
        CancellationTokenSource jobCancellation,
        CancellationToken cancellationToken) {
        while (!cancellationToken.IsCancellationRequested && !jobCancellation.IsCancellationRequested) {
            try {
                await using var scope = scopeFactory.CreateAsyncScope();
                var queue = scope.ServiceProvider.GetRequiredService<IJobQueueService>();
                if (await queue.IsRunCancelledAsync(jobId, cancellationToken)) {
                    jobCancellation.Cancel();
                    return;
                }

                await Task.Delay(JobCancellationPollInterval, cancellationToken);
            } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                return;
            } catch (Exception ex) {
                logger.LogWarning(ex, "Could not check cancellation state for job {JobId}.", jobId);
                await Task.Delay(JobCancellationPollInterval, cancellationToken);
            }
        }
    }

    private static async Task StopJobCancellationMonitorAsync(
        CancellationTokenSource monitorCancellation,
        Task cancellationMonitor) {
        await monitorCancellation.CancelAsync();
        try {
            await cancellationMonitor;
        } catch (OperationCanceledException) {
            // Expected when the job finished before the next cancellation poll.
        }
    }

    private async Task<int> LoadConcurrencyAsync(CancellationToken cancellationToken) {
        try {
            await using var scope = scopeFactory.CreateAsyncScope();
            var settings = scope.ServiceProvider.GetService<SettingsService>();
            if (settings is not null) {
                var worker = await settings.GetWorkerSettingsAsync(cancellationToken);
                return Math.Max(1, worker.BackgroundConcurrency);
            }
        } catch (Exception ex) {
            logger.LogWarning(ex, "Could not load worker concurrency setting, defaulting to 1.");
        }

        return 1;
    }
}
