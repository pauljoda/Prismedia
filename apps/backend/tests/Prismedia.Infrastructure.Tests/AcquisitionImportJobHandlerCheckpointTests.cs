using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Reflection;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Entities;
using Prismedia.Application.Jobs;
using Prismedia.Application.Jobs.Handlers;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Acquisition;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

/// <summary>Pins job-dispatch behavior unique to durable, partially consumed TV payloads.</summary>
public sealed class AcquisitionImportJobHandlerCheckpointTests : IDisposable {
    private readonly string _root = Directory.CreateTempSubdirectory("prismedia-import-job-checkpoint-").FullName;

    public void Dispose() {
        try {
            Directory.Delete(_root, recursive: true);
        } catch {
            // best-effort test cleanup
        }
    }

    [Fact]
    public async Task ResumeSkipsWrongSeasonValidationAgainstTheRemainingPayloadSubset() {
        await using var db = CreateContext();
        var acquisitionId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var contentRoot = Directory.CreateDirectory(Path.Combine(_root, "download")).FullName;
        await File.WriteAllTextAsync(Path.Combine(contentRoot, "Show.S01E01.mkv"), "remaining-file");
        db.Acquisitions.Add(new AcquisitionRow {
            Id = acquisitionId,
            EntityId = entityId,
            Status = AcquisitionStatus.Failed,
            Kind = EntityKind.VideoSeason,
            Title = "Season 03",
            Series = "Show",
            SeasonNumber = 3,
            ExternalIdsJson = "{}",
            SourceUrlsJson = "[]",
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.DownloadTransfers.Add(new DownloadTransferRow {
            Id = Guid.NewGuid(),
            AcquisitionId = acquisitionId,
            ClientItemId = "transfer-1",
            ContentPath = contentRoot,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
        var store = AcquisitionTestFactory.Store(db);
        var claimJobId = Guid.NewGuid();
        await store.SetTvImportCheckpointAsync(
            acquisitionId,
            new TvImportCheckpoint(
                Guid.NewGuid(),
                Path.Combine(_root, "library", "Show"),
                ImportMode.Move,
                AllowFormatChange: false,
                SuccessMessage: "Resume",
                PreferSingleFileFinalSource: false,
                Units: [new TvImportCheckpointUnit(
                    "Show.S03E01.mkv",
                    Path.Combine(_root, "library", "Show", "S03", "Show.S03E01.mkv"),
                    3,
                    1,
                    [],
                    SourceAbsolutePath: Path.Combine(contentRoot, "Show.S03E01.mkv"))],
                TransferClientItemId: "transfer-1",
                AttemptId: Guid.NewGuid(),
                ClaimJobId: claimJobId),
            CancellationToken.None);
        var engine = new RecordingEngine();
        var lifecycle = new RecordingLifecycleLease();
        var handler = new AcquisitionImportJobHandler(
            store,
            new SingleEngineFactory(engine),
            new DownloadPayloadReader(),
            new EfAcquisitionHistoryStore(db),
            NullLogger<AcquisitionImportJobHandler>.Instance,
            lifecycle);
        var job = new JobRunSnapshot(
            claimJobId,
            JobType.AcquisitionImport,
            JobRunStatus.Running,
            0,
            null,
            AcquisitionJobPayload.Serialize(acquisitionId),
            null,
            null,
            null,
            now,
            now,
            null);

        await handler.HandleAsync(
            new JobContext(job, new MergedImportTestSupport.RecordingJobQueue()),
            CancellationToken.None);

        Assert.True(engine.Called);
        Assert.Equal([entityId], lifecycle.ExecutedFor);
        Assert.Equal(AcquisitionStatus.Importing, await store.GetStatusAsync(acquisitionId, CancellationToken.None));
    }

    [Fact]
    public async Task EntityDeletionClaimRejectsInitialImportBeforeEngineOrStatusMutation() {
        await using var db = CreateContext();
        var acquisitionId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var contentRoot = Directory.CreateDirectory(Path.Combine(_root, "claimed-download")).FullName;
        db.Acquisitions.Add(new AcquisitionRow {
            Id = acquisitionId,
            EntityId = entityId,
            Status = AcquisitionStatus.Downloaded,
            Kind = EntityKind.Book,
            Title = "Dune",
            ExternalIdsJson = "{}",
            SourceUrlsJson = "[]",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.DownloadTransfers.Add(new DownloadTransferRow {
            Id = Guid.NewGuid(),
            AcquisitionId = acquisitionId,
            ClientItemId = "transfer-1",
            ContentPath = contentRoot,
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();
        var engine = new RecordingEngine(EntityKind.Book);
        var handler = new AcquisitionImportJobHandler(
            AcquisitionTestFactory.Store(db),
            new SingleEngineFactory(engine),
            new DownloadPayloadReader(),
            new EfAcquisitionHistoryStore(db),
            NullLogger<AcquisitionImportJobHandler>.Instance,
            new RecordingLifecycleLease(allow: false));

        var exception = await Assert.ThrowsAsync<EntityLifecycleMutationConflictException>(() =>
            handler.HandleAsync(
                ContextFor(acquisitionId, Guid.NewGuid(), now),
                CancellationToken.None));

        Assert.Equal(entityId, exception.EntityId);
        Assert.False(engine.Called);
        Assert.Equal(
            AcquisitionStatus.Downloaded,
            await AcquisitionTestFactory.Store(db).GetStatusAsync(acquisitionId, CancellationToken.None));
    }

    [Fact]
    public async Task BookPlacementCheckpointIsClaimedAndDispatchedWithoutReplanningConsumedPayload() {
        await using var db = CreateContext();
        var acquisitionId = Guid.NewGuid();
        var oldClaimJobId = Guid.NewGuid();
        var retryJobId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var payloadRoot = Directory.CreateDirectory(Path.Combine(_root, "book-download")).FullName;
        var libraryRoot = Directory.CreateDirectory(Path.Combine(_root, "book-library")).FullName;
        var source = Path.Combine(payloadRoot, "Novel.epub");
        var target = Path.Combine(libraryRoot, "Author", "Novel.epub");
        // Move already consumed the payload, but the exact target survived the crash.
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await File.WriteAllTextAsync(target, "placed-book");
        var checkpoint = new ImportPlacementCheckpoint(
            EntityKind.Book,
            Guid.NewGuid(),
            libraryRoot,
            payloadRoot,
            ImportMode.Move,
            Path.GetDirectoryName(target)!,
            Path.GetDirectoryName(target)!,
            "Imported into the library.",
            [new ImportPlacementCheckpointUnit(
                "Novel.epub",
                source,
                target,
                IsMedia: true)],
            TransferClientItemId: "book-transfer",
            AttemptId: Guid.NewGuid(),
            ClaimJobId: oldClaimJobId);
        db.Acquisitions.Add(new AcquisitionRow {
            Id = acquisitionId,
            Status = AcquisitionStatus.Failed,
            Kind = EntityKind.Book,
            Title = "Novel",
            Author = "Author",
            ExternalIdsJson = "{}",
            SourceUrlsJson = "[]",
            ImportCheckpointJson = ImportPlacementCheckpointJson.Serialize(checkpoint),
            ImportClaimJobId = oldClaimJobId,
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.DownloadTransfers.Add(new DownloadTransferRow {
            Id = Guid.NewGuid(),
            AcquisitionId = acquisitionId,
            ClientItemId = "book-transfer",
            ContentPath = payloadRoot,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();

        var store = AcquisitionTestFactory.Store(db);
        var engine = new RecordingEngine(EntityKind.Book);
        var handler = new AcquisitionImportJobHandler(
            store,
            new SingleEngineFactory(engine),
            new DownloadPayloadReader(),
            new EfAcquisitionHistoryStore(db),
            NullLogger<AcquisitionImportJobHandler>.Instance);

        await handler.HandleAsync(
            ContextFor(acquisitionId, retryJobId, now),
            CancellationToken.None);

        Assert.True(engine.Called);
        Assert.Equal(retryJobId, engine.LastImport?.ImportPlacementCheckpoint?.ClaimJobId);
        Assert.Equal(AcquisitionStatus.Importing, await store.GetStatusAsync(acquisitionId, CancellationToken.None));
    }

    [Theory]
    [InlineData(AcquisitionStatus.Importing, false)]
    [InlineData(AcquisitionStatus.Failed, false)]
    [InlineData(AcquisitionStatus.ManualImportRequired, true)]
    public async Task OwningJobRecoversPreCheckpointWorkAndExplicitManualRetryClaimsAHold(
        AcquisitionStatus startingStatus,
        bool manualRetry) {
        await using var db = CreateContext();
        var acquisitionId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var contentRoot = Directory.CreateDirectory(Path.Combine(_root, Guid.NewGuid().ToString("N"))).FullName;
        await File.WriteAllTextAsync(Path.Combine(contentRoot, "Show.S03E01.mkv"), "episode");
        db.Acquisitions.Add(new AcquisitionRow {
            Id = acquisitionId,
            Status = startingStatus,
            ImportClaimJobId = manualRetry ? null : jobId,
            Kind = EntityKind.VideoSeason,
            Title = "Season 03",
            Series = "Show",
            SeasonNumber = 3,
            ExternalIdsJson = "{}",
            SourceUrlsJson = "[]",
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.DownloadTransfers.Add(new DownloadTransferRow {
            Id = Guid.NewGuid(),
            AcquisitionId = acquisitionId,
            ClientItemId = "transfer-1",
            ContentPath = contentRoot,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
        var store = AcquisitionTestFactory.Store(db);
        var engine = new RecordingEngine();
        var handler = new AcquisitionImportJobHandler(
            store,
            new SingleEngineFactory(engine),
            new DownloadPayloadReader(),
            new EfAcquisitionHistoryStore(db),
            NullLogger<AcquisitionImportJobHandler>.Instance);
        var job = new JobRunSnapshot(
            jobId,
            JobType.AcquisitionImport,
            JobRunStatus.Running,
            0,
            null,
            AcquisitionJobPayload.Serialize(acquisitionId, manualRetry: manualRetry),
            null,
            null,
            null,
            now,
            now,
            null);

        await handler.HandleAsync(
            new JobContext(job, new MergedImportTestSupport.RecordingJobQueue()),
            CancellationToken.None);

        Assert.True(engine.Called);
        Assert.Equal(AcquisitionStatus.Importing, await store.GetStatusAsync(acquisitionId, CancellationToken.None));
    }

    [Theory]
    [InlineData("dangerous", "cancelled")]
    [InlineData("dangerous", "deleted")]
    [InlineData("dangerous", "newer-status")]
    [InlineData("wrong-content", "cancelled")]
    [InlineData("wrong-content", "deleted")]
    [InlineData("wrong-content", "newer-status")]
    [InlineData("missing-engine", "cancelled")]
    [InlineData("missing-engine", "deleted")]
    [InlineData("missing-engine", "newer-status")]
    public async Task StaleInitialJobCannotOverwriteALifecycleThatWinsBeforeItsClaim(
        string branch,
        string competingAction) {
        await using var db = CreateContext();
        var acquisitionId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var contentRoot = Directory.CreateDirectory(Path.Combine(_root, Guid.NewGuid().ToString("N"))).FullName;
        var fileName = branch switch {
            "dangerous" => "Show.S03E01.scr",
            "wrong-content" => "Show.S01E01.mkv",
            _ => "Show.S03E01.mkv",
        };
        await File.WriteAllTextAsync(Path.Combine(contentRoot, fileName), "payload");
        var row = new AcquisitionRow {
            Id = acquisitionId,
            Status = AcquisitionStatus.Downloaded,
            Kind = EntityKind.VideoSeason,
            Title = "Season 03",
            Series = "Show",
            SeasonNumber = 3,
            ExternalIdsJson = "{}",
            SourceUrlsJson = "[]",
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Acquisitions.Add(row);
        db.DownloadTransfers.Add(new DownloadTransferRow {
            Id = Guid.NewGuid(),
            AcquisitionId = acquisitionId,
            ClientItemId = "transfer-1",
            ContentPath = contentRoot,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();

        var innerStore = AcquisitionTestFactory.Store(db);
        var racingStore = RacingStore.Create(innerStore, async () => {
            if (competingAction == "deleted") {
                db.Acquisitions.Remove(row);
            } else {
                row.Status = competingAction == "cancelled"
                    ? AcquisitionStatus.Cancelled
                    : AcquisitionStatus.Searching;
                row.StatusMessage = "Newer lifecycle won.";
            }
            await db.SaveChangesAsync();
        });
        var engine = new RecordingEngine();
        var factory = branch == "missing-engine"
            ? new SingleEngineFactory(null)
            : new SingleEngineFactory(engine);
        var handler = new AcquisitionImportJobHandler(
            racingStore,
            factory,
            new DownloadPayloadReader(),
            new EfAcquisitionHistoryStore(db),
            NullLogger<AcquisitionImportJobHandler>.Instance);

        await handler.HandleAsync(
            ContextFor(acquisitionId, Guid.NewGuid(), now),
            CancellationToken.None);

        Assert.False(engine.Called);
        Assert.Empty(await db.AcquisitionHistory.AsNoTracking().ToArrayAsync());
        var persisted = await db.Acquisitions.AsNoTracking()
            .FirstOrDefaultAsync(value => value.Id == acquisitionId);
        if (competingAction == "deleted") {
            Assert.Null(persisted);
        } else {
            Assert.Equal(
                competingAction == "cancelled" ? AcquisitionStatus.Cancelled : AcquisitionStatus.Searching,
                persisted?.Status);
            Assert.Equal("Newer lifecycle won.", persisted?.StatusMessage);
        }
    }

    [Fact]
    public async Task CorruptCheckpointCannotOverwriteAConcurrentCancellation() {
        await using var db = CreateContext();
        var acquisitionId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var row = new AcquisitionRow {
            Id = acquisitionId,
            Status = AcquisitionStatus.Downloaded,
            Kind = EntityKind.VideoSeason,
            Title = "Season 03",
            Series = "Show",
            SeasonNumber = 3,
            ExternalIdsJson = "{}",
            SourceUrlsJson = "[]",
            ImportCheckpointJson = "{ damaged checkpoint",
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Acquisitions.Add(row);
        await db.SaveChangesAsync();
        var innerStore = AcquisitionTestFactory.Store(db);
        var racingStore = RacingStore.Create(innerStore, async () => {
            row.Status = AcquisitionStatus.Cancelled;
            row.StatusMessage = "Cancelled while the job was loading.";
            await db.SaveChangesAsync();
        });
        var handler = new AcquisitionImportJobHandler(
            racingStore,
            new SingleEngineFactory(new RecordingEngine()),
            new DownloadPayloadReader(),
            new EfAcquisitionHistoryStore(db),
            NullLogger<AcquisitionImportJobHandler>.Instance);

        await handler.HandleAsync(
            ContextFor(acquisitionId, Guid.NewGuid(), now),
            CancellationToken.None);

        Assert.Equal(AcquisitionStatus.Cancelled, row.Status);
        Assert.Equal("Cancelled while the job was loading.", row.StatusMessage);
    }

    [Fact]
    public async Task StaleCorruptCheckpointAlreadyCancelledIsNotMovedToManualImport() {
        await using var db = CreateContext();
        var acquisitionId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.Acquisitions.Add(new AcquisitionRow {
            Id = acquisitionId,
            Status = AcquisitionStatus.Cancelled,
            Kind = EntityKind.VideoSeason,
            Title = "Season 03",
            Series = "Show",
            SeasonNumber = 3,
            ExternalIdsJson = "{}",
            SourceUrlsJson = "[]",
            ImportCheckpointJson = "{ damaged checkpoint",
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
        var store = AcquisitionTestFactory.Store(db);
        var handler = new AcquisitionImportJobHandler(
            store,
            new SingleEngineFactory(new RecordingEngine()),
            new DownloadPayloadReader(),
            new EfAcquisitionHistoryStore(db),
            NullLogger<AcquisitionImportJobHandler>.Instance);

        await handler.HandleAsync(
            ContextFor(acquisitionId, Guid.NewGuid(), now),
            CancellationToken.None);

        Assert.Equal(AcquisitionStatus.Cancelled, await store.GetStatusAsync(acquisitionId, CancellationToken.None));
    }

    [Theory]
    [InlineData(AcquisitionStatus.Downloaded, false, AcquisitionStatus.ManualImportRequired)]
    [InlineData(AcquisitionStatus.Failed, false, AcquisitionStatus.ManualImportRequired)]
    [InlineData(AcquisitionStatus.ManualImportRequired, false, AcquisitionStatus.ManualImportRequired)]
    [InlineData(AcquisitionStatus.Importing, true, AcquisitionStatus.ManualImportRequired)]
    [InlineData(AcquisitionStatus.Importing, false, AcquisitionStatus.Importing)]
    [InlineData(AcquisitionStatus.Cancelled, true, AcquisitionStatus.Cancelled)]
    [InlineData(AcquisitionStatus.Imported, true, AcquisitionStatus.Imported)]
    [InlineData(AcquisitionStatus.Searching, true, AcquisitionStatus.Searching)]
    public async Task CorruptCheckpointHoldHonorsLifecycleAndActiveJobOwnership(
        AcquisitionStatus startingStatus,
        bool sameJobOwnsImport,
        AcquisitionStatus expectedStatus) {
        await using var db = CreateContext();
        var acquisitionId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var row = new AcquisitionRow {
            Id = acquisitionId,
            Status = startingStatus,
            StatusMessage = "Existing lifecycle state.",
            Kind = EntityKind.VideoSeason,
            Title = "Season 03",
            Series = "Show",
            SeasonNumber = 3,
            ExternalIdsJson = "{}",
            SourceUrlsJson = "[]",
            ImportCheckpointJson = "{ damaged checkpoint",
            ImportClaimJobId = sameJobOwnsImport ? jobId : Guid.NewGuid(),
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Acquisitions.Add(row);
        await db.SaveChangesAsync();
        var store = AcquisitionTestFactory.Store(db);
        var handler = new AcquisitionImportJobHandler(
            store,
            new SingleEngineFactory(new RecordingEngine()),
            new DownloadPayloadReader(),
            new EfAcquisitionHistoryStore(db),
            NullLogger<AcquisitionImportJobHandler>.Instance);

        await handler.HandleAsync(ContextFor(acquisitionId, jobId, now), CancellationToken.None);

        Assert.Equal(expectedStatus, row.Status);
        if (expectedStatus == AcquisitionStatus.ManualImportRequired) {
            Assert.Equal(ImportCheckpointLifecycle.CorruptCheckpointMessage, row.StatusMessage);
            Assert.Null(row.ImportClaimJobId);
        } else {
            Assert.Equal("Existing lifecycle state.", row.StatusMessage);
            Assert.NotNull(row.ImportClaimJobId);
        }
    }

    private static JobContext ContextFor(Guid acquisitionId, Guid jobId, DateTimeOffset now) =>
        new(
            new JobRunSnapshot(
                jobId,
                JobType.AcquisitionImport,
                JobRunStatus.Running,
                0,
                null,
                AcquisitionJobPayload.Serialize(acquisitionId),
                null,
                null,
                null,
                now,
                now,
                null),
            new MergedImportTestSupport.RecordingJobQueue());

    private static PrismediaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private sealed class RecordingEngine(EntityKind kind = EntityKind.VideoSeason) : IAcquisitionImportEngine {
        public EntityKind Kind => kind;
        public bool Called { get; private set; }
        public AcquisitionImportContext? LastImport { get; private set; }

        public Task ImportAsync(
            JobContext context,
            AcquisitionImportContext import,
            CancellationToken cancellationToken) {
            Called = true;
            LastImport = import;
            return Task.CompletedTask;
        }
    }

    private sealed class SingleEngineFactory(IAcquisitionImportEngine? engine) : IAcquisitionImportEngineFactory {
        public IAcquisitionImportEngine? Find(EntityKind kind) => engine is not null && kind == engine.Kind ? engine : null;
    }

    private sealed class RecordingLifecycleLease(bool allow = true)
        : IEntityLifecycleMutationLease {
        public List<Guid> ExecutedFor { get; } = [];

        public async Task<bool> ExecuteAsync(
            Guid entityId,
            Func<CancellationToken, Task> mutation,
            CancellationToken cancellationToken) {
            ExecutedFor.Add(entityId);
            if (!allow) {
                return false;
            }

            await mutation(cancellationToken);
            return true;
        }

        public async Task<bool> ExecuteManyAsync(
            IReadOnlyCollection<Guid> entityIds,
            Func<CancellationToken, Task> mutation,
            CancellationToken cancellationToken) {
            ExecutedFor.AddRange(entityIds);
            if (!allow) {
                return false;
            }

            await mutation(cancellationToken);
            return true;
        }
    }

    /// <summary>
    /// Runs a competing lifecycle mutation immediately after the handler snapshots its import context,
    /// reproducing the queue-latency window without weakening the production store's atomic claims.
    /// </summary>
    private class RacingStore : DispatchProxy {
        private IAcquisitionStore _inner = null!;
        private Func<Task> _afterImportContext = null!;

        public static IAcquisitionStore Create(IAcquisitionStore inner, Func<Task> afterImportContext) {
            var proxy = Create<IAcquisitionStore, RacingStore>();
            var racing = (RacingStore)(object)proxy;
            racing._inner = inner;
            racing._afterImportContext = afterImportContext;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) {
            ArgumentNullException.ThrowIfNull(targetMethod);
            try {
                var result = targetMethod.Invoke(_inner, args);
                return targetMethod.Name == nameof(IAcquisitionStore.GetImportContextAsync)
                    ? RunRaceAsync((Task<AcquisitionImportContext?>)result!)
                    : result;
            } catch (TargetInvocationException ex) when (ex.InnerException is not null) {
                throw ex.InnerException;
            }
        }

        private async Task<AcquisitionImportContext?> RunRaceAsync(Task<AcquisitionImportContext?> load) {
            try {
                return await load;
            } finally {
                await _afterImportContext();
            }
        }
    }
}
