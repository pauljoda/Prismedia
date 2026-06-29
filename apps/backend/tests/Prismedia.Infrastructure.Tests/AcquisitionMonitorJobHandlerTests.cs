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
/// Verifies the monitor's vanished-download handling: it hands off to failed-recovery only when the
/// torrent is truly gone past the grace window, does NOT fail a healthy torrent whose category drifted,
/// and leaves the terminal Failed transition to the recovery job (it only enqueues).
/// </summary>
public sealed class AcquisitionMonitorJobHandlerTests {
    private static readonly Guid ClientId = Guid.NewGuid();

    [Fact]
    public async Task VanishedPastGraceEnqueuesFailedHandleWithoutSettingFailed() {
        await using var db = CreateContext();
        var acquisitionId = await SeedDownloadingAsync(db, lastSeen: DateTimeOffset.UtcNow.AddMinutes(-10));
        var queue = new RecordingJobQueue();

        // Absent from the category listing AND absent on a per-hash lookup → genuinely gone.
        await RunAsync(db, queue, listing: [], directLookup: null, acquisitionId);

        var enqueued = Assert.Single(queue.Enqueued);
        Assert.Equal(JobType.AcquisitionFailedHandle, enqueued.Type);
        Assert.Equal(acquisitionId.ToString(), enqueued.TargetEntityId);
        var payload = AcquisitionFailedPayload.Parse(enqueued.PayloadJson!);
        Assert.Equal(BlocklistReason.Failed, payload.Reason);
        Assert.Equal("hashX", payload.Selected!.InfoHash);
        // The monitor does NOT set Failed; the recovery job owns that transition.
        Assert.Equal(AcquisitionStatus.Downloading, await StatusOf(db, acquisitionId));
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
            new EfAcquisitionStore(db),
            new FakeDownloadClientConfigStore(),
            new FakeDownloadClientFactory(new FakeDownloadClient(listing, directLookup)),
            NullLogger<AcquisitionMonitorJobHandler>.Instance);
        var job = new JobRunSnapshot(
            Guid.NewGuid(), JobType.AcquisitionMonitor, JobRunStatus.Running, 0, null, "{}",
            null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null);
        await handler.HandleAsync(new JobContext(job, queue), CancellationToken.None);
    }

    private static async Task<Guid> SeedDownloadingAsync(PrismediaDbContext db, DateTimeOffset lastSeen) {
        var acquisitionId = Guid.NewGuid();
        db.Acquisitions.Add(new AcquisitionRow {
            Id = acquisitionId, Status = AcquisitionStatus.Downloading, Title = "Book",
            ExternalIdsJson = "{}", SourceUrlsJson = "[]", CreatedAt = lastSeen, UpdatedAt = lastSeen
        });
        db.DownloadTransfers.Add(new DownloadTransferRow {
            Id = Guid.NewGuid(), AcquisitionId = acquisitionId, DownloadClientConfigId = ClientId,
            ClientItemId = "hashX", Progress = 0.5, CreatedAt = lastSeen, UpdatedAt = lastSeen
        });
        await db.SaveChangesAsync();
        await new EfAcquisitionStore(db).SetSelectedReleaseAsync(
            acquisitionId, new SelectedRelease("Book (epub)", "Indexer", "hashX"), CancellationToken.None);
        return acquisitionId;
    }

    private static async Task<AcquisitionStatus> StatusOf(PrismediaDbContext db, Guid acquisitionId) =>
        await db.Acquisitions.AsNoTracking().Where(row => row.Id == acquisitionId).Select(row => row.Status).FirstAsync();

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
        public Task<IReadOnlyList<DownloadClientSummary>> ListAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<DownloadClientDetail>> ListDetailsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<DownloadClientSummary> SaveAsync(DownloadClientSaveCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
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

        public Task<bool> HasPendingAsync(JobType type, string? targetEntityId, CancellationToken cancellationToken) => Task.FromResult(false);
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
