using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Jobs;
using Prismedia.Application.Jobs.Handlers;
using Prismedia.Contracts.Acquisition;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Acquisition;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

/// <summary>
/// Verifies the monitor's vanished-download handling: a download truly gone from the client past the
/// grace window falls back to the searching state (no blocklist, no Failed — the user removing a torrent
/// is not a release failure), while a healthy torrent whose category drifted is never touched. Also
/// covers stalled downloads: a stall is anchored on first observation and abandoned to failed-recovery
/// only after it persists past the grace window, while a recovered transfer clears its anchor.
/// </summary>
public sealed class AcquisitionMonitorJobHandlerTests {
    private static readonly Guid ClientId = Guid.NewGuid();

    [Fact]
    public async Task StalledPastGraceEnqueuesFailedHandleWithStalledReason() {
        await using var db = CreateContext();
        var acquisitionId = await SeedDownloadingAsync(db, lastSeen: DateTimeOffset.UtcNow, stalledSince: DateTimeOffset.UtcNow.AddMinutes(-90));
        var queue = new RecordingJobQueue();

        // The torrent is present in the listing but the client reports it stalled, and it has been stalled
        // for longer than the grace window → abandon it to recovery, blocklisting it as stalled.
        var stalled = new DownloadItemStatus("hashX", "Book", 0.5, "stalledDL", IsComplete: false, "/save", "/save/book", IsStalled: true);
        await RunAsync(db, queue, listing: [stalled], directLookup: null, acquisitionId);

        var enqueued = Assert.Single(queue.Enqueued);
        Assert.Equal(JobType.AcquisitionFailedHandle, enqueued.Type);
        var payload = AcquisitionFailedPayload.Parse(enqueued.PayloadJson!);
        Assert.Equal(BlocklistReason.Stalled, payload.Reason);
        Assert.Equal("hashX", payload.Selected!.InfoHash);
        // The monitor does NOT set Failed; the recovery job owns that transition.
        Assert.Equal(AcquisitionStatus.Downloading, await StatusOf(db, acquisitionId));
    }

    [Fact]
    public async Task StalledAbandonmentEnqueuesAtMostOnceAcrossPasses() {
        await using var db = CreateContext();
        var acquisitionId = await SeedDownloadingAsync(db, lastSeen: DateTimeOffset.UtcNow, stalledSince: DateTimeOffset.UtcNow.AddMinutes(-90));
        var queue = new RecordingJobQueue();
        var stalled = new DownloadItemStatus("hashX", "Book", 0.5, "stalledDL", IsComplete: false, "/save", "/save/book", IsStalled: true);

        // Two monitor passes while the torrent stays stalled and the acquisition is still Downloading (the
        // recovery job hasn't run yet). Abandonment must be enqueued exactly once: the first pass clears the
        // stall anchor (so it isn't re-evaluated on a stale anchor) and the queue dedups by target.
        await RunAsync(db, queue, listing: [stalled], directLookup: null, acquisitionId);
        await RunAsync(db, queue, listing: [stalled], directLookup: null, acquisitionId);

        var enqueued = Assert.Single(queue.Enqueued);
        Assert.Equal(JobType.AcquisitionFailedHandle, enqueued.Type);
        Assert.Equal(BlocklistReason.Stalled, AcquisitionFailedPayload.Parse(enqueued.PayloadJson!).Reason);
    }

    [Fact]
    public async Task FirstStallObservationAnchorsWithoutAbandoning() {
        await using var db = CreateContext();
        var acquisitionId = await SeedDownloadingAsync(db, lastSeen: DateTimeOffset.UtcNow, stalledSince: null);
        var queue = new RecordingJobQueue();

        var stalled = new DownloadItemStatus("hashX", "Book", 0.5, "stalledDL", IsComplete: false, "/save", "/save/book", IsStalled: true);
        await RunAsync(db, queue, listing: [stalled], directLookup: null, acquisitionId);

        // First time seen stalled: anchor the stall, but don't abandon yet (it may recover).
        Assert.Empty(queue.Enqueued);
        Assert.NotNull(await StalledSinceOf(db, acquisitionId));
        Assert.Equal(AcquisitionStatus.Downloading, await StatusOf(db, acquisitionId));
    }

    [Fact]
    public async Task RecoveredTransferClearsStallAnchor() {
        await using var db = CreateContext();
        var acquisitionId = await SeedDownloadingAsync(db, lastSeen: DateTimeOffset.UtcNow, stalledSince: DateTimeOffset.UtcNow.AddMinutes(-90));
        var queue = new RecordingJobQueue();

        // Was stalled long enough to abandon, but it's downloading healthily again this pass → clear the anchor.
        var healthy = new DownloadItemStatus("hashX", "Book", 0.7, "downloading", IsComplete: false, "/save", "/save/book");
        await RunAsync(db, queue, listing: [healthy], directLookup: null, acquisitionId);

        Assert.Empty(queue.Enqueued);
        Assert.Null(await StalledSinceOf(db, acquisitionId));
        Assert.Equal(AcquisitionStatus.Downloading, await StatusOf(db, acquisitionId));
    }

    [Fact]
    public async Task StalledStateButProgressingIsNotAbandoned() {
        await using var db = CreateContext();
        // Anchored well past the grace window, yet the torrent is still inching along.
        var acquisitionId = await SeedDownloadingAsync(db, lastSeen: DateTimeOffset.UtcNow, stalledSince: DateTimeOffset.UtcNow.AddMinutes(-90));
        var queue = new RecordingJobQueue();

        // The client labels it stalled this instant, but cumulative progress rose from the seeded 0.5 → 0.7:
        // it's alive (just slow), so it must not be abandoned, and the stale anchor is cleared.
        var creeping = new DownloadItemStatus("hashX", "Book", 0.7, "stalledDL", IsComplete: false, "/save", "/save/book", IsStalled: true);
        await RunAsync(db, queue, listing: [creeping], directLookup: null, acquisitionId);

        Assert.Empty(queue.Enqueued);
        Assert.Null(await StalledSinceOf(db, acquisitionId));
        Assert.Equal(AcquisitionStatus.Downloading, await StatusOf(db, acquisitionId));
    }

    [Fact]
    public async Task VanishedPastGraceFallsBackToSearching() {
        await using var db = CreateContext();
        var acquisitionId = await SeedDownloadingAsync(db, lastSeen: DateTimeOffset.UtcNow.AddMinutes(-10));
        var queue = new RecordingJobQueue();

        // Absent from the category listing AND absent on a per-hash lookup → genuinely gone (e.g. the
        // user deleted the torrent in the client). The acquisition must not sit orphaned in Downloading,
        // and the removed release must not be blocklisted — it falls back to a fresh search instead.
        await RunAsync(db, queue, listing: [], directLookup: null, acquisitionId);

        var enqueued = Assert.Single(queue.Enqueued);
        Assert.Equal(JobType.AcquisitionSearch, enqueued.Type);
        Assert.Equal(acquisitionId.ToString(), enqueued.TargetEntityId);
        Assert.Equal(AcquisitionStatus.Searching, await StatusOf(db, acquisitionId));

        // Durable record of what happened, so the History surface explains the restart.
        var events = await new EfAcquisitionHistoryStore(db).ListAsync(200, entityId: null, CancellationToken.None);
        var entry = Assert.Single(events, item => item.Event == AcquisitionHistoryEvent.DownloadFailed);
        Assert.Contains("removed from the client", entry.Message ?? "");
    }

    [Fact]
    public async Task VanishedFallbackFiresOnceAcrossPasses() {
        await using var db = CreateContext();
        var acquisitionId = await SeedDownloadingAsync(db, lastSeen: DateTimeOffset.UtcNow.AddMinutes(-10));
        var queue = new RecordingJobQueue();

        // The first pass flips the acquisition to Searching, which retires the orphaned transfer from the
        // active set — a second pass must find nothing to advance and enqueue nothing new.
        await RunAsync(db, queue, listing: [], directLookup: null, acquisitionId);
        await RunAsync(db, queue, listing: [], directLookup: null, acquisitionId);

        var enqueued = Assert.Single(queue.Enqueued);
        Assert.Equal(JobType.AcquisitionSearch, enqueued.Type);
        Assert.Equal(AcquisitionStatus.Searching, await StatusOf(db, acquisitionId));
    }

    [Fact]
    public async Task RecategorizedButHealthyTorrentIsNotFailedOrEnqueued() {
        await using var db = CreateContext();
        var acquisitionId = await SeedDownloadingAsync(db, lastSeen: DateTimeOffset.UtcNow.AddMinutes(-10));
        var queue = new RecordingJobQueue();

        // Absent from the category listing but the per-hash lookup finds it (category drifted) → still healthy.
        var present = new DownloadItemStatus("hashX", "Book", 0.6, "downloading", IsComplete: false, "/save", "/save/book");
        await RunAsync(db, queue, listing: [], directLookup: present, acquisitionId);

        Assert.Empty(queue.Enqueued);
        Assert.Equal(AcquisitionStatus.Downloading, await StatusOf(db, acquisitionId));
        var progress = await db.DownloadTransfers.AsNoTracking().Where(t => t.AcquisitionId == acquisitionId).Select(t => t.Progress).FirstAsync();
        Assert.Equal(0.6, progress);
    }

    [Fact]
    public async Task VanishedWithinGraceDoesNothing() {
        await using var db = CreateContext();
        var acquisitionId = await SeedDownloadingAsync(db, lastSeen: DateTimeOffset.UtcNow);
        var queue = new RecordingJobQueue();

        await RunAsync(db, queue, listing: [], directLookup: null, acquisitionId);

        Assert.Empty(queue.Enqueued);
        Assert.Equal(AcquisitionStatus.Downloading, await StatusOf(db, acquisitionId));
    }

    private static async Task RunAsync(
        PrismediaDbContext db, RecordingJobQueue queue, IReadOnlyList<DownloadItemStatus> listing, DownloadItemStatus? directLookup, Guid acquisitionId) {
        var handler = new AcquisitionMonitorJobHandler(
            AcquisitionTestFactory.Store(db),
            new FakeDownloadClientConfigStore(),
            new FakeDownloadClientFactory(new FakeDownloadClient(listing, directLookup)),
            new RemotePathMapper(new NoRemotePathMappings()),
            new EfAcquisitionHistoryStore(db),
            NullLogger<AcquisitionMonitorJobHandler>.Instance);
        var job = new JobRunSnapshot(
            Guid.NewGuid(), JobType.AcquisitionMonitor, JobRunStatus.Running, 0, null, "{}",
            null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null);
        await handler.HandleAsync(new JobContext(job, queue), CancellationToken.None);
    }

    private static async Task<Guid> SeedDownloadingAsync(PrismediaDbContext db, DateTimeOffset lastSeen, DateTimeOffset? stalledSince = null) {
        var acquisitionId = Guid.NewGuid();
        db.Acquisitions.Add(new AcquisitionRow {
            Id = acquisitionId, Status = AcquisitionStatus.Downloading, Title = "Book",
            ExternalIdsJson = "{}", SourceUrlsJson = "[]", CreatedAt = lastSeen, UpdatedAt = lastSeen
        });
        db.DownloadTransfers.Add(new DownloadTransferRow {
            Id = Guid.NewGuid(), AcquisitionId = acquisitionId, DownloadClientConfigId = ClientId,
            ClientItemId = "hashX", Progress = 0.5, StalledSince = stalledSince, CreatedAt = lastSeen, UpdatedAt = lastSeen
        });
        await db.SaveChangesAsync();
        await AcquisitionTestFactory.Store(db).SetSelectedReleaseAsync(
            acquisitionId, new SelectedRelease("Book (epub)", "Indexer", "hashX"), CancellationToken.None);
        return acquisitionId;
    }

    private static async Task<AcquisitionStatus> StatusOf(PrismediaDbContext db, Guid acquisitionId) =>
        await db.Acquisitions.AsNoTracking().Where(row => row.Id == acquisitionId).Select(row => row.Status).FirstAsync();

    private static async Task<DateTimeOffset?> StalledSinceOf(PrismediaDbContext db, Guid acquisitionId) =>
        await db.DownloadTransfers.AsNoTracking().Where(row => row.AcquisitionId == acquisitionId).Select(row => row.StalledSince).FirstAsync();

    private static PrismediaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class FakeDownloadClient(IReadOnlyList<DownloadItemStatus> listing, DownloadItemStatus? directLookup) : IDownloadClient {
        public DownloadClientKind Kind => DownloadClientKind.QBittorrent;
        public Task<IReadOnlyList<DownloadItemStatus>> ListItemsAsync(DownloadClientConnection connection, CancellationToken cancellationToken) =>
            Task.FromResult(listing);
        public Task<DownloadItemStatus?> GetItemAsync(DownloadClientConnection connection, string clientItemId, CancellationToken cancellationToken) =>
            Task.FromResult(directLookup);
        public Task<string> AddAsync(DownloadClientConnection connection, DownloadAddRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string> AddTorrentFileAsync(DownloadClientConnection connection, string fileName, byte[] torrent, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<DownloadItemFile>> GetFilesAsync(DownloadClientConnection connection, string clientItemId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<DownloadItemProperties?> GetPropertiesAsync(DownloadClientConnection connection, string clientItemId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<byte[]> GetPieceStatesAsync(DownloadClientConnection connection, string clientItemId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task RemoveAsync(DownloadClientConnection connection, string clientItemId, bool deleteData, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<DownloadClientConnectionTest> TestAsync(DownloadClientConnection connection, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeDownloadClientFactory(IDownloadClient client) : IDownloadClientFactory {
        public IDownloadClient Get(DownloadClientKind kind) => client;
    }

    private sealed class FakeDownloadClientConfigStore : IDownloadClientConfigStore {
        private static readonly DownloadClientDetail Detail =
            new(ClientId, DownloadClientKind.QBittorrent, "qbit", "http://x", null, "prismedia-books", true, false, null);
        public Task<DownloadClientDetail?> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<DownloadClientDetail?>(Detail);
        public Task<DownloadClientDetail?> GetDefaultAsync(CancellationToken cancellationToken) => Task.FromResult<DownloadClientDetail?>(Detail);
        public Task<DownloadClientDetail?> GetDefaultAsync(Prismedia.Domain.Entities.DownloadProtocol protocol, CancellationToken cancellationToken) => GetDefaultAsync(cancellationToken);
        public Task<IReadOnlyList<DownloadClientDetail>> ListEnabledAsync(Prismedia.Domain.Entities.DownloadProtocol protocol, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<Prismedia.Domain.Entities.DownloadProtocol>> GetEnabledProtocolsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Prismedia.Domain.Entities.DownloadProtocol>>([Prismedia.Domain.Entities.DownloadProtocol.Torrent]);
        public Task<IReadOnlyList<DownloadClientSummary>> ListAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<DownloadClientDetail>> ListDetailsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<DownloadClientSummary> SaveAsync(DownloadClientSaveCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class NoRemotePathMappings : IRemotePathMappingStore {
        public Task<IReadOnlyList<Prismedia.Contracts.Acquisition.RemotePathMappingView>> ListForClientAsync(Guid downloadClientConfigId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Prismedia.Contracts.Acquisition.RemotePathMappingView>>([]);
        public Task<IReadOnlyList<Prismedia.Contracts.Acquisition.RemotePathMappingView>> ListAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Prismedia.Contracts.Acquisition.RemotePathMappingView> SaveAsync(Prismedia.Contracts.Acquisition.RemotePathMappingSaveRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RecordingJobQueue : IJobQueueService {
        public List<EnqueueJobRequest> Enqueued { get; } = [];

        public Task<JobRunSnapshot> EnqueueAsync(EnqueueJobRequest request, CancellationToken cancellationToken) {
            Enqueued.Add(request);
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(new JobRunSnapshot(
                Guid.NewGuid(), request.Type, JobRunStatus.Queued, 0, null, request.PayloadJson ?? "{}",
                request.TargetEntityKind, request.TargetEntityId, request.TargetLabel, now, null, null));
        }

        // Mirrors the real queue's dedup-by-target: a job already enqueued for the same (type, target) is
        // pending, so EnqueueIfNeededAsync skips a duplicate. Lets multi-pass tests exercise dedup faithfully.
        public Task<bool> HasPendingAsync(JobType type, string? targetEntityId, CancellationToken cancellationToken) =>
            Task.FromResult(Enqueued.Any(request => request.Type == type && request.TargetEntityId == targetEntityId));
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
