using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Integrations;
using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;
using Prismedia.Infrastructure.Integrations;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class EntityAcquisitionAttributionTests : IDisposable {
    private readonly string keys = Path.Combine(Path.GetTempPath(), "prismedia-attribution-" + Guid.NewGuid().ToString("N"));
    private static readonly CatalogAttribution Attribution = new("https://catalog.test/source", "Creator", "Credit", "CC BY 4.0", null, null, true);

    [Fact]
    public async Task ReadsOnlyTheExactImportedEntityAndRetainsTheAcceptedSnapshotAfterRestart() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var entityId = Guid.NewGuid();
        Guid operationId;
        await using (var db = database.CreateContext()) {
            operationId = await AddAsync(db, entityId, Attribution);
            var unrelated = await AddAsync(db, Guid.NewGuid(), Attribution);
            (await db.IntegrationTransfers.SingleAsync(row => row.Id == unrelated)).ProtectedPlan = "unreadable-unrelated-plan";
            await db.SaveChangesAsync();
        }
        await using var read = database.CreateContext();
        var reader = new EfEntityAcquisitionAttributionReader(read, new(keys));
        var result = await reader.ReadAsync(entityId, default);
        Assert.NotNull(result);
        Assert.False(result.Unavailable);
        var source = Assert.Single(result.Items);
        Assert.Equal(operationId, source.OperationId);
        Assert.Equal(Attribution, source.Attribution);
        Assert.Null(await reader.ReadAsync(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task OldImportsWithoutAttributionDoNotInventSourceStatements() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var entityId = Guid.NewGuid();
        await AddAsync(db, entityId, null);
        Assert.Null(await new EfEntityAcquisitionAttributionReader(db, new(keys)).ReadAsync(entityId, default));
    }

    [Fact]
    public async Task MissingKeysMarkSourceInformationUnavailableWithoutBreakingLibraryDetails() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var entityId = Guid.NewGuid();
        await AddAsync(db, entityId, Attribution);
        var result = await new EfEntityAcquisitionAttributionReader(db, new(Path.Combine(keys, "different-ring"))).ReadAsync(entityId, default);
        Assert.NotNull(result);
        Assert.True(result.Unavailable);
        Assert.Empty(result.Items);
    }

    private async Task<Guid> AddAsync(PrismediaDbContext db, Guid entityId, CatalogAttribution? attribution) {
        var connection = new IntegrationConnectionRow {
            Id = Guid.NewGuid(), PluginId = "catalog-test", Name = "Catalog", BaseUrl = "https://catalog.test/",
            Revision = 1, Status = ConnectionStatus.Unverified
        };
        db.IntegrationConnections.Add(connection);
        await db.SaveChangesAsync();
        var transfer = IntegrationTransfer.CreateSourceDownload(Guid.NewGuid(), connection.Id);
        var plan = new IntegrationTransferPlan("Book", EntityKind.Book, Guid.NewGuid(), Path.GetTempPath(), transfer.State.OperationId.ToString(),
            new string('a', 64), new(new("item", "private-source-locator", EntityKind.Book), "offer",
                attribution is null ? null : new("Book", null, [], new Dictionary<string, string>(), Attribution: attribution)));
        var store = new EfIntegrationTransferStore(db, new(keys), new Scheduler());
        await store.CreateAsync(transfer, plan, default);
        transfer.AcceptSourceArtifact(new("file", "item", "book.epub", "application/epub+zip", 100, new string('a', 64), IntegrationArtifactRole.Content));
        await store.SaveAsync(transfer, 1, null, default);
        transfer.RecordImported(new("file", new string('a', 64), [entityId]));
        await store.SaveAsync(transfer, 2, null, default);
        return transfer.State.OperationId;
    }

    private sealed class Scheduler : IIntegrationTransferScheduler {
        public Task EnqueueAsync(Guid operationId, string title, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    public void Dispose() { if (Directory.Exists(keys)) Directory.Delete(keys, true); }
}
