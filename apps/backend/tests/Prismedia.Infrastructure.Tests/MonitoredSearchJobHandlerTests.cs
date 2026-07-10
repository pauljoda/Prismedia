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
        var acquisitions = new FakeAcquisitionLifecycleStore(a, b);
        var queue = new RecordingJobQueue();
        var handler = Handler(monitors, acquisitions);

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
        var acquisitions = new FakeAcquisitionLifecycleStore(a, b);
        var queue = new RecordingJobQueue();
        queue.AlreadyPending.Add(a.ToString()); // a search is already in flight for A
        var handler = Handler(monitors, acquisitions);

        await handler.HandleAsync(new JobContext(Job(), queue), CancellationToken.None);

        // Only B's search is enqueued; A is deduped because one is already pending.
        var enqueued = Assert.Single(queue.Enqueued);
        Assert.Equal(b.ToString(), enqueued.TargetEntityId);
        // Both are stamped: the monitor attempted a pass for each (A's existing search will advance it).
        Assert.Equal(2, monitors.Searched.Count);
    }

    [Fact]
    public async Task AcquisitionLinkedEntityMonitorUsesAcquisitionLifecycleNotEntityMaintenance() {
        var acquisitionId = Guid.NewGuid();
        var monitors = new FakeMonitorStore([
            new DueMonitor(Guid.NewGuid(), acquisitionId, "Book", EntityId: Guid.NewGuid())
        ]);
        var acquisitions = new FakeAcquisitionLifecycleStore(acquisitionId);
        var queue = new RecordingJobQueue();
        var handler = Handler(monitors, acquisitions);

        await handler.HandleAsync(new JobContext(Job(), queue), CancellationToken.None);

        Assert.Equal(acquisitionId.ToString(), Assert.Single(queue.Enqueued).TargetEntityId);
    }

    [Fact]
    public async Task NoDueMonitorsEnqueuesNothing() {
        var monitors = new FakeMonitorStore([]);
        var acquisitions = new FakeAcquisitionLifecycleStore();
        var queue = new RecordingJobQueue();
        var handler = Handler(monitors, acquisitions);

        await handler.HandleAsync(new JobContext(Job(), queue), CancellationToken.None);

        Assert.Empty(queue.Enqueued);
        Assert.Empty(monitors.Searched);
    }

    [Fact]
    public async Task ActiveMonitorExplicitlyRevivesCancelledBeforeEnqueueingSearch() {
        var acquisitionId = Guid.NewGuid();
        var monitors = new FakeMonitorStore([
            new DueMonitor(Guid.NewGuid(), acquisitionId, "Book")
        ]);
        var acquisitions = new FakeAcquisitionLifecycleStore(acquisitionId);
        acquisitions.Statuses[acquisitionId] = AcquisitionStatus.Cancelled;
        var queue = new RecordingJobQueue();

        await Handler(monitors, acquisitions)
            .HandleAsync(new JobContext(Job(), queue), CancellationToken.None);

        Assert.Equal(AcquisitionStatus.Searching, acquisitions.Statuses[acquisitionId]);
        Assert.Equal(
            (acquisitionId, AcquisitionStatus.Cancelled, AcquisitionStatus.Searching),
            Assert.Single(acquisitions.Transitions));
        Assert.Equal(acquisitionId.ToString(), Assert.Single(queue.Enqueued).TargetEntityId);
    }

    [Fact]
    public async Task MonitorPublishesSearchingBeforeAnEnqueueFailure() {
        var acquisitionId = Guid.NewGuid();
        var monitors = new FakeMonitorStore([
            new DueMonitor(Guid.NewGuid(), acquisitionId, "Book")
        ]);
        var acquisitions = new FakeAcquisitionLifecycleStore(acquisitionId);
        acquisitions.Statuses[acquisitionId] = AcquisitionStatus.Failed;
        var queue = new RecordingJobQueue { EnqueueFailure = new IOException("queue unavailable") };

        await Assert.ThrowsAsync<IOException>(() =>
            Handler(monitors, acquisitions)
                .HandleAsync(new JobContext(Job(), queue), CancellationToken.None));

        Assert.Equal(AcquisitionStatus.Searching, acquisitions.Statuses[acquisitionId]);
        Assert.Empty(queue.Enqueued);
    }

    [Fact]
    public async Task EntityMaintenanceContentionNeverPausesDurableParentIntent() {
        var monitorId = Guid.NewGuid();
        var monitors = new FakeMonitorStore([
            new DueMonitor(monitorId, null, "Artist", EntityId: Guid.NewGuid())
        ]);

        await Handler(monitors, new FakeAcquisitionLifecycleStore())
            .HandleAsync(new JobContext(Job(), new RecordingJobQueue()), CancellationToken.None);

        Assert.Equal([monitorId], monitors.Searched);
    }

    private static MonitoredSearchJobHandler Handler(
        IMonitorStore monitors,
        IAcquisitionLifecycleStore acquisitions) =>
        new(
            monitors,
            acquisitions,
            new SettingsService(new EmptySettingsPersistence()),
            CommitService(monitors),
            NullLogger<MonitoredSearchJobHandler>.Instance);

    private static JobRunSnapshot Job() {
        var now = DateTimeOffset.UtcNow;
        return new JobRunSnapshot(Guid.NewGuid(), JobType.MonitoredSearch, JobRunStatus.Running, 0, null, "{}", null, null, null, now, now, null);
    }

    /// <summary>A commit service whose container sync never resolves (null sources), for the non-container test paths.</summary>
    private static Prismedia.Application.Requests.RequestCommitService CommitService(IMonitorStore monitors) =>
        new(
            new NullProposalSource(),
            new NullReviewSource(),
            new NullWantedWriter(),
            new NullAcquisitionRequestService(),
            monitors,
            new NullSuppressionStore(),
            new NullEntityGiveUpService());

    private sealed class NullEntityGiveUpService : IEntityGiveUpService {
        public Task<MonitorStopResult> GiveUpEntityAsync(Guid entityId, CancellationToken cancellationToken) =>
            Task.FromResult(new MonitorStopResult(Found: false, Stopped: false));
    }

    private sealed class NullReviewSource : Prismedia.Application.Requests.IPluginRequestReviewSource {
        public Task<Prismedia.Contracts.Requests.RequestReviewResponse?> ReviewAsync(
            Prismedia.Contracts.Requests.RequestReviewRequest request,
            bool hideNsfw,
            CancellationToken cancellationToken) =>
            Task.FromResult<Prismedia.Contracts.Requests.RequestReviewResponse?>(null);

        public Task<Prismedia.Contracts.Requests.RequestReviewResponse?> RevalidateAsync(
            Prismedia.Contracts.Requests.RequestReviewRequest request,
            bool hideNsfw,
            CancellationToken cancellationToken) =>
            Task.FromResult<Prismedia.Contracts.Requests.RequestReviewResponse?>(null);
    }

    private sealed class NullSuppressionStore : Prismedia.Application.Requests.IWantedSuppressionStore {
        public Task SuppressAsync(IReadOnlyList<ExternalIdentity> identities, EntityKind kind, string title, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlySet<ExternalIdentity>> FilterSuppressedAsync(IReadOnlyList<ExternalIdentity> identities, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<ExternalIdentity>>(new HashSet<ExternalIdentity>());
        public Task ClearAsync(IReadOnlyList<ExternalIdentity> identities, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class NullProposalSource : Prismedia.Application.Requests.IPluginRequestProposalSource {
        public Task<Prismedia.Contracts.Plugins.EntityMetadataProposal?> ResolveProposalAsync(
            Prismedia.Application.Requests.RequestKindDescriptor descriptor,
            Prismedia.Application.Plugins.PluginIdentityRoute route,
            bool hideNsfw,
            bool includeChildren,
            CancellationToken cancellationToken) =>
            Task.FromResult<Prismedia.Contracts.Plugins.EntityMetadataProposal?>(null);

        public Task<Prismedia.Application.Requests.RoutedRequestProposal?> ResolveProposalAsync(
            Prismedia.Application.Requests.RequestKindDescriptor descriptor, ExternalIdentity identity, bool hideNsfw, bool includeChildren, CancellationToken cancellationToken) =>
            Task.FromResult<Prismedia.Application.Requests.RoutedRequestProposal?>(null);
    }

    private sealed class NullWantedWriter : Prismedia.Application.Requests.IWantedEntityWriter {
        public Task<Prismedia.Application.Requests.WantedEntityResult> EnsureAsync(EntityKind kind, ExternalIdentity identity, string title, Guid? parentEntityId, bool matchTitleKindWide, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> BindProviderIdentityAsync(Guid entityId, Prismedia.Application.Plugins.PluginIdentityRoute route, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task ApplyProposalAsync(Guid entityId, Prismedia.Contracts.Plugins.EntityMetadataProposal proposal, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> DeleteIfWantedAsync(Guid entityId, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<Prismedia.Application.Requests.MonitorableEntity?> GetEntityAsync(Guid entityId, CancellationToken cancellationToken) =>
            Task.FromResult<Prismedia.Application.Requests.MonitorableEntity?>(null);
        public Task<IReadOnlyList<Guid>> ListWantedChildIdsAsync(Guid parentEntityId, EntityKind childKind, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Guid>>([]);

        public Task<IReadOnlyList<Guid>> ListChildIdsAsync(Guid parentEntityId, EntityKind childKind, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Guid>>([]);
    }

    private sealed class NullAcquisitionRequestService : IAcquisitionRequestService {
        public Task<AcquisitionSummary> CreateAndSearchAsync(AcquisitionCreateRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> AnyOpenForEntityAsync(Guid entityId, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<IReadOnlyList<Guid>> ListIdsForEntityAsync(Guid entityId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Guid>>([]);
        public Task<AcquisitionReacquireEligibility> GetReacquireEligibilityAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(new AcquisitionReacquireEligibility(false, "The acquisition no longer exists."));
        public Task<AcquisitionRemovalEligibility> GetRemovalEligibilityAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(new AcquisitionRemovalEligibility(true));
        public Task<bool> ClaimTeardownAsync(Guid id, AcquisitionTeardownIntent intent, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task ConfirmTransferRemovedAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<bool> CompleteTeardownAsync(Guid id, AcquisitionTeardownIntent intent, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken, bool preserveWantedLoop = false) => Task.FromResult(false);
        public Task<bool> DeleteForUnmonitorAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<Guid?> ReacquireAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<Guid?>(null);
    }

    private sealed class FakeMonitorStore(IReadOnlyList<DueMonitor> due) : IMonitorStore {
        public List<Guid> Searched { get; } = [];
        public List<Guid> CreatedChildFor { get; } = [];
        public Guid? ChildId { get; set; }
        public Task<IReadOnlyList<DueMonitor>> ListDueMonitorsAsync(int defaultIntervalMinutes, CancellationToken cancellationToken) => Task.FromResult(due);
        public Task<bool> IsActiveAsync(Guid monitorId, CancellationToken cancellationToken) =>
            Task.FromResult(due.Any(monitor => monitor.MonitorId == monitorId));
        public Task MarkSearchedAsync(Guid monitorId, CancellationToken cancellationToken) { Searched.Add(monitorId); return Task.CompletedTask; }
        public Task<Guid?> CreateUpgradeChildAsync(Guid monitorId, CancellationToken cancellationToken) { CreatedChildFor.Add(monitorId); return Task.FromResult(ChildId); }
        public Task ResolveUpgradeChildAsync(Guid childId, bool succeeded, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<MonitorView> StartAsync(Guid acquisitionId, EntityKind kind, string title, string? author, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(Guid monitorId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> RetargetAsync(Guid fromAcquisitionId, Guid toAcquisitionId, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<bool> SetStatusAsync(Guid monitorId, MonitorStatus status, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<MonitorView>> ListAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<WantedPage> ListMissingAsync(int page, int pageSize, EntityKind? kind, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<WantedPage> ListCutoffUnmetAsync(int page, int pageSize, EntityKind? kind, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<MonitorView?> GetByAcquisitionAsync(Guid acquisitionId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<MonitorView> StartForEntityAsync(Guid entityId, EntityKind kind, string title, AcquisitionTargeting? targeting, MonitorPreset? preset, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<MonitorView?> GetByEntityAsync(Guid entityId, CancellationToken cancellationToken) => Task.FromResult<MonitorView?>(null);
        public Task<AcquisitionTargeting?> GetTargetingByEntityAsync(Guid entityId, CancellationToken cancellationToken) => Task.FromResult<AcquisitionTargeting?>(null);
        public Task<MonitorPreset?> GetPresetByEntityAsync(Guid entityId, CancellationToken cancellationToken) => Task.FromResult<MonitorPreset?>(null);
        public Task<bool> HasActiveMonitorsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeAcquisitionLifecycleStore(params Guid[] acquisitionIds)
        : IAcquisitionLifecycleStore {
        public Dictionary<Guid, AcquisitionStatus> Statuses { get; } =
            acquisitionIds.ToDictionary(id => id, _ => AcquisitionStatus.Pending);
        public List<(Guid Id, AcquisitionStatus Expected, AcquisitionStatus Status)> Transitions { get; } = [];

        public Task<AcquisitionStatus?> GetStatusAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Statuses.TryGetValue(id, out var status)
                ? (AcquisitionStatus?)status
                : null);

        public Task<bool> TryTransitionStatusAsync(
            Guid id,
            IReadOnlyCollection<AcquisitionStatus> expectedStatuses,
            AcquisitionStatus status,
            string? message,
            CancellationToken cancellationToken) {
            if (!Statuses.TryGetValue(id, out var current) || !expectedStatuses.Contains(current)) {
                return Task.FromResult(false);
            }

            Statuses[id] = status;
            Transitions.Add((id, current, status));
            return Task.FromResult(true);
        }

        public Task<bool> TryClaimFailedRecoveryAsync(
            Guid id,
            IReadOnlyCollection<AcquisitionStatus> expectedStatuses,
            SelectedRelease? expectedSelectedRelease,
            string message,
            CancellationToken cancellationToken) => throw new NotSupportedException();
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
        public Exception? EnqueueFailure { get; init; }
        public Task<JobRunSnapshot> EnqueueAsync(EnqueueJobRequest request, CancellationToken cancellationToken) {
            if (EnqueueFailure is not null) {
                throw EnqueueFailure;
            }

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
