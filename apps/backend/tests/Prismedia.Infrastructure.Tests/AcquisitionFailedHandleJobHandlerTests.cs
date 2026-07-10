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
/// Exercises failed-download recovery end-to-end over the real EF stores (on the in-memory provider),
/// with only the re-queue capability faked so the test can observe which candidate would be grabbed.
/// The release that failed is carried in the job payload (not re-read), so the tests pass it explicitly.
/// </summary>
public sealed class AcquisitionFailedHandleJobHandlerTests {
    [Fact]
    public async Task BlocklistsFailedReleaseAndReQueuesNextBestWhenAutoRedownloadOn() {
        await using var db = CreateContext();
        var (acquisitionId, candidateA, candidateB) = await SeedTwoCandidatesAsync(db, autoRedownload: true);
        var queue = new RecordingQueueService();

        await RunAsync(db, queue, acquisitionId, Selected(candidateA));

        var blocklisted = await new EfAcquisitionBlocklistStore(db).GetIdentitiesAsync(CancellationToken.None);
        Assert.Contains(candidateA.Identity, blocklisted);
        Assert.Equal((acquisitionId, candidateB.CandidateId), Assert.Single(queue.Calls));
        Assert.Equal(AcquisitionStatus.Failed, Assert.Single(queue.RequiredStatuses));
    }

    [Fact]
    public async Task BlocklistsButDoesNotReQueueWhenAutoRedownloadOff() {
        await using var db = CreateContext();
        var (acquisitionId, candidateA, _) = await SeedTwoCandidatesAsync(db, autoRedownload: false);
        var queue = new RecordingQueueService();

        await RunAsync(db, queue, acquisitionId, Selected(candidateA));

        Assert.Contains(candidateA.Identity, await new EfAcquisitionBlocklistStore(db).GetIdentitiesAsync(CancellationToken.None));
        Assert.Empty(queue.Calls);
        Assert.Equal(AcquisitionStatus.Failed, await StatusOf(db, acquisitionId));
    }

    [Fact]
    public async Task FailsWhenNoAlternativeRemains() {
        await using var db = CreateContext();
        var (acquisitionId, candidateA, _) = await SeedTwoCandidatesAsync(db, autoRedownload: true, includeSecond: false);
        var queue = new RecordingQueueService();

        await RunAsync(db, queue, acquisitionId, Selected(candidateA));

        Assert.Empty(queue.Calls);
        Assert.Equal(AcquisitionStatus.Failed, await StatusOf(db, acquisitionId));
    }

    [Fact]
    public async Task NoSelectedReleaseLeavesFailedWithoutBlocklisting() {
        await using var db = CreateContext();
        var (acquisitionId, _, _) = await SeedTwoCandidatesAsync(db, autoRedownload: true);
        var queue = new RecordingQueueService();

        // A manually-uploaded torrent has no snapshot — nothing specific to blocklist; just leave it failed.
        await RunAsync(db, queue, acquisitionId, selected: null);

        Assert.Empty(await new EfAcquisitionBlocklistStore(db).GetIdentitiesAsync(CancellationToken.None));
        Assert.Empty(queue.Calls);
        Assert.Equal(AcquisitionStatus.Failed, await StatusOf(db, acquisitionId));
    }

    [Fact]
    public async Task NullInfoHashReleaseIsBlocklistedByTitleAndRejectedOnNextSearch() {
        // The correctness crux: a magnet/info-page release has no info hash at search time, so it must be
        // blocklisted by its normalized indexer+title identity and recognized again on the next search.
        await using var db = CreateContext();
        var (acquisitionId, _, _) = await SeedTwoCandidatesAsync(db, autoRedownload: false);
        var selected = new SelectedRelease("Some Book (epub)", "Indexer", InfoHash: null);

        await RunAsync(db, new RecordingQueueService(), acquisitionId, selected);

        var blocklisted = await new EfAcquisitionBlocklistStore(db).GetIdentitiesAsync(CancellationToken.None);
        Assert.Contains(selected.Identity, blocklisted);
        Assert.StartsWith("title:", selected.Identity);

        // A fresh search result for the same release (still no hash) must be rejected as Blocklisted.
        var release = new IndexerRelease("Some Book (epub)", 5_000_000, 50, 5, DownloadProtocol.Torrent, "http://dl", "magnet:?x", null, "http://info", null, null);
        var scored = new BookReleaseDecisionEngine().Evaluate([(release, null, "Indexer")], BookAcquisitionRules.Default, blocklisted);
        Assert.False(scored[0].Accepted);
        Assert.Contains(ReleaseRejectionReason.Blocklisted, scored[0].Rejections);
    }

    [Fact]
    public async Task RecoveryTerminatesOnceEveryCandidateIsBlocklisted() {
        await using var db = CreateContext();
        var (acquisitionId, candidateA, candidateB) = await SeedTwoCandidatesAsync(db, autoRedownload: true);
        var queue = new RecordingQueueService();

        await RunAsync(db, queue, acquisitionId, Selected(candidateA)); // A fails → grabs B
        await RunAsync(db, queue, acquisitionId, Selected(candidateB)); // B fails → nothing left

        var blocklisted = await new EfAcquisitionBlocklistStore(db).GetIdentitiesAsync(CancellationToken.None);
        Assert.Contains(candidateA.Identity, blocklisted);
        Assert.Contains(candidateB.Identity, blocklisted);
        // B was grabbed exactly once; the second failure finds no non-blocklisted candidate and gives up.
        Assert.Equal((acquisitionId, candidateB.CandidateId), Assert.Single(queue.Calls));
        Assert.Equal(AcquisitionStatus.Failed, await StatusOf(db, acquisitionId));
    }

    [Fact]
    public async Task RecordsDownloadFailedAndBlocklistedHistoryEvents() {
        await using var db = CreateContext();
        var (acquisitionId, candidateA, _) = await SeedTwoCandidatesAsync(db, autoRedownload: false);

        await RunAsync(db, new RecordingQueueService(), acquisitionId, Selected(candidateA));

        var events = await new EfAcquisitionHistoryStore(db).ListAsync(200, entityId: null, CancellationToken.None);
        // A failed download with a snapshot records BOTH the failure and the resulting blocklist.
        Assert.Contains(events, e => e.Event == AcquisitionHistoryEvent.DownloadFailed && e.AcquisitionId == acquisitionId);
        Assert.Contains(events, e => e.Event == AcquisitionHistoryEvent.Blocklisted && e.ReleaseTitle == candidateA.Title);
    }

    [Fact]
    public async Task NoSelectedReleaseStillRecordsDownloadFailedButNotBlocklisted() {
        await using var db = CreateContext();
        var (acquisitionId, _, _) = await SeedTwoCandidatesAsync(db, autoRedownload: true);

        // A manually-uploaded torrent has no snapshot: the failure is still logged, but nothing is blocklisted.
        await RunAsync(db, new RecordingQueueService(), acquisitionId, selected: null);

        var events = await new EfAcquisitionHistoryStore(db).ListAsync(200, entityId: null, CancellationToken.None);
        Assert.Contains(events, e => e.Event == AcquisitionHistoryEvent.DownloadFailed);
        Assert.DoesNotContain(events, e => e.Event == AcquisitionHistoryEvent.Blocklisted);
    }

    [Fact]
    public async Task StaleFailedHandlerCannotOverwriteCancellationOrBlocklist() {
        await using var db = CreateContext();
        var (acquisitionId, candidateA, _) = await SeedTwoCandidatesAsync(db, autoRedownload: true);
        Assert.True(await AcquisitionTestFactory.Store(db).TryTransitionStatusAsync(
            acquisitionId,
            [AcquisitionStatus.Downloading],
            AcquisitionStatus.Cancelled,
            "Cancelled.",
            CancellationToken.None));
        var queue = new RecordingQueueService();

        await RunAsync(db, queue, acquisitionId, Selected(candidateA));

        Assert.Equal(AcquisitionStatus.Cancelled, await StatusOf(db, acquisitionId));
        Assert.Empty(await new EfAcquisitionBlocklistStore(db).GetIdentitiesAsync(CancellationToken.None));
        Assert.Empty(queue.Calls);
        Assert.Empty(await new EfAcquisitionHistoryStore(db).ListAsync(
            200,
            entityId: null,
            CancellationToken.None));
    }

    [Fact]
    public async Task CancellationDuringRecoveryWinsOverTheFallbackFailedFinish() {
        await using var db = CreateContext();
        var (acquisitionId, candidateA, _) = await SeedTwoCandidatesAsync(db, autoRedownload: true);
        var queue = new RecordingQueueService {
            BeforeQueue = async () => {
                Assert.True(await AcquisitionTestFactory.Store(db).TryTransitionStatusAsync(
                    acquisitionId,
                    [AcquisitionStatus.Failed],
                    AcquisitionStatus.Cancelled,
                    "Cancelled.",
                    CancellationToken.None));
            },
            Failure = new IOException("queue interrupted")
        };

        await RunAsync(db, queue, acquisitionId, Selected(candidateA));

        Assert.Equal(AcquisitionStatus.Cancelled, await StatusOf(db, acquisitionId));
        Assert.Equal(AcquisitionStatus.Failed, Assert.Single(queue.RequiredStatuses));
    }

    [Fact]
    public async Task ListAcceptedCandidatesReturnsAcceptedOnlyBestFirst() {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var acquisitionId = Guid.NewGuid();
        db.Acquisitions.Add(new AcquisitionRow { Id = acquisitionId, Status = AcquisitionStatus.AwaitingSelection, Title = "B", ExternalIdsJson = "{}", SourceUrlsJson = "[]", CreatedAt = now, UpdatedAt = now });
        db.ReleaseCandidates.AddRange(
            Candidate(acquisitionId, "low", accepted: true, score: 10),
            Candidate(acquisitionId, "high", accepted: true, score: 90),
            Candidate(acquisitionId, "mid", accepted: true, score: 50),
            Candidate(acquisitionId, "rejected", accepted: false, score: 999));
        await db.SaveChangesAsync();

        var accepted = await AcquisitionTestFactory.Store(db).ListAcceptedCandidatesAsync(acquisitionId, CancellationToken.None);

        Assert.Equal(["high", "mid", "low"], accepted.Select(candidate => candidate.Title));
    }

    private static SelectedRelease Selected(AcquisitionCandidateRef candidate) =>
        new(candidate.Title, candidate.IndexerName, candidate.InfoHash);

    private static async Task<AcquisitionStatus> StatusOf(PrismediaDbContext db, Guid acquisitionId) =>
        await db.Acquisitions.AsNoTracking().Where(row => row.Id == acquisitionId).Select(row => row.Status).FirstAsync();

    private static async Task RunAsync(PrismediaDbContext db, IAcquisitionQueueService queue, Guid acquisitionId, SelectedRelease? selected) {
        if (selected is not null) {
            await AcquisitionTestFactory.Store(db).SetSelectedReleaseAsync(
                acquisitionId,
                selected,
                CancellationToken.None);
        }

        var handler = new AcquisitionFailedHandleJobHandler(
            AcquisitionTestFactory.Store(db),
            new EfAcquisitionBlocklistStore(db),
            new EfBookAcquisitionProfileStore(db),
            queue,
            new EfAcquisitionHistoryStore(db),
            NullLogger<AcquisitionFailedHandleJobHandler>.Instance);
        await handler.HandleAsync(new JobContext(Job(acquisitionId, selected), new ThrowingJobQueue()), CancellationToken.None);
    }

    private static ReleaseCandidateRow Candidate(Guid acquisitionId, string title, bool accepted, double score) =>
        new() {
            Id = Guid.NewGuid(), AcquisitionId = acquisitionId, IndexerName = "Indexer", Title = title,
            InfoHash = title + "-hash", Accepted = accepted, Score = score, Protocol = DownloadProtocol.Torrent,
            RejectionsJson = "[]", CreatedAt = DateTimeOffset.UtcNow
        };

    private static async Task<(Guid AcquisitionId, AcquisitionCandidateRef A, AcquisitionCandidateRef B)> SeedTwoCandidatesAsync(
        PrismediaDbContext db, bool autoRedownload, bool includeSecond = true) {
        var now = DateTimeOffset.UtcNow;
        var acquisitionId = Guid.NewGuid();
        db.Acquisitions.Add(new AcquisitionRow {
            Id = acquisitionId, Status = AcquisitionStatus.Downloading, Title = "Some Book",
            ExternalIdsJson = "{}", SourceUrlsJson = "[]", CreatedAt = now, UpdatedAt = now
        });
        db.BookAcquisitionProfiles.Add(new BookAcquisitionProfileRow {
            Id = Guid.NewGuid(), DisplayName = "Default", IsDefault = true, AutoRedownload = autoRedownload,
            TargetLibraryRootId = Guid.NewGuid(), CreatedAt = now, UpdatedAt = now
        });

        var a = new ReleaseCandidateRow {
            Id = Guid.NewGuid(), AcquisitionId = acquisitionId, IndexerName = "Indexer", Title = "Some Book (epub)",
            InfoHash = "hashA", Accepted = true, Score = 100, Protocol = DownloadProtocol.Torrent, RejectionsJson = "[]", CreatedAt = now
        };
        db.ReleaseCandidates.Add(a);
        ReleaseCandidateRow? b = null;
        if (includeSecond) {
            b = new ReleaseCandidateRow {
                Id = Guid.NewGuid(), AcquisitionId = acquisitionId, IndexerName = "Indexer", Title = "Some Book v2 (epub)",
                InfoHash = "hashB", Accepted = true, Score = 50, Protocol = DownloadProtocol.Torrent, RejectionsJson = "[]", CreatedAt = now
            };
            db.ReleaseCandidates.Add(b);
        }

        await db.SaveChangesAsync();

        var refA = new AcquisitionCandidateRef(a.Id, a.Title, a.IndexerName, a.InfoHash);
        var refB = b is null
            ? new AcquisitionCandidateRef(Guid.Empty, "", "", null)
            : new AcquisitionCandidateRef(b.Id, b.Title, b.IndexerName, b.InfoHash);
        return (acquisitionId, refA, refB);
    }

    private static JobRunSnapshot Job(Guid acquisitionId, SelectedRelease? selected) {
        var now = DateTimeOffset.UtcNow;
        return new JobRunSnapshot(
            Guid.NewGuid(), JobType.AcquisitionFailedHandle, JobRunStatus.Running, 0, null,
            AcquisitionFailedPayload.Serialize(acquisitionId, BlocklistReason.Failed, "removed", selected),
            null, acquisitionId.ToString(), null, now, now, null);
    }

    private static PrismediaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class RecordingQueueService : IAcquisitionQueueService {
        public List<(Guid AcquisitionId, Guid CandidateId)> Calls { get; } = [];
        public List<AcquisitionStatus?> RequiredStatuses { get; } = [];
        public Func<Task>? BeforeQueue { get; init; }
        public Exception? Failure { get; init; }

        public async Task<AcquisitionDetail?> QueueAsync(
            Guid acquisitionId,
            Guid candidateId,
            CancellationToken cancellationToken,
            bool manualPick = false,
            AcquisitionStatus? requiredStatus = null) {
            Calls.Add((acquisitionId, candidateId));
            RequiredStatuses.Add(requiredStatus);
            if (BeforeQueue is not null) {
                await BeforeQueue();
            }
            if (Failure is not null) {
                throw Failure;
            }

            return null;
        }
    }

    private sealed class ThrowingJobQueue : IJobQueueService {
        public Task<IReadOnlyList<JobRunSnapshot>> ListAsync(bool hideNsfw, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<JobRunSnapshot> EnqueueAsync(JobType type, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<JobRunSnapshot> EnqueueAsync(EnqueueJobRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> HasPendingAsync(JobType type, string? targetEntityId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> EnqueueBatchAsync(IReadOnlyList<EnqueueJobRequest> requests, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> CancelAsync(JobType? type, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> CancelRunAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> ClearFailuresAsync(JobType? type, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<JobRunSnapshot?> ClaimNextAsync(string workerId, CancellationToken cancellationToken, JobRunLane? lane = null) => throw new NotSupportedException();
        public Task<int> RecoverStaleRunningAsync(string currentWorkerId, TimeSpan staleAfter, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpdateProgressAsync(Guid id, int progress, string? message, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CompleteAsync(Guid id, string? message, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task FailAsync(Guid id, string message, TimeSpan retryDelay, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<JobQueueCount>> GetQueueCountsAsync(bool hideNsfw, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> PruneHistoryAsync(TimeSpan retention, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
