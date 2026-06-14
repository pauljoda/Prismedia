using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Prismedia.Application.Health;
using Prismedia.Application.Jobs;
using Prismedia.Application.Settings;
using Prismedia.Contracts.Settings;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class QueueWorkerTests {
    [Fact]
    public async Task QueueWorkerIncreasesParallelClaimsWhenConcurrencySettingChanges() {
        var queue = new RecordingJobQueueService(Enumerable.Range(0, 4).Select(_ => CreateJob()).ToArray());
        var settings = new MutableSettingsPersistence { BackgroundConcurrency = 1 };
        var handler = new BlockingJobHandler();

        var services = new ServiceCollection();
        services.AddSingleton<IJobQueueService>(queue);
        services.AddSingleton<ISettingsPersistence>(settings);
        services.AddScoped<SettingsService>();
        services.AddSingleton<IJobHandler>(handler);
        await using var provider = services.BuildServiceProvider();

        var worker = new QueueWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new WorkerRuntimeIdentity(),
            NullLogger<QueueWorker>.Instance,
            TimeSpan.FromMilliseconds(25));

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await worker.StartAsync(CancellationToken.None);
        try {
            await handler.WaitForMaxActiveAsync(1, timeout.Token);
            await Task.Delay(100, timeout.Token);
            Assert.Equal(1, handler.MaxActive);

            settings.BackgroundConcurrency = 3;

            await handler.WaitForMaxActiveAsync(3, timeout.Token);
            Assert.True(queue.Claims >= 3);
        } finally {
            handler.ReleaseAll();
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task QueueWorkerClaimsForegroundIdentifyJobThroughReservedLaneWhenSaturated() {
        var queue = new RecordingJobQueueService([CreateJob()]);
        var settings = new MutableSettingsPersistence { BackgroundConcurrency = 1 };
        var handler = new BlockingJobHandler();

        var services = new ServiceCollection();
        services.AddSingleton<IJobQueueService>(queue);
        services.AddSingleton<ISettingsPersistence>(settings);
        services.AddScoped<SettingsService>();
        services.AddSingleton<IJobHandler>(handler);
        await using var provider = services.BuildServiceProvider();

        var worker = new QueueWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new WorkerRuntimeIdentity(),
            NullLogger<QueueWorker>.Instance,
            TimeSpan.FromMilliseconds(25));

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await worker.StartAsync(CancellationToken.None);
        try {
            // The lone background slot fills with a long-running job.
            await handler.WaitForMaxActiveAsync(1, timeout.Token);

            // A queued background job must NOT enter the reserved lane...
            queue.Add(CreateJob(), priority: 0);
            await Task.Delay(150, timeout.Token);
            Assert.Equal(1, handler.MaxActive);

            // A broad priority-70 identify job without the foreground lane cannot steal it either.
            queue.Add(CreateJob(), JobPriorities.InteractiveIdentify);
            await Task.Delay(150, timeout.Token);
            Assert.Equal(1, handler.MaxActive);

            // ...but a direct manual identify job is claimed immediately through it.
            queue.Add(CreateJob(JobRunLane.ForegroundIdentify), JobPriorities.InteractiveIdentify);
            await handler.WaitForMaxActiveAsync(2, timeout.Token);
            Assert.Equal(2, handler.MaxActive);
        } finally {
            handler.ReleaseAll();
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task QueueWorkerStopsRunningHandlerWhenJobRunIsCancelled() {
        var job = CreateJob();
        var queue = new RecordingJobQueueService([job]);
        var settings = new MutableSettingsPersistence { BackgroundConcurrency = 1 };
        var handler = new CancellableJobHandler();

        var services = new ServiceCollection();
        services.AddSingleton<IJobQueueService>(queue);
        services.AddSingleton<ISettingsPersistence>(settings);
        services.AddScoped<SettingsService>();
        services.AddSingleton<IJobHandler>(handler);
        await using var provider = services.BuildServiceProvider();

        var worker = new QueueWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new WorkerRuntimeIdentity(),
            NullLogger<QueueWorker>.Instance,
            TimeSpan.FromMilliseconds(25));

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await worker.StartAsync(CancellationToken.None);
        try {
            await handler.WaitForStartedAsync(timeout.Token);
            queue.Cancel(job.Id);

            await handler.WaitForCancelledAsync(timeout.Token);

            Assert.DoesNotContain(job.Id, queue.CompletedJobIds);
        } finally {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task JobContextDoesNotEnqueueDownstreamWorkAfterCancellation() {
        var job = CreateJob();
        var queue = new RecordingJobQueueService([]);
        var context = new JobContext(job, queue);
        queue.Cancel(job.Id);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            context.EnqueueAsync(new EnqueueJobRequest(JobType.Noop), CancellationToken.None));

        Assert.Equal(0, queue.EnqueuedCount);
    }

    private static JobRunSnapshot CreateJob(JobRunLane? lane = null) =>
        new(
            Guid.NewGuid(),
            JobType.Noop,
            JobRunStatus.Running,
            0,
            null,
            "{}",
            null,
            null,
            "Worker test",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            null,
            Lane: lane);

    private sealed class BlockingJobHandler : IJobHandler {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _active;
        private int _maxActive;

        public JobType Type => JobType.Noop;

        public int MaxActive => Volatile.Read(ref _maxActive);

        public async Task HandleAsync(JobContext context, CancellationToken cancellationToken) {
            var active = Interlocked.Increment(ref _active);
            var observedMax = _maxActive;
            while (active > observedMax) {
                var original = Interlocked.CompareExchange(ref _maxActive, active, observedMax);
                if (original == observedMax) break;
                observedMax = original;
            }

            try {
                await _release.Task.WaitAsync(cancellationToken);
            } finally {
                Interlocked.Decrement(ref _active);
            }
        }

        public void ReleaseAll() => _release.TrySetResult();

        public async Task WaitForMaxActiveAsync(int expected, CancellationToken cancellationToken) {
            while (MaxActive < expected) {
                await Task.Delay(10, cancellationToken);
            }
        }
    }

    private sealed class CancellableJobHandler : IJobHandler {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public JobType Type => JobType.Noop;

        public async Task HandleAsync(JobContext context, CancellationToken cancellationToken) {
            _started.TrySetResult();
            try {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                _cancelled.TrySetResult();
                throw;
            }
        }

        public Task WaitForStartedAsync(CancellationToken cancellationToken) =>
            _started.Task.WaitAsync(cancellationToken);

        public Task WaitForCancelledAsync(CancellationToken cancellationToken) =>
            _cancelled.Task.WaitAsync(cancellationToken);
    }

    private sealed class MutableSettingsPersistence : ISettingsPersistence {
        public int BackgroundConcurrency { get; set; } = 1;

        public Task<IReadOnlyDictionary<string, string>> LoadSettingOverridesAsync(CancellationToken cancellationToken) {
            IReadOnlyDictionary<string, string> overrides = new Dictionary<string, string> {
                [AppSettingKeys.JobsBackgroundConcurrency] = JsonSerializer.Serialize(BackgroundConcurrency),
            };

            return Task.FromResult(overrides);
        }

        public Task SaveSettingOverrideAsync(string key, string valueJson, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task SaveSettingOverridesAsync(IReadOnlyDictionary<string, string> values, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task DeleteSettingOverrideAsync(string key, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<LibraryRoot>> ListLibraryRootsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<LibraryRoot>>([]);

        public Task<LibraryRoot?> GetLibraryRootAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<LibraryRoot?>(null);

        public Task<LibraryRoot> AddLibraryRootAsync(LibraryRoot state, CancellationToken cancellationToken) =>
            Task.FromResult(state);

        public Task<LibraryRoot> SaveLibraryRootAsync(LibraryRoot state, CancellationToken cancellationToken) =>
            Task.FromResult(state);

        public Task<bool> DeleteLibraryRootAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(false);
    }

    private sealed class RecordingJobQueueService : IJobQueueService {
        private readonly object _lock = new();
        private readonly List<(int Priority, JobRunSnapshot Job)> _jobs;
        private readonly HashSet<Guid> _cancelled = [];
        private readonly List<Guid> _completedJobIds = [];
        private int _claims;
        private int _enqueuedCount;

        public RecordingJobQueueService(IEnumerable<JobRunSnapshot> jobs) {
            _jobs = jobs.Select(job => (0, job)).ToList();
        }

        public int Claims => Volatile.Read(ref _claims);

        public int EnqueuedCount => Volatile.Read(ref _enqueuedCount);

        public IReadOnlyList<Guid> CompletedJobIds {
            get {
                lock (_lock) {
                    return _completedJobIds.ToArray();
                }
            }
        }

        public void Add(JobRunSnapshot job, int priority) {
            lock (_lock) {
                _jobs.Add((priority, job));
            }
        }

        public void Cancel(Guid jobId) {
            lock (_lock) {
                _cancelled.Add(jobId);
            }
        }

        public Task<JobRunSnapshot?> ClaimNextAsync(string workerId, CancellationToken cancellationToken, JobRunLane? lane = null) {
            Interlocked.Increment(ref _claims);
            lock (_lock) {
                var bestIndex = -1;
                var bestPriority = int.MinValue;
                for (var i = 0; i < _jobs.Count; i++) {
                    if (lane is not null && _jobs[i].Job.Lane != lane) {
                        continue;
                    }

                    if (_jobs[i].Priority > bestPriority) {
                        bestPriority = _jobs[i].Priority;
                        bestIndex = i;
                    }
                }

                if (bestIndex < 0) {
                    return Task.FromResult<JobRunSnapshot?>(null);
                }

                var job = _jobs[bestIndex].Job;
                _jobs.RemoveAt(bestIndex);
                return Task.FromResult<JobRunSnapshot?>(job);
            }
        }

        public Task CompleteAsync(Guid id, string? message, CancellationToken cancellationToken) {
            lock (_lock) {
                _completedJobIds.Add(id);
            }

            return Task.CompletedTask;
        }

        public Task<int> RecoverStaleRunningAsync(string currentWorkerId, TimeSpan staleAfter, CancellationToken cancellationToken) =>
            Task.FromResult(0);

        public Task<IReadOnlyList<JobRunSnapshot>> ListAsync(bool hideNsfw, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<JobRunSnapshot>>([]);

        public Task<JobRunSnapshot> EnqueueAsync(JobType type, CancellationToken cancellationToken) =>
            EnqueueAsync(new EnqueueJobRequest(type), cancellationToken);

        public Task<JobRunSnapshot> EnqueueAsync(EnqueueJobRequest request, CancellationToken cancellationToken) {
            Interlocked.Increment(ref _enqueuedCount);
            return Task.FromResult(CreateJob());
        }

        public Task<bool> HasPendingAsync(JobType type, string? targetEntityId, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<int> EnqueueBatchAsync(IReadOnlyList<EnqueueJobRequest> requests, CancellationToken cancellationToken) {
            Interlocked.Add(ref _enqueuedCount, requests.Count);
            return Task.FromResult(requests.Count);
        }

        public Task<int> CancelAsync(JobType? type, CancellationToken cancellationToken) =>
            Task.FromResult(0);

        public Task<bool> CancelRunAsync(Guid id, CancellationToken cancellationToken) {
            Cancel(id);
            return Task.FromResult(true);
        }

        public Task<bool> IsRunCancelledAsync(Guid id, CancellationToken cancellationToken) {
            lock (_lock) {
                return Task.FromResult(_cancelled.Contains(id));
            }
        }

        public Task<int> ClearFailuresAsync(JobType? type, CancellationToken cancellationToken) =>
            Task.FromResult(0);

        public Task UpdateProgressAsync(Guid id, int progress, string? message, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task FailAsync(Guid id, string message, TimeSpan retryDelay, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<JobQueueCount>> GetQueueCountsAsync(bool hideNsfw, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<JobQueueCount>>([]);

        public Task<int> PruneHistoryAsync(TimeSpan retention, CancellationToken cancellationToken) =>
            Task.FromResult(0);
    }
}
