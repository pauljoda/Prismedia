using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Jobs;
using Prismedia.Application.Settings;
using Prismedia.Contracts.Acquisition;
using Prismedia.Contracts.Settings;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class JobSchedulerTests {
    [Fact]
    public async Task ScheduleRecurringScansAsyncSkipsRootAwayFromScheduleBoundary() {
        var rootId = Guid.NewGuid();
        var settings = new SchedulerSettingsPersistence([
            CreateRoot(rootId, lastScannedAt: null)
        ]);
        var queue = new SchedulerJobQueue();
        await using var provider = CreateProvider(settings, queue);
        var scheduler = CreateScheduler(provider, new DateTimeOffset(2026, 5, 30, 10, 37, 0, TimeSpan.Zero));

        await scheduler.ScheduleRecurringScansAsync(CancellationToken.None);

        Assert.Empty(queue.Enqueued);
        Assert.Null(settings.Roots.Single().LastScannedAt);
    }

    [Fact]
    public async Task ScheduleRecurringScansAsyncQueuesRootOnScheduleBoundaryAndRecordsTrigger() {
        var rootId = Guid.NewGuid();
        var triggeredAt = new DateTimeOffset(2026, 5, 30, 11, 0, 15, TimeSpan.Zero);
        var settings = new SchedulerSettingsPersistence([
            CreateRoot(rootId, lastScannedAt: null)
        ]);
        var queue = new SchedulerJobQueue();
        await using var provider = CreateProvider(settings, queue);
        var scheduler = CreateScheduler(provider, triggeredAt);

        await scheduler.ScheduleRecurringScansAsync(CancellationToken.None);

        var request = Assert.Single(queue.Enqueued);
        Assert.Equal(JobType.ScanLibrary, request.Type);
        // Scans are aggregate per-kind singletons that cover every enabled root, so there is no
        // per-root target.
        Assert.Null(request.TargetEntityId);
        Assert.Equal(triggeredAt, settings.Roots.Single().LastScannedAt);
    }

    [Fact]
    public async Task ScheduleRecurringScansEnqueuesOneScanPerEnabledKindAcrossAllDueRoots() {
        var videoRoot = CreateRoot(Guid.NewGuid(), lastScannedAt: null);
        var imageBookRoot = CreateRoot(Guid.NewGuid(), lastScannedAt: null) with {
            ScanVideos = false,
            ScanImages = true,
            ScanBooks = true,
        };
        var settings = new SchedulerSettingsPersistence([videoRoot, imageBookRoot]);
        var queue = new SchedulerJobQueue();
        await using var provider = CreateProvider(settings, queue);
        var scheduler = CreateScheduler(provider, new DateTimeOffset(2026, 5, 30, 11, 0, 15, TimeSpan.Zero));

        await scheduler.ScheduleRecurringScansAsync(CancellationToken.None);

        // One scan per enabled kind across both roots: video from the first, image + book from the
        // second — and nothing per-root.
        var types = queue.Enqueued.Select(request => request.Type).ToHashSet();
        Assert.Equal(3, queue.Enqueued.Count);
        Assert.Contains(JobType.ScanLibrary, types);
        Assert.Contains(JobType.ScanGallery, types);
        Assert.Contains(JobType.ScanBook, types);
        Assert.DoesNotContain(JobType.ScanAudio, types);
        Assert.All(queue.Enqueued, request => Assert.Null(request.TargetEntityId));
        Assert.All(settings.Roots, root => Assert.NotNull(root.LastScannedAt));
    }

    [Fact]
    public async Task ScheduleRecurringScansAsyncSkipsRootAlreadyTriggeredInCurrentWindow() {
        var rootId = Guid.NewGuid();
        var settings = new SchedulerSettingsPersistence([
            CreateRoot(rootId, lastScannedAt: new DateTimeOffset(2026, 5, 30, 11, 0, 5, TimeSpan.Zero))
        ]);
        var queue = new SchedulerJobQueue();
        await using var provider = CreateProvider(settings, queue);
        var scheduler = CreateScheduler(provider, new DateTimeOffset(2026, 5, 30, 11, 0, 20, TimeSpan.Zero));

        await scheduler.ScheduleRecurringScansAsync(CancellationToken.None);

        Assert.Empty(queue.Enqueued);
    }

    [Fact]
    public async Task ScheduleRecurringCollectionRefreshAsyncQueuesUntargetedRefreshOnHourBoundary() {
        var settings = new SchedulerSettingsPersistence([]);
        var queue = new SchedulerJobQueue();
        await using var provider = CreateProvider(settings, queue);
        var scheduler = CreateScheduler(provider, new DateTimeOffset(2026, 5, 30, 11, 0, 15, TimeSpan.Zero));

        await scheduler.ScheduleRecurringCollectionRefreshAsync(CancellationToken.None);

        var request = Assert.Single(queue.Enqueued);
        Assert.Equal(JobType.RefreshCollection, request.Type);
        Assert.Null(request.TargetEntityId);
        Assert.Equal("Hourly collection refresh", request.TargetLabel);
    }

    [Fact]
    public async Task ScheduleRecurringCollectionRefreshAsyncSkipsAwayFromHourBoundary() {
        var settings = new SchedulerSettingsPersistence([]);
        var queue = new SchedulerJobQueue();
        await using var provider = CreateProvider(settings, queue);
        var scheduler = CreateScheduler(provider, new DateTimeOffset(2026, 5, 30, 11, 12, 0, TimeSpan.Zero));

        await scheduler.ScheduleRecurringCollectionRefreshAsync(CancellationToken.None);

        Assert.Empty(queue.Enqueued);
    }

    [Fact]
    public async Task ScheduleRecurringCollectionRefreshAsyncSkipsWhenDisabled() {
        var settings = new SchedulerSettingsPersistence([], collectionAutoRefreshEnabled: false);
        var queue = new SchedulerJobQueue();
        await using var provider = CreateProvider(settings, queue);
        var scheduler = CreateScheduler(provider, new DateTimeOffset(2026, 5, 30, 11, 0, 15, TimeSpan.Zero));

        await scheduler.ScheduleRecurringCollectionRefreshAsync(CancellationToken.None);

        Assert.Empty(queue.Enqueued);
    }

    [Fact]
    public async Task ScheduleRecurringCollectionRefreshAsyncSkipsWhenRefreshIsAlreadyPending() {
        var settings = new SchedulerSettingsPersistence([]);
        var queue = new SchedulerJobQueue(hasPendingRefresh: true);
        await using var provider = CreateProvider(settings, queue);
        var scheduler = CreateScheduler(provider, new DateTimeOffset(2026, 5, 30, 11, 0, 15, TimeSpan.Zero));

        await scheduler.ScheduleRecurringCollectionRefreshAsync(CancellationToken.None);

        Assert.Empty(queue.Enqueued);
    }

    [Fact]
    public async Task ScheduleMonitoredSearchEnqueuesOnBoundaryWhenActiveMonitorsExist() {
        var queue = new SchedulerJobQueue();
        await using var provider = CreateProvider(new SchedulerSettingsPersistence([]), queue, new SchedulerMonitorStore(hasActive: true));
        var scheduler = CreateScheduler(provider, new DateTimeOffset(2026, 5, 30, 12, 0, 15, TimeSpan.Zero));

        await scheduler.ScheduleMonitoredSearchAsync(CancellationToken.None);

        var request = Assert.Single(queue.Enqueued);
        Assert.Equal(JobType.MonitoredSearch, request.Type);
        Assert.Null(request.TargetEntityId);
        Assert.Equal("Re-search monitored items", request.TargetLabel);
    }

    [Fact]
    public async Task ScheduleMonitoredSearchSkipsAwayFromWindowBoundary() {
        var queue = new SchedulerJobQueue();
        await using var provider = CreateProvider(new SchedulerSettingsPersistence([]), queue, new SchedulerMonitorStore(hasActive: true));
        var scheduler = CreateScheduler(provider, new DateTimeOffset(2026, 5, 30, 12, 12, 0, TimeSpan.Zero));

        await scheduler.ScheduleMonitoredSearchAsync(CancellationToken.None);

        Assert.Empty(queue.Enqueued);
    }

    [Fact]
    public async Task ScheduleMonitoredSearchSkipsWhenDisabled() {
        var queue = new SchedulerJobQueue();
        await using var provider = CreateProvider(new SchedulerSettingsPersistence([], monitoringSearchEnabled: false), queue, new SchedulerMonitorStore(hasActive: true));
        var scheduler = CreateScheduler(provider, new DateTimeOffset(2026, 5, 30, 12, 0, 15, TimeSpan.Zero));

        await scheduler.ScheduleMonitoredSearchAsync(CancellationToken.None);

        Assert.Empty(queue.Enqueued);
    }

    [Fact]
    public async Task ScheduleMonitoredSearchSkipsWhenNoActiveMonitors() {
        var queue = new SchedulerJobQueue();
        await using var provider = CreateProvider(new SchedulerSettingsPersistence([]), queue, new SchedulerMonitorStore(hasActive: false));
        var scheduler = CreateScheduler(provider, new DateTimeOffset(2026, 5, 30, 12, 0, 15, TimeSpan.Zero));

        await scheduler.ScheduleMonitoredSearchAsync(CancellationToken.None);

        Assert.Empty(queue.Enqueued);
    }

    [Fact]
    public async Task ScheduleMonitoredSearchSkipsWhenAlreadyPending() {
        var queue = new SchedulerJobQueue(hasPendingMonitoredSearch: true);
        await using var provider = CreateProvider(new SchedulerSettingsPersistence([]), queue, new SchedulerMonitorStore(hasActive: true));
        var scheduler = CreateScheduler(provider, new DateTimeOffset(2026, 5, 30, 12, 0, 15, TimeSpan.Zero));

        await scheduler.ScheduleMonitoredSearchAsync(CancellationToken.None);

        Assert.Empty(queue.Enqueued);
    }

    private static ServiceProvider CreateProvider(
        ISettingsPersistence settings,
        IJobQueueService queue,
        IMonitorStore? monitors = null) {
        var services = new ServiceCollection();
        services.AddSingleton(settings);
        services.AddScoped<SettingsService>();
        services.AddSingleton(queue);
        services.AddSingleton(monitors ?? new SchedulerMonitorStore(hasActive: false));
        return services.BuildServiceProvider();
    }

    private static JobScheduler CreateScheduler(ServiceProvider provider, DateTimeOffset now) =>
        new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<JobScheduler>.Instance,
            new FixedTimeProvider(now));

    private static LibraryRoot CreateRoot(Guid id, DateTimeOffset? lastScannedAt) {
        var now = DateTimeOffset.UtcNow;
        return new LibraryRoot(
            id,
            "/media/library",
            "Library",
            Enabled: true,
            Recursive: true,
            ScanVideos: true,
            ScanImages: false,
            ScanAudio: false,
            ScanBooks: false,
            IsNsfw: false,
            LastScannedAt: lastScannedAt,
            CreatedAt: now,
            UpdatedAt: now);
    }

    private sealed class SchedulerSettingsPersistence(
        IEnumerable<LibraryRoot> roots,
        bool collectionAutoRefreshEnabled = true,
        bool monitoringSearchEnabled = true) : ISettingsPersistence {
        private readonly List<LibraryRoot> _roots = roots.ToList();

        public IReadOnlyList<LibraryRoot> Roots => _roots;

        public Task<IReadOnlyDictionary<string, string>> LoadSettingOverridesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string> {
                [AppSettingKeys.ScanAutoScanEnabled] = JsonSerializer.Serialize(true),
                [AppSettingKeys.ScanIntervalMinutes] = JsonSerializer.Serialize(60),
                [AppSettingKeys.CollectionsAutoRefreshEnabled] = JsonSerializer.Serialize(collectionAutoRefreshEnabled),
                [AppSettingKeys.MonitoringSearchEnabled] = JsonSerializer.Serialize(monitoringSearchEnabled),
                [AppSettingKeys.MonitoringIntervalMinutes] = JsonSerializer.Serialize(360),
            });

        public Task SaveSettingOverrideAsync(string key, string valueJson, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task SaveSettingOverridesAsync(IReadOnlyDictionary<string, string> values, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task ReplaceSettingOverridesAsync(
            IReadOnlyDictionary<string, string> upserts,
            IReadOnlyCollection<string> deletes,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task DeleteSettingOverrideAsync(string key, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<LibraryRoot>> ListLibraryRootsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<LibraryRoot>>(_roots.ToArray());

        public Task<LibraryRoot?> GetLibraryRootAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(_roots.FirstOrDefault(root => root.Id == id));

        public Task<LibraryRoot> AddLibraryRootAsync(LibraryRoot state, CancellationToken cancellationToken) =>
            Task.FromResult(state);

        public Task<LibraryRoot> SaveLibraryRootAsync(LibraryRoot state, CancellationToken cancellationToken) {
            var index = _roots.FindIndex(root => root.Id == state.Id);
            if (index >= 0) {
                _roots[index] = state;
            }

            return Task.FromResult(state);
        }

        public Task<bool> DeleteLibraryRootAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(false);
    }

    private sealed class SchedulerJobQueue(bool hasPendingRefresh = false, bool hasPendingMonitoredSearch = false) : IJobQueueService {
        public List<EnqueueJobRequest> Enqueued { get; } = [];

        public Task<bool> HasPendingAsync(JobType type, string? targetEntityId, CancellationToken cancellationToken) =>
            Task.FromResult((type == JobType.RefreshCollection && hasPendingRefresh)
                || (type == JobType.MonitoredSearch && hasPendingMonitoredSearch));

        public Task<JobRunSnapshot> EnqueueAsync(EnqueueJobRequest request, CancellationToken cancellationToken) {
            Enqueued.Add(request);
            return Task.FromResult(NewSnapshot(request.Type));
        }

        public Task<JobRunSnapshot> EnqueueAsync(JobType type, CancellationToken cancellationToken) =>
            Task.FromResult(NewSnapshot(type));

        public Task<IReadOnlyList<JobRunSnapshot>> ListAsync(bool hideNsfw, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<JobRunSnapshot>>([]);

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

        public Task<IReadOnlyList<JobQueueCount>> GetQueueCountsAsync(bool hideNsfw, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<JobQueueCount>>([]);

        public Task<int> PruneHistoryAsync(TimeSpan retention, CancellationToken cancellationToken) =>
            Task.FromResult(0);

        private static JobRunSnapshot NewSnapshot(JobType type) {
            var now = DateTimeOffset.UtcNow;
            return new JobRunSnapshot(
                Guid.NewGuid(),
                type,
                JobRunStatus.Queued,
                Progress: 0,
                Message: null,
                PayloadJson: "{}",
                TargetEntityKind: null,
                TargetEntityId: null,
                TargetLabel: null,
                CreatedAt: now,
                StartedAt: null,
                FinishedAt: null);
        }
    }

    private sealed class SchedulerMonitorStore(bool hasActive) : IMonitorStore {
        public Task<bool> HasActiveMonitorsAsync(CancellationToken cancellationToken) => Task.FromResult(hasActive);
        public Task<MonitorView> StartAsync(Guid acquisitionId, EntityKind kind, string title, string? author, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(Guid monitorId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> SetStatusAsync(Guid monitorId, MonitorStatus status, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<MonitorView>> ListAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<WantedPage> ListMissingAsync(int page, int pageSize, EntityKind? kind, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<WantedPage> ListCutoffUnmetAsync(int page, int pageSize, EntityKind? kind, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<MonitorView?> GetByAcquisitionAsync(Guid acquisitionId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<MonitorView> StartForEntityAsync(Guid entityId, EntityKind kind, string title, AcquisitionTargeting? targeting, MonitorPreset? preset, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<MonitorView?> GetByEntityAsync(Guid entityId, CancellationToken cancellationToken) => Task.FromResult<MonitorView?>(null);
        public Task<AcquisitionTargeting?> GetTargetingByEntityAsync(Guid entityId, CancellationToken cancellationToken) => Task.FromResult<AcquisitionTargeting?>(null);
        public Task<MonitorPreset?> GetPresetByEntityAsync(Guid entityId, CancellationToken cancellationToken) => Task.FromResult<MonitorPreset?>(null);
        public Task<IReadOnlyList<DueMonitor>> ListDueMonitorsAsync(int defaultIntervalMinutes, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task MarkSearchedAsync(Guid monitorId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Guid?> CreateUpgradeChildAsync(Guid monitorId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ResolveUpgradeChildAsync(Guid childId, bool succeeded, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
