using Microsoft.Extensions.Logging.Abstractions;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Jobs;
using Prismedia.Application.Jobs.Handlers;
using Prismedia.Application.Settings;
using Prismedia.Contracts.Acquisition;
using Prismedia.Contracts.Settings;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class MonitoredSearchJobHandlerTests {
    [Fact]
    public async Task EnqueuesOneAcquisitionSearchPerDueMonitorAndStampsEach() {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var monitors = new FakeMonitorStore([
            new DueMonitor(Guid.NewGuid(), a, "Book A"),
            new DueMonitor(Guid.NewGuid(), b, "Book B"),
        ]);
        var queue = new RecordingJobQueue();
        var handler = new MonitoredSearchJobHandler(monitors, new SettingsService(new EmptySettingsPersistence()), NullLogger<MonitoredSearchJobHandler>.Instance);

        await handler.HandleAsync(new JobContext(Job(), queue), CancellationToken.None);

        // One AcquisitionSearch per due monitor, targeted at its acquisition.
        Assert.Equal(2, queue.Enqueued.Count);
        Assert.All(queue.Enqueued, e => Assert.Equal(JobType.AcquisitionSearch, e.Type));
        Assert.Contains(queue.Enqueued, e => e.TargetEntityId == a.ToString());
        Assert.Contains(queue.Enqueued, e => e.TargetEntityId == b.ToString());
        // Each monitor stamped as searched so it isn't re-fired next tick.
        Assert.Equal(2, monitors.Searched.Count);
    }

    [Fact]
    public async Task DedupsAMonitorWhoseSearchIsAlreadyPending() {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var monitors = new FakeMonitorStore([
            new DueMonitor(Guid.NewGuid(), a, "Book A"),
            new DueMonitor(Guid.NewGuid(), b, "Book B"),
        ]);
        var queue = new RecordingJobQueue();
        queue.AlreadyPending.Add(a.ToString()); // a search is already in flight for A
        var handler = new MonitoredSearchJobHandler(monitors, new SettingsService(new EmptySettingsPersistence()), NullLogger<MonitoredSearchJobHandler>.Instance);

        await handler.HandleAsync(new JobContext(Job(), queue), CancellationToken.None);

        // Only B's search is enqueued; A is deduped because one is already pending.
        var enqueued = Assert.Single(queue.Enqueued);
        Assert.Equal(b.ToString(), enqueued.TargetEntityId);
        // Both are stamped: the monitor attempted a pass for each (A's existing search will advance it).
        Assert.Equal(2, monitors.Searched.Count);
    }

    [Fact]
    public async Task NoDueMonitorsEnqueuesNothing() {
        var monitors = new FakeMonitorStore([]);
        var queue = new RecordingJobQueue();
        var handler = new MonitoredSearchJobHandler(monitors, new SettingsService(new EmptySettingsPersistence()), NullLogger<MonitoredSearchJobHandler>.Instance);

        await handler.HandleAsync(new JobContext(Job(), queue), CancellationToken.None);

        Assert.Empty(queue.Enqueued);
        Assert.Empty(monitors.Searched);
    }

    private static JobRunSnapshot Job() {
        var now = DateTimeOffset.UtcNow;
        return new JobRunSnapshot(Guid.NewGuid(), JobType.MonitoredSearch, JobRunStatus.Running, 0, null, "{}", null, null, null, now, now, null);
    }

    private sealed class FakeMonitorStore(IReadOnlyList<DueMonitor> due) : IMonitorStore {
        public List<Guid> Searched { get; } = [];
        public Task<IReadOnlyList<DueMonitor>> ListDueMonitorsAsync(int defaultIntervalMinutes, CancellationToken cancellationToken) => Task.FromResult(due);
        public Task MarkSearchedAsync(Guid monitorId, CancellationToken cancellationToken) { Searched.Add(monitorId); return Task.CompletedTask; }
        public Task<MonitorView> StartAsync(Guid acquisitionId, EntityKind kind, string title, string? author, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(Guid monitorId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> SetStatusAsync(Guid monitorId, MonitorStatus status, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<MonitorView>> ListAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<MonitorView?> GetByAcquisitionAsync(Guid acquisitionId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> HasActiveMonitorsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class EmptySettingsPersistence : ISettingsPersistence {
        public Task<IReadOnlyDictionary<string, string>> LoadSettingOverridesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());
        public Task SaveSettingOverrideAsync(string key, string valueJson, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SaveSettingOverridesAsync(IReadOnlyDictionary<string, string> values, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ReplaceSettingOverridesAsync(IReadOnlyDictionary<string, string> upserts, IReadOnlyCollection<string> deletes, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DeleteSettingOverrideAsync(string key, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<LibraryRoot>> ListLibraryRootsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<LibraryRoot>>([]);
        public Task<LibraryRoot?> GetLibraryRootAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<LibraryRoot?>(null);
        public Task<LibraryRoot> AddLibraryRootAsync(LibraryRoot state, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<LibraryRoot> SaveLibraryRootAsync(LibraryRoot state, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> DeleteLibraryRootAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RecordingJobQueue : IJobQueueService {
        public List<EnqueueJobRequest> Enqueued { get; } = [];
        public Task<JobRunSnapshot> EnqueueAsync(EnqueueJobRequest request, CancellationToken cancellationToken) {
            Enqueued.Add(request);
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(new JobRunSnapshot(Guid.NewGuid(), request.Type, JobRunStatus.Queued, 0, null, request.PayloadJson ?? "{}", request.TargetEntityKind, request.TargetEntityId, request.TargetLabel, now, null, null));
        }
        /// <summary>Target ids treated as already having a pending search, so EnqueueIfNeededAsync dedups them.</summary>
        public HashSet<string> AlreadyPending { get; } = [];
        public Task<bool> HasPendingAsync(JobType type, string? targetEntityId, CancellationToken cancellationToken) =>
            Task.FromResult(targetEntityId is not null && AlreadyPending.Contains(targetEntityId));
        public Task UpdateProgressAsync(Guid id, int progress, string? message, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<JobRunSnapshot>> ListAsync(bool hideNsfw, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<JobRunSnapshot> EnqueueAsync(JobType type, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> EnqueueBatchAsync(IReadOnlyList<EnqueueJobRequest> requests, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> CancelAsync(JobType? type, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> CancelRunAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> ClearFailuresAsync(JobType? type, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<JobRunSnapshot?> ClaimNextAsync(string workerId, CancellationToken cancellationToken, JobRunLane? lane = null) => throw new NotSupportedException();
        public Task<int> RecoverStaleRunningAsync(string currentWorkerId, TimeSpan staleAfter, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CompleteAsync(Guid id, string? message, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task FailAsync(Guid id, string message, TimeSpan retryDelay, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<JobQueueCount>> GetQueueCountsAsync(bool hideNsfw, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> PruneHistoryAsync(TimeSpan retention, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
