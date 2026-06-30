using System.Text.Json;
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
/// Covers the upgrade-replace orchestration: a successful swap records the new owned quality, releases the
/// monitor's slot counting the attempt, removes the consumed child, and enqueues a re-scan; an aborted swap
/// (e.g. no longer an upgrade, or the replacer refused) leaves the owned book untouched and counts barren.
/// </summary>
public sealed class AcquisitionUpgradeReplaceJobHandlerTests {
    [Fact]
    public async Task SuccessfulSwapUpdatesOwnedQualityAndConsumesTheChild() {
        await using var db = CreateContext();
        var (parentId, childId, monitorId) = await SeedAsync(db, childSelectedTitle: "Some Book (retail) (epub)");
        var queue = new RecordingJobQueue();

        await RunAsync(db, queue, new FakeReplacer(OwnedFileReplaceResult.Ok("/library/Some Book/Book.epub", BookFormatTier.Reflowable)), childId);

        var parent = await db.Acquisitions.AsNoTracking().FirstAsync(a => a.Id == parentId);
        Assert.Equal(BookSourceTier.Retail, parent.OwnedSourceTier); // upgraded source recorded
        Assert.Equal(BookFormatTier.Reflowable, parent.OwnedFormatTier);
        Assert.False(await db.Acquisitions.AsNoTracking().AnyAsync(a => a.Id == childId)); // child consumed
        var monitor = await db.Monitors.AsNoTracking().FirstAsync(m => m.Id == monitorId);
        Assert.Null(monitor.UpgradeChildAcquisitionId);
        Assert.Equal(1, monitor.UpgradeAttempts);
        Assert.Contains(queue.Enqueued, job => job.Type == JobType.ScanBook);
    }

    [Fact]
    public async Task ReplacerRefusalAbortsAndCountsBarrenLeavingOwnedUntouched() {
        await using var db = CreateContext();
        var (parentId, childId, monitorId) = await SeedAsync(db, childSelectedTitle: "Some Book (retail) (epub)");
        var queue = new RecordingJobQueue();

        await RunAsync(db, queue, new FakeReplacer(OwnedFileReplaceResult.Failed("format change needs manual replacement")), childId);

        var parent = await db.Acquisitions.AsNoTracking().FirstAsync(a => a.Id == parentId);
        Assert.Equal(BookSourceTier.Web, parent.OwnedSourceTier); // unchanged — owned book untouched
        Assert.Equal(AcquisitionStatus.Failed, (await db.Acquisitions.AsNoTracking().FirstAsync(a => a.Id == childId)).Status);
        var monitor = await db.Monitors.AsNoTracking().FirstAsync(m => m.Id == monitorId);
        Assert.Null(monitor.UpgradeChildAcquisitionId);
        Assert.Equal(1, monitor.BarrenSearches);
        Assert.DoesNotContain(queue.Enqueued, job => job.Type == JobType.ScanBook);
    }

    [Fact]
    public async Task NoLongerAnUpgradeAbortsBeforeTouchingFiles() {
        await using var db = CreateContext();
        // The child's release is only equal to (not better than) the owned quality → must not swap.
        var (_, childId, _) = await SeedAsync(db, childSelectedTitle: "Some Book (web) (epub)");
        var queue = new RecordingJobQueue();
        var replacer = new FakeReplacer(OwnedFileReplaceResult.Ok("x", BookFormatTier.Reflowable));

        await RunAsync(db, queue, replacer, childId);

        Assert.False(replacer.Called); // bailed before invoking the destructive swap
    }

    private static async Task RunAsync(PrismediaDbContext db, RecordingJobQueue queue, FakeReplacer replacer, Guid childId) {
        var handler = new AcquisitionUpgradeReplaceJobHandler(
            new EfAcquisitionStore(db), new EfMonitorStore(db), replacer,
            new NullDownloadClientConfigStore(), new ThrowingDownloadClientFactory(),
            NullLogger<AcquisitionUpgradeReplaceJobHandler>.Instance);
        var job = new JobRunSnapshot(
            Guid.NewGuid(), JobType.AcquisitionUpgradeReplace, JobRunStatus.Running, 0, null,
            AcquisitionJobPayload.Serialize(childId), null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null);
        await handler.HandleAsync(new JobContext(job, queue), CancellationToken.None);
    }

    private static async Task<(Guid ParentId, Guid ChildId, Guid MonitorId)> SeedAsync(PrismediaDbContext db, string childSelectedTitle) {
        var now = DateTimeOffset.UtcNow;
        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        db.Acquisitions.Add(new AcquisitionRow {
            Id = parentId, Status = AcquisitionStatus.Imported, Title = "Some Book", ExternalIdsJson = "{}", SourceUrlsJson = "[]",
            FinalSourcePath = "/library/Some Book", OwnedSourceTier = BookSourceTier.Web, OwnedFormatTier = BookFormatTier.Reflowable,
            UpgradeQualityCaptured = true, CreatedAt = now, UpdatedAt = now
        });
        db.Acquisitions.Add(new AcquisitionRow {
            Id = childId, Status = AcquisitionStatus.Downloaded, Title = "Some Book", ExternalIdsJson = "{}", SourceUrlsJson = "[]",
            UpgradeOfAcquisitionId = parentId, SelectedReleaseJson = JsonSerializer.Serialize(new SelectedRelease(childSelectedTitle, "Indexer", "hash")),
            CreatedAt = now, UpdatedAt = now
        });
        db.DownloadTransfers.Add(new DownloadTransferRow {
            Id = Guid.NewGuid(), AcquisitionId = childId, ClientItemId = "hash", ContentPath = "/downloads/Some Book", Progress = 1, CreatedAt = now, UpdatedAt = now
        });
        var monitorId = Guid.NewGuid();
        db.Monitors.Add(new MonitorRow {
            Id = monitorId, Kind = EntityKind.Book, AcquisitionId = parentId, Status = MonitorStatus.Active, Title = "Some Book",
            UpgradeChildAcquisitionId = childId, CreatedAt = now, UpdatedAt = now
        });
        await db.SaveChangesAsync();
        return (parentId, childId, monitorId);
    }

    private static PrismediaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class FakeReplacer(OwnedFileReplaceResult result) : IOwnedFileReplacer {
        public bool Called { get; private set; }
        public Task<OwnedFileReplaceResult> ReplaceAsync(string ownedFolder, string newContentPath, BookFormatTier ownedFormatTier, CancellationToken cancellationToken) {
            Called = true;
            return Task.FromResult(result);
        }
    }

    private sealed class NullDownloadClientConfigStore : IDownloadClientConfigStore {
        public Task<DownloadClientDetail?> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<DownloadClientDetail?>(null);
        public Task<DownloadClientDetail?> GetDefaultAsync(CancellationToken cancellationToken) => Task.FromResult<DownloadClientDetail?>(null);
        public Task<IReadOnlyList<DownloadClientSummary>> ListAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<DownloadClientDetail>> ListDetailsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<DownloadClientSummary> SaveAsync(DownloadClientSaveCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class ThrowingDownloadClientFactory : IDownloadClientFactory {
        public IDownloadClient Get(DownloadClientKind kind) => throw new NotSupportedException();
    }

    private sealed class RecordingJobQueue : IJobQueueService {
        public List<EnqueueJobRequest> Enqueued { get; } = [];
        public Task<JobRunSnapshot> EnqueueAsync(EnqueueJobRequest request, CancellationToken cancellationToken) {
            Enqueued.Add(request);
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(new JobRunSnapshot(Guid.NewGuid(), request.Type, JobRunStatus.Queued, 0, null, request.PayloadJson ?? "{}", request.TargetEntityKind, request.TargetEntityId, request.TargetLabel, now, null, null));
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
