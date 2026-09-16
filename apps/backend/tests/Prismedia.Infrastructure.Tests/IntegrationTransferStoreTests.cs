using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Integrations;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;
using Prismedia.Infrastructure.Integrations;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Tests;

public sealed class IntegrationTransferStoreTests : IDisposable {
    private readonly string root = Path.Combine(Path.GetTempPath(), "prismedia-transfer-store-" + Guid.NewGuid().ToString("N"));
    private readonly DbContextOptions<PrismediaDbContext> options = new DbContextOptionsBuilder<PrismediaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
    private const string SensitiveLocator = "https://catalog.test/page?opaque=private-source-ticket";
    private static IntegrationTransferPlan Plan => new("Book", EntityKind.Book, Guid.NewGuid(), Path.GetTempPath(), "source-owner", new string('a', 64), new(new("source-item", SensitiveLocator, EntityKind.Book), "offer"));

    [Fact]
    public async Task AcceptedIntentIsEncryptedAndSameOperationDoesNotDispatchAgainAfterRestart() {
        var transfer = IntegrationTransfer.CreateSourceDownload(Guid.NewGuid(), Guid.NewGuid());
        var plan = Plan;
        var scheduler = new Scheduler();
        await using (var db = new PrismediaDbContext(options)) {
            var store = new EfIntegrationTransferStore(db, new(root), scheduler);
            await store.CreateAsync(transfer, plan, default);
            var row = await db.IntegrationTransfers.SingleAsync();
            Assert.DoesNotContain(SensitiveLocator, row.ProtectedPlan);
            Assert.DoesNotContain(SensitiveLocator, row.StateJson);
        }
        await using (var db = new PrismediaDbContext(options)) {
            var store = new EfIntegrationTransferStore(db, new(root), scheduler);
            var restored = await store.CreateAsync(transfer, plan, default);
            Assert.Equal(SensitiveLocator, restored.Plan.Source!.Selection.Locator);
            Assert.Equal(transfer.State.OperationId, restored.Transfer.State.OperationId);
            Assert.Equal(1, scheduler.Calls);
            await Assert.ThrowsAsync<IntegrationTransferConflictException>(() => store.CreateAsync(transfer, plan with { RequestFingerprint = new string('b', 64) }, default));
        }
    }

    [Fact]
    public async Task StaleWriterCannotOverwriteVerifiedArtifactEvidence() {
        var transfer = IntegrationTransfer.CreateSourceDownload(Guid.NewGuid(), Guid.NewGuid());
        await using var db = new PrismediaDbContext(options);
        var store = new EfIntegrationTransferStore(db, new(root), new Scheduler());
        await store.CreateAsync(transfer, Plan, default);
        var stale = (await store.FindAsync(transfer.State.OperationId, default))!.Transfer;
        var artifact = new IntegrationArtifact("file", "source-item", "book.epub", "application/epub+zip", 100, new string('a', 64), IntegrationArtifactRole.Content);
        transfer.AcceptSourceArtifact(artifact);
        await store.SaveAsync(transfer, 1, null, default);
        stale.AcceptSourceArtifact(artifact with { SizeBytes = 200 });
        await Assert.ThrowsAsync<IntegrationTransferConflictException>(() => store.SaveAsync(stale, 1, null, default));
        Assert.Equal(100, (await store.FindAsync(transfer.State.OperationId, default))!.Transfer.State.Artifacts![0].SizeBytes);
    }

    [Fact]
    public void AcceptedIntentCiphertextCannotMoveBetweenOperations() {
        var protector = new TransferPlanProtector(root);
        var connection = Guid.NewGuid();
        var encrypted = protector.Protect(connection, Guid.NewGuid(), SensitiveLocator);
        Assert.Throws<IntegrationTransferPlanUnavailableException>(() => protector.Unprotect(connection, Guid.NewGuid(), encrypted));
    }

    private sealed class Scheduler : IIntegrationTransferScheduler {
        internal int Calls { get; private set; }
        public Task EnqueueAsync(Guid operationId, string title, CancellationToken cancellationToken) { Calls++; return Task.CompletedTask; }
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
