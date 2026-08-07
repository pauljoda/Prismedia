using Prismedia.Application.Acquisition;
using Prismedia.Contracts.Acquisition;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Tests.Acquisition;

public sealed class IndexerConfigCommandServiceTests {
    [Fact]
    public async Task HealthyConnectionTestClearsExistingBackoff() {
        var id = Guid.NewGuid();
        var statuses = new RecordingIndexerStatusStore();
        var service = Create(id, statuses, connected: true);

        var response = await service.TestAsync(
            new IndexerTestRequest(id, IndexerKind.Prowlarr, "http://prowlarr:9696", ApiKey: null),
            CancellationToken.None);

        Assert.True(response.Connected);
        Assert.Equal([id], statuses.Cleared);
    }

    [Fact]
    public async Task ManualRetryClearsBackoffForAConfiguredIndexer() {
        var id = Guid.NewGuid();
        var statuses = new RecordingIndexerStatusStore();
        var service = Create(id, statuses, connected: false);

        var found = await service.RetryNowAsync(id, CancellationToken.None);

        Assert.True(found);
        Assert.Equal([id], statuses.Cleared);
    }

    private static IndexerConfigCommandService Create(
        Guid id,
        RecordingIndexerStatusStore statuses,
        bool connected) =>
        new(
            new StubIndexerConfigStore(id),
            new StubIndexerClientFactory(connected),
            statuses);

    private sealed class StubIndexerConfigStore(Guid id) : IIndexerConfigStore {
        private readonly IndexerConfigDetail _detail = new(
            id,
            IndexerKind.Prowlarr,
            "Prowlarr",
            "http://prowlarr:9696",
            Enabled: true,
            Priority: 25,
            Categories: [],
            HasApiKey: true,
            ApiKey: "secret");

        public Task<IndexerConfigDetail?> GetAsync(Guid candidate, CancellationToken cancellationToken) =>
            Task.FromResult<IndexerConfigDetail?>(candidate == id ? _detail : null);

        public Task<IReadOnlyList<IndexerConfigSummary>> ListAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<IndexerConfigDetail>> ListDetailsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IndexerConfigSummary> SaveAsync(IndexerConfigSaveCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(Guid candidate, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubIndexerClientFactory(bool connected) : IIndexerSearchClientFactory {
        public IIndexerSearchClient Get(IndexerKind kind) => new StubIndexerClient(connected);
    }

    private sealed class StubIndexerClient(bool connected) : IIndexerSearchClient {
        public IndexerKind Kind => IndexerKind.Prowlarr;

        public Task<IndexerConnectionTest> TestAsync(IndexerConnection connection, CancellationToken cancellationToken) =>
            Task.FromResult(new IndexerConnectionTest(connected, connected ? "Connected" : "Unavailable"));

        public Task<IReadOnlyList<IndexerRelease>> SearchAsync(
            IndexerConnection connection,
            IndexerQuery query,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RecordingIndexerStatusStore : IIndexerStatusStore {
        public List<Guid> Cleared { get; } = [];

        public Task ClearAsync(Guid indexerConfigId, CancellationToken cancellationToken) {
            Cleared.Add(indexerConfigId);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyDictionary<Guid, IndexerHealth>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, IndexerHealth>>(new Dictionary<Guid, IndexerHealth>());

        public Task RecordFailureAsync(Guid indexerConfigId, string message, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task RecordSuccessAsync(Guid indexerConfigId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
