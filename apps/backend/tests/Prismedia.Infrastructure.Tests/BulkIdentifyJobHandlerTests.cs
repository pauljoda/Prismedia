using Microsoft.Extensions.Logging.Abstractions;
using Prismedia.Application.Jobs;
using Prismedia.Application.Jobs.Handlers;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class BulkIdentifyJobHandlerTests {
    [Fact]
    public async Task HandleAsyncRequeuesImmediatelyWhenIdentifySlotIsBusy() {
        var provider = new BlockingBulkIdentifyProvider();
        var handler = new BulkIdentifyJobHandler(
            provider,
            new AutoIdentifyConcurrencyGate(),
            NullLogger<BulkIdentifyJobHandler>.Instance);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var first = handler.HandleAsync(new JobContext(CreateJob([Guid.NewGuid()]), new NoopJobQueue()), timeout.Token);
        await provider.WaitForStartedCallsAsync(1, timeout.Token);

        var busy = await Assert.ThrowsAsync<JobRetryLaterException>(() =>
            handler.HandleAsync(new JobContext(CreateJob([Guid.NewGuid()]), new NoopJobQueue()), timeout.Token));

        Assert.Equal("Bulk identify provider slot busy.", busy.Message);
        Assert.Equal(TimeSpan.FromSeconds(5), busy.RetryDelay);
        Assert.Equal(1, provider.MaxActive);
        Assert.Equal(1, provider.StartedCalls);

        provider.ReleaseNext();
        await first;
    }

    [Fact]
    public async Task HandleAsyncRequeuesWhenEntitySearchExceedsTimeout() {
        var provider = new BlockingBulkIdentifyProvider();
        var handler = new BulkIdentifyJobHandler(
            provider,
            new AutoIdentifyConcurrencyGate(),
            NullLogger<BulkIdentifyJobHandler>.Instance,
            TimeSpan.FromMilliseconds(25));

        var retry = await Assert.ThrowsAsync<JobRetryLaterException>(() =>
            handler.HandleAsync(new JobContext(CreateJob([Guid.NewGuid()]), new NoopJobQueue()), CancellationToken.None));

        Assert.Equal("Bulk identify timed out after 0 seconds.", retry.Message);
        Assert.Equal(TimeSpan.FromMinutes(1), retry.RetryDelay);
        Assert.Equal(1, provider.StartedCalls);
    }

    [Fact]
    public async Task HandleAsyncResumesPastEntitiesResolvedByEarlierAttempts() {
        var provider = new BlockingBulkIdentifyProvider();
        var alreadyDone = Guid.NewGuid();
        var pending = Guid.NewGuid();
        provider.ResolvedEntityIds.Add(alreadyDone);
        var handler = new BulkIdentifyJobHandler(
            provider,
            new AutoIdentifyConcurrencyGate(),
            NullLogger<BulkIdentifyJobHandler>.Instance);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var run = handler.HandleAsync(new JobContext(CreateJob([alreadyDone, pending]), new NoopJobQueue()), timeout.Token);
        await provider.WaitForStartedCallsAsync(1, timeout.Token);
        provider.ReleaseNext();
        await run;

        Assert.Equal([pending], provider.SearchedEntityIds);
    }

    private static JobRunSnapshot CreateJob(IReadOnlyList<Guid> entityIds) =>
        new(
            Guid.NewGuid(),
            JobType.BulkIdentify,
            JobRunStatus.Running,
            Progress: 0,
            Message: null,
            PayloadJson: new BulkIdentifyPayload(entityIds, "tmdb", null, false).ToJson(),
            TargetEntityKind: null,
            TargetEntityId: null,
            TargetLabel: "Bulk identify test",
            CreatedAt: DateTimeOffset.UtcNow,
            StartedAt: DateTimeOffset.UtcNow,
            FinishedAt: null);

    private sealed class BlockingBulkIdentifyProvider : IBulkIdentifyProvider {
        private readonly SemaphoreSlim _release = new(0);
        private int _active;
        private int _maxActive;
        private int _startedCalls;

        public int MaxActive => Volatile.Read(ref _maxActive);
        public int StartedCalls => Volatile.Read(ref _startedCalls);

        /// <summary>Entities a prior attempt already resolved; the handler should skip these.</summary>
        public HashSet<Guid> ResolvedEntityIds { get; } = [];

        public List<Guid> SearchedEntityIds { get; } = [];

        public async Task SearchAndQueueAsync(
            Guid entityId,
            string provider,
            IdentifyQuery? query,
            bool hideNsfw,
            CancellationToken cancellationToken) {
            var active = Interlocked.Increment(ref _active);
            TrackMaxActive(active);
            Interlocked.Increment(ref _startedCalls);
            lock (SearchedEntityIds) {
                SearchedEntityIds.Add(entityId);
            }

            try {
                await _release.WaitAsync(cancellationToken);
            } finally {
                Interlocked.Decrement(ref _active);
            }
        }

        public Task<bool> HasResultSinceAsync(
            Guid entityId,
            string provider,
            DateTimeOffset since,
            CancellationToken cancellationToken) =>
            Task.FromResult(ResolvedEntityIds.Contains(entityId));

        public void ReleaseNext() => _release.Release();

        public async Task WaitForStartedCallsAsync(int expected, CancellationToken cancellationToken) {
            while (StartedCalls < expected) {
                await Task.Delay(10, cancellationToken);
            }
        }

        private void TrackMaxActive(int active) {
            var observedMax = _maxActive;
            while (active > observedMax) {
                var original = Interlocked.CompareExchange(ref _maxActive, active, observedMax);
                if (original == observedMax) return;
                observedMax = original;
            }
        }
    }

    private sealed class NoopJobQueue : IJobQueueService {
        public Task<IReadOnlyList<JobRunSnapshot>> ListAsync(bool hideNsfw, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<JobRunSnapshot>>([]);

        public Task<JobRunSnapshot> EnqueueAsync(JobType type, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<JobRunSnapshot> EnqueueAsync(EnqueueJobRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> HasPendingAsync(JobType type, string? targetEntityId, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<int> EnqueueBatchAsync(IReadOnlyList<EnqueueJobRequest> requests, CancellationToken cancellationToken) =>
            Task.FromResult(0);

        public Task<int> CancelAsync(JobType? type, CancellationToken cancellationToken) =>
            Task.FromResult(0);

        public Task<bool> CancelRunAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<int> ClearFailuresAsync(JobType? type, CancellationToken cancellationToken) =>
            Task.FromResult(0);

        public Task<JobRunSnapshot?> ClaimNextAsync(string workerId, CancellationToken cancellationToken, int? minPriority = null) =>
            Task.FromResult<JobRunSnapshot?>(null);

        public Task<int> RecoverStaleRunningAsync(string currentWorkerId, TimeSpan staleAfter, CancellationToken cancellationToken) =>
            Task.FromResult(0);

        public Task UpdateProgressAsync(Guid id, int progress, string? message, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task CompleteAsync(Guid id, string? message, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task FailAsync(Guid id, string message, TimeSpan retryDelay, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<JobQueueCount>> GetQueueCountsAsync(bool hideNsfw, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<JobQueueCount>>([]);

        public Task<int> PruneHistoryAsync(TimeSpan retention, CancellationToken cancellationToken) =>
            Task.FromResult(0);
    }
}
