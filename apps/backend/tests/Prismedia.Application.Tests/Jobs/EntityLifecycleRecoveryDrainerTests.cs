using Microsoft.Extensions.Logging.Abstractions;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Entities;
using Prismedia.Application.Jobs;

namespace Prismedia.Application.Tests.Jobs;

public sealed class EntityLifecycleRecoveryDrainerTests {
    [Fact]
    public async Task DrainBatchProcessesClaimsSequentiallyAndIsolatesFailures() {
        var firstDelete = Guid.NewGuid();
        var secondDelete = Guid.NewGuid();
        var stoppingMonitor = Guid.NewGuid();
        var activity = new RecoveryActivity();
        var drainer = new EntityLifecycleRecoveryDrainer(
            new FixedRecoveryReader(new EntityLifecycleRecoveryBatch(
                [firstDelete, secondDelete],
                [],
                [stoppingMonitor])),
            new RecordingDeletionService(activity, firstDelete),
            new RecordingUnmonitorService(activity),
            NullLogger<EntityLifecycleRecoveryDrainer>.Instance);

        var result = await drainer.DrainBatchAsync(10, CancellationToken.None);

        Assert.Equal(3, result.Attempted);
        Assert.Equal(2, result.Resolved);
        Assert.Equal(1, result.Failed);
        Assert.Equal(1, activity.MaximumConcurrency);
        Assert.Equal(
            [$"delete:{firstDelete}", $"delete:{secondDelete}", $"stop:{stoppingMonitor}"],
            activity.Order);
    }

    private sealed class FixedRecoveryReader(EntityLifecycleRecoveryBatch batch)
        : IEntityLifecycleRecoveryStore {
        public Task<EntityLifecycleRecoveryBatch> ListAsync(int limit, CancellationToken cancellationToken) =>
            Task.FromResult(batch);

        public Task<bool> CompleteOrphanedDeletionAsync(Guid monitorId, CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }

    private sealed class RecordingDeletionService(RecoveryActivity activity, Guid failureId)
        : IMediaEntityDeletionService {
        public Task<MediaEntityBulkDeleteResult> DeleteManyAsync(
            IReadOnlyList<Guid> ids,
            bool deleteFiles,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public async Task<MediaEntityDeleteResult> DeleteAsync(
            Guid id,
            bool deleteFiles,
            CancellationToken cancellationToken) {
            await activity.RecordAsync($"delete:{id}", cancellationToken);
            return id == failureId
                ? new MediaEntityDeleteResult(false, "blocked", FailureKind: MediaEntityDeleteFailureKind.Conflict)
                : new MediaEntityDeleteResult(true);
        }
    }

    private sealed class RecordingUnmonitorService(RecoveryActivity activity) : IEntityUnmonitorService {
        public async Task<MonitorStopResult> StopAsync(Guid monitorId, CancellationToken cancellationToken) {
            await activity.RecordAsync($"stop:{monitorId}", cancellationToken);
            return new MonitorStopResult(true, true);
        }
    }

    private sealed class RecoveryActivity {
        private int _active;

        public List<string> Order { get; } = [];
        public int MaximumConcurrency { get; private set; }

        public async Task RecordAsync(string value, CancellationToken cancellationToken) {
            var active = Interlocked.Increment(ref _active);
            MaximumConcurrency = Math.Max(MaximumConcurrency, active);
            try {
                Order.Add(value);
                await Task.Yield();
                cancellationToken.ThrowIfCancellationRequested();
            } finally {
                Interlocked.Decrement(ref _active);
            }
        }
    }
}
