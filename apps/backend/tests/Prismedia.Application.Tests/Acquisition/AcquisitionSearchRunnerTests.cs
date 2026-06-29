using Prismedia.Application.Acquisition;
using Prismedia.Contracts.Acquisition;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Tests.Acquisition;

/// <summary>
/// Pins that the search runner threads the blocklist into the decision engine: a blocklisted release
/// is rejected while a non-blocklisted one in the same batch stays accepted. Guards against silently
/// dropping the blocklist argument back to its default.
/// </summary>
public sealed class AcquisitionSearchRunnerTests {
    [Fact]
    public async Task RejectsBlocklistedReleaseAndKeepsCleanOneAccepted() {
        var blocked = new IndexerRelease("Blocked Book (epub)", 5_000_000, 80, 4, DownloadProtocol.Torrent, "http://dl", "magnet:?b", "blockedhash", "http://info", null, null);
        var clean = new IndexerRelease("Clean Book (epub)", 5_000_000, 60, 3, DownloadProtocol.Torrent, "http://dl", "magnet:?c", "cleanhash", "http://info", null, null);

        var runner = new AcquisitionSearchRunner(
            new FakeIndexerConfigStore(),
            new FakeClientFactory(new FakeIndexerSearchClient([blocked, clean])),
            new FakeProfileStore(),
            new FakeBlocklistStore(ReleaseIdentity.For("blockedhash", null, null)),
            new BookReleaseDecisionEngine());

        var outcome = await runner.RunAsync(new AcquisitionSearchInput(Guid.NewGuid(), "Book", null), CancellationToken.None);

        var blockedResult = outcome.Candidates.Single(candidate => candidate.Release.Title == "Blocked Book (epub)");
        var cleanResult = outcome.Candidates.Single(candidate => candidate.Release.Title == "Clean Book (epub)");
        Assert.False(blockedResult.Accepted);
        Assert.Contains(ReleaseRejectionReason.Blocklisted, blockedResult.Rejections);
        Assert.True(cleanResult.Accepted);
    }

    private sealed class FakeIndexerConfigStore : IIndexerConfigStore {
        public Task<IReadOnlyList<IndexerConfigDetail>> ListDetailsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<IndexerConfigDetail>>(
                [new IndexerConfigDetail(Guid.NewGuid(), IndexerKind.Prowlarr, "Indexer", "http://x", true, 25, [], true, "key")]);

        public Task<IReadOnlyList<IndexerConfigSummary>> ListAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IndexerConfigDetail?> GetAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IndexerConfigSummary> SaveAsync(IndexerConfigSaveCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeClientFactory(IIndexerSearchClient client) : IIndexerSearchClientFactory {
        public IIndexerSearchClient Get(IndexerKind kind) => client;
    }

    private sealed class FakeIndexerSearchClient(IReadOnlyList<IndexerRelease> releases) : IIndexerSearchClient {
        public IndexerKind Kind => IndexerKind.Prowlarr;
        public Task<IReadOnlyList<IndexerRelease>> SearchAsync(IndexerConnection connection, IndexerQuery query, CancellationToken cancellationToken) =>
            Task.FromResult(releases);
        public Task<IndexerConnectionTest> TestAsync(IndexerConnection connection, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeProfileStore : IBookAcquisitionProfileStore {
        public Task<BookAcquisitionRules> GetDefaultRulesAsync(CancellationToken cancellationToken) => Task.FromResult(BookAcquisitionRules.Default);
        public Task<BookImportProfile?> GetDefaultImportProfileAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> GetDefaultAutoPickAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> GetDefaultAutoRedownloadAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<BookAcquisitionProfileView>> ListAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BookAcquisitionProfileView?> GetAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BookAcquisitionProfileView> SaveAsync(BookAcquisitionProfileSaveCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeBlocklistStore(string identity) : IAcquisitionBlocklistStore {
        public Task<IReadOnlySet<string>> GetIdentitiesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<string>>(new HashSet<string> { identity });
        public Task AddAsync(BlocklistAddRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<AcquisitionBlocklistEntry>> ListAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
