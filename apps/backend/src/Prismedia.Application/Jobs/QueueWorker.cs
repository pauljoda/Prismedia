using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Prismedia.Application.Settings;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs;

/// <summary>
/// Hosted application service that claims durable queue jobs and dispatches them
/// to typed handlers with configurable concurrency.
/// </summary>
public sealed class QueueWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<QueueWorker> logger) : BackgroundService {
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);
    private readonly string _workerId = $"{Environment.MachineName}-{Guid.NewGuid():N}";

    /// <summary>
    /// Runs the worker loop until the host shuts down. Supports concurrent job processing
    /// controlled by the BackgroundWorkerConcurrency library setting.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        var concurrency = await LoadConcurrencyAsync(stoppingToken);
        using var semaphore = new SemaphoreSlim(concurrency, concurrency);

        logger.LogInformation(
            "Prismedia .NET worker {WorkerId} started with concurrency {Concurrency}.",
            _workerId, concurrency);

        while (!stoppingToken.IsCancellationRequested) {
            await semaphore.WaitAsync(stoppingToken);

            JobRunSnapshot? job;
            try {
                await using var claimScope = scopeFactory.CreateAsyncScope();
                var queue = claimScope.ServiceProvider.GetRequiredService<IJobQueueService>();
                job = await queue.ClaimNextAsync(_workerId, stoppingToken);
            } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                semaphore.Release();
                throw;
            } catch (Exception ex) {
                semaphore.Release();
                logger.LogError(ex, "Failed to claim next job.");
                await Task.Delay(IdleDelay, stoppingToken);
                continue;
            }

            if (job is null) {
                semaphore.Release();
                await Task.Delay(IdleDelay, stoppingToken);
                continue;
            }

            _ = Task.Run(() => ProcessJobAsync(job, stoppingToken), stoppingToken)
                .ContinueWith(_ => semaphore.Release(), TaskScheduler.Default);
        }
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
        try {
            var context = new JobContext(job, queue);
            await handler.HandleAsync(context, stoppingToken);
            await queue.CompleteAsync(job.Id, "Completed", stoppingToken);

            var report = timer.Finish();
            logger.LogInformation(
                "[METRICS] {JobType} {Label} completed — {Timing}",
                job.Type.ToCode(), job.TargetLabel ?? job.Id.ToString(), report.ToLogString());
        } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
            logger.LogInformation("Job {JobId} cancelled due to worker shutdown.", job.Id);
        } catch (Exception ex) {
            var report = timer.Finish();
            logger.LogError(ex,
                "[METRICS] {JobType} {Label} FAILED after {Elapsed:F2}s — {Timing}",
                job.Type.ToCode(), job.TargetLabel ?? job.Id.ToString(),
                report.Total.TotalSeconds, report.ToLogString());
            await queue.FailAsync(job.Id, ex.Message, RetryDelay, stoppingToken);
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
