using Microsoft.Extensions.Logging.Abstractions;
using Prismedia.Application.Jobs;
using Prismedia.Application.Jobs.Handlers;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class IdentifyCascadeJobHandlerTests {
    [Fact]
    public async Task HandleAsyncRequeuesImmediatelyWhenProviderSlotIsBusy() {
        var gate = new AutoIdentifyConcurrencyGate();
        var handler = new IdentifyCascadeJobHandler(
            new NoopCascadeRunner(),
            gate,
            NullLogger<IdentifyCascadeJobHandler>.Instance);

        using var lease = gate.TryEnterInteractive();
        Assert.NotNull(lease);

        var busy = await Assert.ThrowsAsync<JobRetryLaterException>(() =>
            handler.HandleAsync(new JobContext(CreateJob(), new IdentifySearchJobHandlerTestsQueue()), CancellationToken.None));

        Assert.Equal("Identify provider slot busy.", busy.Message);
        Assert.Equal(TimeSpan.FromSeconds(5), busy.RetryDelay);
    }

    [Fact]
    public async Task HandleAsyncRunsTheCascadeWhileHoldingTheProviderSlot() {
        var gate = new AutoIdentifyConcurrencyGate();
        var runner = new NoopCascadeRunner();
        var handler = new IdentifyCascadeJobHandler(runner, gate, NullLogger<IdentifyCascadeJobHandler>.Instance);

        await handler.HandleAsync(new JobContext(CreateJob(), new IdentifySearchJobHandlerTestsQueue()), CancellationToken.None);

        Assert.Equal(1, runner.Calls);
        // The slot was released when the cascade finished.
        var lease = gate.TryEnterInteractive();
        Assert.NotNull(lease);
        lease.Dispose();
    }

    private static JobRunSnapshot CreateJob() =>
        new(
            Guid.NewGuid(),
            JobType.IdentifyCascade,
            JobRunStatus.Running,
            Progress: 0,
            Message: null,
            PayloadJson: new IdentifyCascadePayload(Guid.NewGuid(), "tmdb", null, false).ToJson(),
            TargetEntityKind: "video-series",
            TargetEntityId: Guid.NewGuid().ToString(),
            TargetLabel: "Cascade test",
            CreatedAt: DateTimeOffset.UtcNow,
            StartedAt: DateTimeOffset.UtcNow,
            FinishedAt: null);

    private sealed class NoopCascadeRunner : IIdentifyCascadeRunner {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public Task RunAsync(IdentifyCascadePayload payload, Guid cascadeJobId, bool isFinalAttempt, CancellationToken cancellationToken) {
            Interlocked.Increment(ref _calls);
            return Task.CompletedTask;
        }
    }
}

/// <summary>Minimal job queue stub shared by the cascade handler tests.</summary>
internal sealed class IdentifySearchJobHandlerTestsQueue : IJobQueueService {
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
