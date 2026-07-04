using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Jobs;
using Prismedia.Application.Jobs.Handlers;
using Prismedia.Application.Requests;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Acquisition;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

/// <summary>
/// Covers the request-time metadata enrichment handler: it fills the held acquisition's gaps from the
/// provider lookup, skips when there is no plugin id to look up, and is a best-effort no-op when the provider
/// returns nothing.
/// </summary>
public sealed class AcquisitionEnrichJobHandlerTests {
    [Fact]
    public async Task FillsGapsFromTheProviderLookup() {
        await using var db = CreateContext();
        var id = await SeedAsync(db, pluginId: "openlibrary", pluginItemId: "OL123W", posterUrl: null);
        var enricher = new FakeEnricher(new RequestMetadataEnrichment("A fuller description from the provider.", "http://covers/OL123W.jpg", 2024));

        await RunAsync(db, enricher, id);

        var row = await db.Acquisitions.AsNoTracking().FirstAsync(a => a.Id == id);
        Assert.Equal("http://covers/OL123W.jpg", row.PosterUrl);
        Assert.Equal("A fuller description from the provider.", row.Description);
        Assert.Equal(2024, row.Year);
        Assert.Equal("openlibrary:OL123W", enricher.LookedUp);
    }

    [Fact]
    public async Task SkipsWhenThereIsNoPluginToLookUp() {
        await using var db = CreateContext();
        var id = await SeedAsync(db, pluginId: null, pluginItemId: null, posterUrl: null);
        var enricher = new FakeEnricher(new RequestMetadataEnrichment("x", "y", 1));

        await RunAsync(db, enricher, id);

        Assert.Null(enricher.LookedUp); // never queried the provider
        Assert.Null((await db.Acquisitions.AsNoTracking().FirstAsync(a => a.Id == id)).PosterUrl);
    }

    [Fact]
    public async Task ProviderMissLeavesHeldMetadataUntouched() {
        await using var db = CreateContext();
        var id = await SeedAsync(db, pluginId: "openlibrary", pluginItemId: "OL9W", posterUrl: "http://held");
        var enricher = new FakeEnricher(null); // provider couldn't resolve it

        await RunAsync(db, enricher, id);

        Assert.Equal("http://held", (await db.Acquisitions.AsNoTracking().FirstAsync(a => a.Id == id)).PosterUrl);
    }

    private static async Task RunAsync(PrismediaDbContext db, FakeEnricher enricher, Guid id) {
        var handler = new AcquisitionEnrichJobHandler(AcquisitionTestFactory.Store(db), enricher, NullLogger<AcquisitionEnrichJobHandler>.Instance);
        var job = new JobRunSnapshot(
            Guid.NewGuid(), JobType.AcquisitionEnrich, JobRunStatus.Running, 0, null,
            AcquisitionJobPayload.Serialize(id), null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null);
        await handler.HandleAsync(new JobContext(job, new NoopJobQueue()), CancellationToken.None);
    }

    private static async Task<Guid> SeedAsync(PrismediaDbContext db, string? pluginId, string? pluginItemId, string? posterUrl) {
        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();
        db.Acquisitions.Add(new AcquisitionRow {
            Id = id, Status = AcquisitionStatus.Pending, Title = "B", ExternalIdsJson = "{}", SourceUrlsJson = "[]",
            PluginId = pluginId, PluginItemId = pluginItemId, PosterUrl = posterUrl, CreatedAt = now, UpdatedAt = now
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static PrismediaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class FakeEnricher(RequestMetadataEnrichment? result) : IRequestMetadataEnricher {
        public string? LookedUp { get; private set; }
        public Task<RequestMetadataEnrichment?> LookupByIdAsync(EntityKind kind, string providerId, string externalId, bool hideNsfw, CancellationToken cancellationToken) {
            LookedUp = $"{providerId}:{externalId}";
            return Task.FromResult(result);
        }
    }

    private sealed class NoopJobQueue : IJobQueueService {
        public Task<JobRunSnapshot> EnqueueAsync(EnqueueJobRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
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
