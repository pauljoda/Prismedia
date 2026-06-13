using Microsoft.Extensions.Logging.Abstractions;
using Prismedia.Application.Jobs;
using Prismedia.Application.Jobs.Handlers;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class IdentifySearchJobHandlerTests {
    [Fact]
    public async Task HandleAsyncDelegatesPayloadAndJobIdToTheRunner() {
        var runner = new RecordingIdentifySearchRunner();
        var handler = new IdentifySearchJobHandler(
            runner,
            new AutoIdentifyConcurrencyGate(),
            NullLogger<IdentifySearchJobHandler>.Instance);
        var entityId = Guid.NewGuid();
        var job = CreateJob(entityId, "tmdb");

        runner.Release();
        await handler.HandleAsync(new JobContext(job, new NoopJobQueue()), CancellationToken.None);

        var call = Assert.Single(runner.Calls);
        Assert.Equal(entityId, call.Payload.EntityId);
        Assert.Equal("tmdb", call.Payload.Provider);
        Assert.Equal(job.Id, call.SearchJobId);
    }

    [Fact]
    public async Task HandleAsyncRequeuesImmediatelyWhenIdentifySlotIsBusy() {
        var runner = new RecordingIdentifySearchRunner();
        var gate = new AutoIdentifyConcurrencyGate();
        var handler = new IdentifySearchJobHandler(runner, gate, NullLogger<IdentifySearchJobHandler>.Instance);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var first = handler.HandleAsync(new JobContext(CreateJob(Guid.NewGuid(), "tmdb"), new NoopJobQueue()), timeout.Token);
        await runner.WaitForStartedCallsAsync(1, timeout.Token);

        var busy = await Assert.ThrowsAsync<JobRetryLaterException>(() =>
            handler.HandleAsync(new JobContext(CreateJob(Guid.NewGuid(), "tmdb"), new NoopJobQueue()), timeout.Token));

        Assert.Equal("Identify search slot busy.", busy.Message);
        Assert.Equal(TimeSpan.FromSeconds(5), busy.RetryDelay);
        Assert.Equal(1, runner.StartedCalls);

        runner.Release();
        await first;
    }

    [Fact]
    public async Task HandleAsyncRequeuesWhenSearchExceedsTimeout() {
        var runner = new RecordingIdentifySearchRunner();
        var handler = new IdentifySearchJobHandler(
            runner,
            new AutoIdentifyConcurrencyGate(),
            NullLogger<IdentifySearchJobHandler>.Instance,
            TimeSpan.FromMilliseconds(25));

        var retry = await Assert.ThrowsAsync<JobRetryLaterException>(() =>
            handler.HandleAsync(new JobContext(CreateJob(Guid.NewGuid(), "tmdb"), new NoopJobQueue()), CancellationToken.None));

        Assert.Equal("Identify search timed out after 0 seconds.", retry.Message);
        Assert.Equal(TimeSpan.FromMinutes(1), retry.RetryDelay);
        Assert.Equal(1, runner.StartedCalls);
    }

    private static JobRunSnapshot CreateJob(Guid entityId, string? provider) =>
        new(
            Guid.NewGuid(),
            JobType.IdentifySearch,
            JobRunStatus.Running,
            Progress: 0,
            Message: null,
            PayloadJson: new IdentifySearchPayload(entityId, provider, null, false).ToJson(),
            TargetEntityKind: "video",
            TargetEntityId: entityId.ToString(),
            TargetLabel: "Identify search test",
            CreatedAt: DateTimeOffset.UtcNow,
            StartedAt: DateTimeOffset.UtcNow,
            FinishedAt: null);

    private sealed class RecordingIdentifySearchRunner : IIdentifySearchRunner {
        private readonly SemaphoreSlim _release = new(0);
        private int _startedCalls;

        public List<(IdentifySearchPayload Payload, Guid SearchJobId, bool IsFinalAttempt)> Calls { get; } = [];
        public int StartedCalls => Volatile.Read(ref _startedCalls);

        public async Task RunAsync(IdentifySearchPayload payload, Guid searchJobId, bool isFinalAttempt, CancellationToken cancellationToken) {
            Interlocked.Increment(ref _startedCalls);
            lock (Calls) {
                Calls.Add((payload, searchJobId, isFinalAttempt));
            }

            await _release.WaitAsync(cancellationToken);
        }

        public void Release() => _release.Release(int.MaxValue / 2);

        public async Task WaitForStartedCallsAsync(int expected, CancellationToken cancellationToken) {
            while (StartedCalls < expected) {
                await Task.Delay(10, cancellationToken);
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

        public Task<JobRunSnapshot?> ClaimNextAsync(string workerId, CancellationToken cancellationToken, JobRunLane? lane = null) =>
            Task.FromResult<JobRunSnapshot?>(null);

        public Task<int> RecoverStaleRunningAsync(string currentWorkerId, TimeSpan staleAfter, CancellationToken cancellationToken) =>
            Task.FromResult(0);

        public Task UpdateProgressAsync(Guid id, int progress, string? message, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task CompleteAsync(Guid id, string? message, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task FailAsync(Guid id, string message, TimeSpan retryDelay, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task DeferAsync(Guid id, string message, TimeSpan retryDelay, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<JobQueueCount>> GetQueueCountsAsync(bool hideNsfw, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<JobQueueCount>>([]);

        public Task<int> PruneHistoryAsync(TimeSpan retention, CancellationToken cancellationToken) =>
            Task.FromResult(0);
    }
}
