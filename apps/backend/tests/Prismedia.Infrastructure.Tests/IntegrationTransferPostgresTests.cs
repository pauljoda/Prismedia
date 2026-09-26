using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Integrations;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;
using Prismedia.Infrastructure.Integrations;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;
using Prismedia.Infrastructure.Queue;

namespace Prismedia.Infrastructure.Tests;

public sealed class IntegrationTransferPostgresTests : IDisposable {
    private readonly string keys = Path.Combine(Path.GetTempPath(), "prismedia-transfer-pg-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task RemoteCancellationAtomicallyWakesReconciliationAndRetainsOwnershipUntilStopped() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var connection = await AddConnectionAsync(database);
        var transfer = IntegrationTransfer.Create(Guid.NewGuid(), connection, "installation");
        var plan = Plan with { Source = null, Executor = new(transfer.State.OperationId, "https://source.test/book", "selection", "revision", ["publication"], 1, 1000) };
        await using (var seed = database.CreateContext()) {
            var store = Store(seed);
            await store.CreateAsync(transfer, plan, default);
            transfer.BeginSubmission(); await store.SaveAsync(transfer, 1, null, default);
            transfer.AcceptSubmission(transfer.State.OperationId, "installation", "job"); await store.SaveAsync(transfer, 2, null, default);
            var run = await seed.JobRuns.SingleAsync(); run.AvailableAt = DateTimeOffset.UtcNow.AddHours(1); await seed.SaveChangesAsync();
        }
        await using (var failure = database.CreateContext()) {
            var store = new EfIntegrationTransferStore(failure, new(keys), new FailingScheduler(new(new JobQueueService(failure))));
            var cancel = new IntegrationTransfer(transfer.State); cancel.RequestRemoteCancellation();
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAndEnqueueAsync(cancel, 3, default));
        }
        await using (var check = database.CreateContext()) {
            Assert.False((await Store(check).FindAsync(transfer.State.OperationId, default))!.Transfer.State.CancellationRequested);
            Assert.True((await check.JobRuns.SingleAsync()).AvailableAt > DateTimeOffset.UtcNow.AddMinutes(50));
        }
        await using (var api = database.CreateContext()) {
            var service = new IntegrationTransferService(Store(api), new JobQueueService(api));
            Assert.True((await service.CancelAsync(transfer.State.OperationId, default)).CancellationRequested);
            Assert.True((await service.CancelAsync(transfer.State.OperationId, default)).CancellationRequested);
        }
        await using var verify = database.CreateContext();
        Assert.NotNull((await verify.IntegrationTransfers.SingleAsync()).ActiveOwnershipKey);
        Assert.True((await verify.JobRuns.SingleAsync()).AvailableAt <= DateTimeOffset.UtcNow);
        transfer.Observe("installation", "job", 1, RemoteJobState.Succeeded, "manifest");
        await Assert.ThrowsAsync<IntegrationTransferConflictException>(() => Store(verify).SaveAsync(transfer, 3, null, default));
    }

    [Fact]
    public async Task CancellationFencesAStaleWorkerReleasesOwnershipAndOnlyStopsItsOwnTarget() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var connection = await AddConnectionAsync(database);
        var plan = Plan;
        var transfer = IntegrationTransfer.CreateSourceDownload(Guid.NewGuid(), connection);
        var other = IntegrationTransfer.CreateSourceDownload(Guid.NewGuid(), connection);
        await using var worker = database.CreateContext();
        await Store(worker).CreateAsync(transfer, plan, default);
        await Store(worker).CreateAsync(other, plan with { OwnershipKey = "another-owner" }, default);
        await using (var api = database.CreateContext()) {
            var service = new IntegrationTransferService(Store(api), new JobQueueService(api));
            Assert.Equal(IntegrationTransferPhase.Cancelled, (await service.CancelAsync(transfer.State.OperationId, default)).Phase);
            Assert.Equal(IntegrationTransferPhase.Cancelled, (await service.CancelAsync(transfer.State.OperationId, default)).Phase);
        }
        transfer.AcceptSourceArtifact(new("file", "item", "book.epub", "application/epub+zip", 100, new string('a', 64), IntegrationArtifactRole.Content));
        await Assert.ThrowsAsync<IntegrationTransferConflictException>(() => Store(worker).SaveAsync(transfer, 1, null, default));
        await using var check = database.CreateContext();
        Assert.Equal(JobRunStatus.Cancelled, (await check.JobRuns.SingleAsync(job => job.TargetEntityId == transfer.State.OperationId.ToString())).Status);
        Assert.Equal(JobRunStatus.Queued, (await check.JobRuns.SingleAsync(job => job.TargetEntityId == other.State.OperationId.ToString())).Status);
        Assert.Null((await check.IntegrationTransfers.SingleAsync(row => row.Id == transfer.State.OperationId)).ActiveOwnershipKey);
        await Store(check).CreateAsync(IntegrationTransfer.CreateSourceDownload(Guid.NewGuid(), connection), plan, default);
    }

    [Fact]
    public async Task QueuePublicationFailureRollsBackIntentAndEntireGraph() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var connection = await AddConnectionAsync(database);
        var operation = IntegrationTransfer.CreateSourceDownload(Guid.NewGuid(), connection);
        await using (var db = database.CreateContext()) {
            var store = new EfIntegrationTransferStore(db, new(keys), new FailingScheduler(new(new JobQueueService(db))));
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.CreateAsync(operation, Plan, default));
        }
        await using var check = database.CreateContext();
        Assert.Empty(await check.IntegrationTransfers.ToArrayAsync());
        Assert.Empty(await check.JobRuns.ToArrayAsync());
        Assert.Empty(await check.JobGraphs.ToArrayAsync());
    }

    [Fact]
    public async Task ConcurrentRequestsKeepOneActiveOwnerAndRepeatedOperationKeepsOneQueueRun() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var connection = await AddConnectionAsync(database);
        var plan = Plan;
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ => {
            await using var db = database.CreateContext();
            try {
                return await Store(db).CreateAsync(IntegrationTransfer.CreateSourceDownload(Guid.NewGuid(), connection), plan, default);
            } catch (IntegrationTransferConflictException) { return null; }
        }));
        var accepted = Assert.Single(results, result => result is not null)!;
        await using (var retry = database.CreateContext()) {
            var same = await Store(retry).CreateAsync(IntegrationTransfer.CreateSourceDownload(accepted.Transfer.State.OperationId, connection), plan, default);
            Assert.Equal(accepted.Transfer.State.OperationId, same.Transfer.State.OperationId);
            await Store(retry).EnqueueRetryAsync(same.Transfer.State.OperationId, default);
        }
        await using var check = database.CreateContext();
        Assert.Single(await check.IntegrationTransfers.ToArrayAsync());
        var job = Assert.Single(await check.JobRuns.ToArrayAsync());
        Assert.Equal(JobType.IntegrationTransfer, job.Type);
        Assert.Equal(accepted.Transfer.State.OperationId.ToString(), job.TargetEntityId);
        Assert.Single(await check.JobGraphs.ToArrayAsync());
    }

    private EfIntegrationTransferStore Store(PrismediaDbContext db) => new(db, new(keys), new IntegrationTransferScheduler(new JobQueueService(db)));
    private static IntegrationTransferPlan Plan => new("Book", EntityKind.Book, Guid.NewGuid(), Path.GetTempPath(), "same-source-owner", new string('a', 64),
        new(new("publication", "https://catalog.test/entry", EntityKind.Book), "epub"));
    private static async Task<Guid> AddConnectionAsync(PostgresTestDatabase database) {
        await using var db = database.CreateContext();
        var row = new IntegrationConnectionRow { Id = Guid.NewGuid(), PluginId = "catalog-test", Name = "Catalog", BaseUrl = "https://catalog.test/", Revision = 1, Status = ConnectionStatus.Ready, Enabled = true };
        db.IntegrationConnections.Add(row);
        await db.SaveChangesAsync();
        return row.Id;
    }
    private sealed class FailingScheduler(IntegrationTransferScheduler actual) : IIntegrationTransferScheduler {
        public async Task EnqueueAsync(Guid operationId, string title, CancellationToken cancellationToken) {
            await actual.EnqueueAsync(operationId, title, cancellationToken);
            throw new InvalidOperationException("Simulated process boundary failure before commit.");
        }
    }
    public void Dispose() { if (Directory.Exists(keys)) Directory.Delete(keys, true); }
}
