using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Jobs;
using Prismedia.Application.Jobs.Handlers;
using Prismedia.Application.Settings;
using Prismedia.Contracts.Acquisition;
using Prismedia.Contracts.Settings;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Acquisition;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class JobSchedulerTests {
    [Fact]
    public async Task ScheduleAcquisitionMonitorAsyncKeepsTransferPollingAheadOfSearchFanout() {
        await using var db = new PrismediaDbContext(
            new DbContextOptionsBuilder<PrismediaDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);
        var acquisitionId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.Acquisitions.Add(new AcquisitionRow {
            Id = acquisitionId,
            Status = AcquisitionStatus.Queued,
            Title = "Active download",
            ExternalIdsJson = "{}",
            SourceUrlsJson = "[]",
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.DownloadTransfers.Add(new DownloadTransferRow {
            Id = Guid.NewGuid(),
            AcquisitionId = acquisitionId,
            ClientItemId = "active-client-item",
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();

        var queue = new SchedulerJobQueue();
        await using var provider = CreateProvider(
            new SchedulerSettingsPersistence([]),
            queue,
            acquisitions: AcquisitionTestFactory.Store(db));
        var scheduler = CreateScheduler(provider, now);

        await scheduler.ScheduleAcquisitionMonitorAsync(CancellationToken.None);

        var request = Assert.Single(queue.Enqueued);
        Assert.Equal(JobType.AcquisitionMonitor, request.Type);
        Assert.Equal(JobGraphOrigin.Background, request.Origin);
    }

    [Fact]
    public async Task ScheduleRecurringScansAsyncSkipsRootScannedWithinInterval() {
        // Age-based due check: a root scanned half an interval ago is not due, regardless of
        // where "now" falls relative to interval boundaries (restarts can no longer skip cycles).
        var rootId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 5, 30, 10, 37, 0, TimeSpan.Zero);
        var settings = new SchedulerSettingsPersistence([
            CreateRoot(rootId, lastScannedAt: now.AddMinutes(-30))
        ]);
        var queue = new SchedulerJobQueue();
        await using var provider = CreateProvider(settings, queue);
        var scheduler = CreateScheduler(provider, now);

        await scheduler.ScheduleRecurringScansAsync(CancellationToken.None);

        Assert.Empty(queue.Enqueued);
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
        Assert.Equal(rootId.ToString(), request.TargetEntityId);
        Assert.Equal(JobResourceKeys.LibraryScan, request.ResourceKey);
        Assert.True(ScanRootPayload.TryParse(request.PayloadJson, out var payload));
        Assert.Equal(rootId, payload.RootId);
        Assert.Equal(triggeredAt, settings.Roots.Single().LastScannedAt);
    }

    [Fact]
    public async Task ScheduleRecurringScansEnqueuesOnlyEnabledKindsForEachDueRoot() {
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

        // One scan per enabled kind and exact root: video from the first, image + book from the second.
        var types = queue.Enqueued.Select(request => request.Type).ToHashSet();
        Assert.Equal(3, queue.Enqueued.Count);
        Assert.Contains(JobType.ScanLibrary, types);
        Assert.Contains(JobType.ScanGallery, types);
        Assert.Contains(JobType.ScanBook, types);
        Assert.DoesNotContain(JobType.ScanAudio, types);
        Assert.Single(queue.Enqueued, request => request.TargetEntityId == videoRoot.Id.ToString());
        Assert.Equal(2, queue.Enqueued.Count(request => request.TargetEntityId == imageBookRoot.Id.ToString()));
        Assert.All(queue.Enqueued, request => Assert.Equal(JobResourceKeys.LibraryScan, request.ResourceKey));
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
    public async Task SchedulePluginUpdatesAsyncQueuesOnWorkerStartupByDefault() {
        var settings = new SchedulerSettingsPersistence([]);
        var queue = new SchedulerJobQueue();
        await using var provider = CreateProvider(settings, queue);
        var scheduler = CreateScheduler(provider, new DateTimeOffset(2026, 5, 30, 10, 37, 0, TimeSpan.Zero));

        await scheduler.SchedulePluginUpdatesAsync(CancellationToken.None);

        var request = Assert.Single(queue.Enqueued);
        Assert.Equal(JobType.UpdatePlugins, request.Type);
        Assert.Equal("Automatic plugin updates", request.TargetLabel);
    }

    [Fact]
    public async Task SchedulePluginUpdatesAsyncSkipsWhenDisabled() {
        var settings = new SchedulerSettingsPersistence([], pluginAutoUpdateEnabled: false);
        var queue = new SchedulerJobQueue();
        await using var provider = CreateProvider(settings, queue);
        var scheduler = CreateScheduler(provider, new DateTimeOffset(2026, 5, 30, 10, 37, 0, TimeSpan.Zero));

        await scheduler.SchedulePluginUpdatesAsync(CancellationToken.None);

        Assert.Empty(queue.Enqueued);
    }

    [Fact]
    public async Task SchedulePluginUpdatesAsyncQueuesAgainOnSixHourBoundary() {
        var settings = new SchedulerSettingsPersistence([]);
        var queue = new SchedulerJobQueue();
        await using var provider = CreateProvider(settings, queue);
        var time = new MutableTimeProvider(new DateTimeOffset(2026, 5, 30, 10, 37, 0, TimeSpan.Zero));
        var scheduler = CreateScheduler(provider, time);

        await scheduler.SchedulePluginUpdatesAsync(CancellationToken.None);
        queue.ClearPending(JobType.UpdatePlugins);

        time.UtcNow = new DateTimeOffset(2026, 5, 30, 11, 12, 0, TimeSpan.Zero);
        await scheduler.SchedulePluginUpdatesAsync(CancellationToken.None);
        Assert.Single(queue.Enqueued);

        time.UtcNow = new DateTimeOffset(2026, 5, 30, 12, 0, 15, TimeSpan.Zero);
        await scheduler.SchedulePluginUpdatesAsync(CancellationToken.None);

        Assert.Equal(2, queue.Enqueued.Count);
        Assert.All(queue.Enqueued, request => Assert.Equal(JobType.UpdatePlugins, request.Type));
    }

    [Fact]
    public async Task RecoverDownloadedCompletionJobsRoutesOrdinaryAndUpgradeWorkIdempotently() {
        var ordinaryId = Guid.NewGuid();
        var upgradeId = Guid.NewGuid();
        var audiobookUpgradeId = Guid.NewGuid();
        var queue = new SchedulerJobQueue();
        var acquisitions = new SchedulerAcquisitionLifecycleStore([
            new DownloadedAcquisitionCompletion(ordinaryId, EntityKind.Movie, IsUpgrade: false),
            new DownloadedAcquisitionCompletion(upgradeId, EntityKind.Book, IsUpgrade: true),
            new DownloadedAcquisitionCompletion(
                audiobookUpgradeId,
                EntityKind.Book,
                IsUpgrade: true,
                BookRendition.Audiobook),
        ]);
        await using var provider = CreateProvider(
            new SchedulerSettingsPersistence([]),
            queue,
            acquisitions: acquisitions,
            importEngines: new SchedulerImportEngineFactory([EntityKind.Movie, EntityKind.Book]));
        var scheduler = CreateScheduler(provider, DateTimeOffset.UtcNow);

        await scheduler.RecoverDownloadedCompletionJobsAsync(CancellationToken.None);
        await scheduler.RecoverDownloadedCompletionJobsAsync(CancellationToken.None);

        Assert.Equal(3, queue.Enqueued.Count);
        AssertCompletionRequest(
            Assert.Single(queue.Enqueued, request => request.TargetEntityId == ordinaryId.ToString()),
            ordinaryId,
            JobType.AcquisitionImport);
        AssertCompletionRequest(
            Assert.Single(queue.Enqueued, request => request.TargetEntityId == upgradeId.ToString()),
            upgradeId,
            JobType.AcquisitionUpgradeReplace);
        AssertCompletionRequest(
            Assert.Single(queue.Enqueued, request => request.TargetEntityId == audiobookUpgradeId.ToString()),
            audiobookUpgradeId,
            JobType.AcquisitionImport);
    }

    [Fact]
    public async Task RecoverDownloadedCompletionJobsSkipsOrdinaryKindsWithoutAnImportEngine() {
        var unsupportedId = Guid.NewGuid();
        var queue = new SchedulerJobQueue();
        var acquisitions = new SchedulerAcquisitionLifecycleStore([
            new DownloadedAcquisitionCompletion(unsupportedId, EntityKind.Image, IsUpgrade: false),
        ]);
        await using var provider = CreateProvider(
            new SchedulerSettingsPersistence([]),
            queue,
            acquisitions: acquisitions,
            importEngines: new SchedulerImportEngineFactory([]));
        var scheduler = CreateScheduler(provider, DateTimeOffset.UtcNow);

        await scheduler.RecoverDownloadedCompletionJobsAsync(CancellationToken.None);

        Assert.Empty(queue.Enqueued);
    }

    [Fact]
    public async Task RecoverDownloadedCompletionJobsRetriesAfterAQueueFailure() {
        var acquisitionId = Guid.NewGuid();
        var queue = new SchedulerJobQueue { EnqueueFailuresRemaining = 1 };
        var acquisitions = new SchedulerAcquisitionLifecycleStore([
            new DownloadedAcquisitionCompletion(acquisitionId, EntityKind.Movie, IsUpgrade: false),
        ]);
        await using var provider = CreateProvider(
            new SchedulerSettingsPersistence([]),
            queue,
            acquisitions: acquisitions,
            importEngines: new SchedulerImportEngineFactory([EntityKind.Movie]));
        var scheduler = CreateScheduler(provider, DateTimeOffset.UtcNow);

        await Assert.ThrowsAsync<IOException>(() =>
            scheduler.RecoverDownloadedCompletionJobsAsync(CancellationToken.None));
        Assert.Empty(queue.Enqueued);

        await scheduler.RecoverDownloadedCompletionJobsAsync(CancellationToken.None);

        Assert.Equal(JobType.AcquisitionImport, Assert.Single(queue.Enqueued).Type);
    }

    private static void AssertCompletionRequest(
        EnqueueJobRequest request,
        Guid acquisitionId,
        JobType expectedType) {
        Assert.Equal(expectedType, request.Type);
        Assert.Equal(acquisitionId.ToString(), request.TargetEntityId);
        Assert.Equal(acquisitionId, AcquisitionJobPayload.Parse(request.PayloadJson!).AcquisitionId);
        Assert.Equal(JobGraphOrigin.Background, request.Origin);
    }

    private static ServiceProvider CreateProvider(
        ISettingsPersistence settings,
        IJobQueueService queue,
        IAcquisitionLifecycleStore? acquisitions = null,
        IAcquisitionImportEngineFactory? importEngines = null) {
        var services = new ServiceCollection();
        services.AddSingleton(settings);
        services.AddScoped<SettingsService>();
        services.AddSingleton(queue);
        var lifecycle = acquisitions ?? new SchedulerAcquisitionLifecycleStore([]);
        services.AddSingleton(lifecycle);
        if (lifecycle is IAcquisitionStore store) {
            services.AddSingleton(store);
        }
        services.AddSingleton(importEngines ?? new SchedulerImportEngineFactory([]));
        return services.BuildServiceProvider();
    }

    private static JobScheduler CreateScheduler(ServiceProvider provider, DateTimeOffset now) =>
        new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<JobScheduler>.Instance,
            new FixedTimeProvider(now));

    private static JobScheduler CreateScheduler(ServiceProvider provider, TimeProvider timeProvider) =>
        new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<JobScheduler>.Instance,
            timeProvider);

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
        bool pluginAutoUpdateEnabled = true) : ISettingsPersistence {
        private readonly List<LibraryRoot> _roots = roots.ToList();

        public IReadOnlyList<LibraryRoot> Roots => _roots;

        public Task<IReadOnlyDictionary<string, string>> LoadSettingOverridesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string> {
                [AppSettings.Scan.AutoScanEnabled.Key] = JsonSerializer.Serialize(true),
                [AppSettings.Scan.IntervalMinutes.Key] = JsonSerializer.Serialize(60),
                [AppSettings.Collections.AutoRefreshEnabled.Key] = JsonSerializer.Serialize(collectionAutoRefreshEnabled),
                [AppSettings.Plugins.AutoUpdateEnabled.Key] = JsonSerializer.Serialize(pluginAutoUpdateEnabled),
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

    private sealed class SchedulerJobQueue(bool hasPendingRefresh = false) : IJobQueueService {
        private readonly HashSet<(JobType Type, string? TargetEntityId)> _pending = [];

        public List<EnqueueJobRequest> Enqueued { get; } = [];
        public int EnqueueFailuresRemaining { get; set; }

        public void ClearPending(JobType type) => _pending.Remove((type, null));

        public Task<bool> HasPendingAsync(JobType type, string? targetEntityId, CancellationToken cancellationToken) =>
            Task.FromResult((type == JobType.RefreshCollection && hasPendingRefresh)
                || _pending.Contains((type, targetEntityId)));

        public Task<JobRunSnapshot> EnqueueAsync(EnqueueJobRequest request, CancellationToken cancellationToken) {
            if (EnqueueFailuresRemaining > 0) {
                EnqueueFailuresRemaining--;
                throw new IOException("queue unavailable");
            }

            if (_pending.Add((request.Type, request.TargetEntityId))) {
                Enqueued.Add(request);
            }
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

    private sealed class SchedulerAcquisitionLifecycleStore(
        IReadOnlyList<DownloadedAcquisitionCompletion> downloaded) : IAcquisitionLifecycleStore {
        public Task<IReadOnlyList<DownloadedAcquisitionCompletion>> ListDownloadedCompletionWorkAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(downloaded);

        public Task<AcquisitionStatus?> GetStatusAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> TryTransitionStatusAsync(
            Guid id,
            IReadOnlyCollection<AcquisitionStatus> expectedStatuses,
            AcquisitionStatus status,
            string? message,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> TryClaimFailedRecoveryAsync(
            Guid id,
            IReadOnlyCollection<AcquisitionStatus> expectedStatuses,
            SelectedRelease? expectedSelectedRelease,
            string message,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class SchedulerImportEngineFactory(
        IReadOnlyCollection<EntityKind> supportedKinds) : IAcquisitionImportEngineFactory {
        private readonly IReadOnlyDictionary<EntityKind, IAcquisitionImportEngine> _engines = supportedKinds
            .Distinct()
            .ToDictionary(kind => kind, kind => (IAcquisitionImportEngine)new SchedulerImportEngine(kind));

        public IAcquisitionImportEngine? Find(EntityKind kind) => _engines.GetValueOrDefault(kind);
    }

    private sealed class SchedulerImportEngine(EntityKind kind) : IAcquisitionImportEngine {
        public EntityKind Kind { get; } = kind;

        public Task ImportAsync(
            JobContext context,
            AcquisitionImportContext import,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider {
        public DateTimeOffset UtcNow { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
