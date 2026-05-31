using Microsoft.Extensions.Logging.Abstractions;
using Prismedia.Application.Jobs;
using Prismedia.Application.Jobs.Handlers;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class AutoIdentifyJobHandlerTests {
    [Fact]
    public async Task HandleAsyncRunsOnlyOneAutoIdentifyAtATime() {
        var runner = new BlockingAutoIdentifyRunner();
        var handler = new AutoIdentifyJobHandler(
            runner,
            new AutoIdentifyConcurrencyGate(),
            NullLogger<AutoIdentifyJobHandler>.Instance);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var first = handler.HandleAsync(new JobContext(CreateJob(Guid.NewGuid()), new NoopJobQueue()), timeout.Token);
        await runner.WaitForStartedCallsAsync(1, timeout.Token);

        var second = handler.HandleAsync(new JobContext(CreateJob(Guid.NewGuid()), new NoopJobQueue()), timeout.Token);
        await Task.Delay(100, timeout.Token);

        Assert.Equal(1, runner.MaxActive);
        Assert.Equal(1, runner.StartedCalls);

        runner.ReleaseNext();
        await first;
        await runner.WaitForStartedCallsAsync(2, timeout.Token);

        Assert.Equal(1, runner.MaxActive);

        runner.ReleaseNext();
        await second;
    }

    private static JobRunSnapshot CreateJob(Guid entityId) =>
        new(
            Guid.NewGuid(),
            JobType.AutoIdentify,
            JobRunStatus.Running,
            Progress: 0,
            Message: null,
            PayloadJson: "{}",
            TargetEntityKind: "video",
            TargetEntityId: entityId.ToString(),
            TargetLabel: "Auto identify test",
            CreatedAt: DateTimeOffset.UtcNow,
            StartedAt: DateTimeOffset.UtcNow,
            FinishedAt: null);

    private sealed class BlockingAutoIdentifyRunner : IAutoIdentifyRunner {
        private readonly SemaphoreSlim _release = new(0);
        private int _active;
        private int _maxActive;
        private int _startedCalls;

        public int MaxActive => Volatile.Read(ref _maxActive);
        public int StartedCalls => Volatile.Read(ref _startedCalls);

        public async Task<AutoIdentifyResult> RunAsync(Guid entityId, CancellationToken cancellationToken) {
            var active = Interlocked.Increment(ref _active);
            TrackMaxActive(active);
            Interlocked.Increment(ref _startedCalls);

            try {
                await _release.WaitAsync(cancellationToken);
                return new AutoIdentifyResult(false, SkipReason: "test complete");
            } finally {
                Interlocked.Decrement(ref _active);
            }
        }

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

        public Task<JobRunSnapshot?> ClaimNextAsync(string workerId, CancellationToken cancellationToken) =>
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
