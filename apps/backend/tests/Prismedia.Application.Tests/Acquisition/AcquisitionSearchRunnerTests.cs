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
            new AcquisitionDecisionEngineFactory([new BookReleaseDecisionEngine()]));

        var outcome = await runner.RunAsync(new AcquisitionSearchInput(Guid.NewGuid(), "Book", null), CancellationToken.None);

        var blockedResult = outcome.Candidates.Single(candidate => candidate.Release.Title == "Blocked Book (epub)");
        var cleanResult = outcome.Candidates.Single(candidate => candidate.Release.Title == "Clean Book (epub)");
        Assert.False(blockedResult.Accepted);
        Assert.Contains(ReleaseRejectionReason.Blocklisted, blockedResult.Rejections);
        Assert.True(cleanResult.Accepted);
    }

    [Fact]
    public async Task QueryLadderFallsThroughToTheBroaderRungWhenNothingAcceptableIsFound() {
        // The context-rich rung ("Book Author") returns nothing; the bare-title rung finds the release.
        var release = new IndexerRelease("Book (epub)", 5_000_000, 60, 3, DownloadProtocol.Torrent, "http://dl", "magnet:?c", "hash", "http://info", null, null);
        var client = new QueryAwareIndexerSearchClient(new Dictionary<string, IReadOnlyList<IndexerRelease>>(StringComparer.OrdinalIgnoreCase) {
            ["Book"] = [release],
        });

        var runner = new AcquisitionSearchRunner(
            new FakeIndexerConfigStore(),
            new FakeClientFactory(client),
            new FakeProfileStore(),
            new FakeBlocklistStore("unrelated"),
            new AcquisitionDecisionEngineFactory([new BookReleaseDecisionEngine()]));

        var outcome = await runner.RunAsync(new AcquisitionSearchInput(Guid.NewGuid(), "Book", "Author"), CancellationToken.None);

        Assert.Equal(["Book Author", "Book"], client.Queries.ToArray());
        Assert.Single(outcome.Candidates, candidate => candidate.Accepted);
    }

    [Fact]
    public async Task QueryLadderStopsAtTheFirstRungWithAnAcceptableRelease() {
        var release = new IndexerRelease("Book (epub)", 5_000_000, 60, 3, DownloadProtocol.Torrent, "http://dl", "magnet:?c", "hash", "http://info", null, null);
        var client = new QueryAwareIndexerSearchClient(new Dictionary<string, IReadOnlyList<IndexerRelease>>(StringComparer.OrdinalIgnoreCase) {
            ["Book Author"] = [release],
        });

        var runner = new AcquisitionSearchRunner(
            new FakeIndexerConfigStore(),
            new FakeClientFactory(client),
            new FakeProfileStore(),
            new FakeBlocklistStore("unrelated"),
            new AcquisitionDecisionEngineFactory([new BookReleaseDecisionEngine()]));

        await runner.RunAsync(new AcquisitionSearchInput(Guid.NewGuid(), "Book", "Author"), CancellationToken.None);

        Assert.Equal(["Book Author"], client.Queries.ToArray());
    }

    [Fact]
    public void QueryLadderBuildsContextRichRungsPerKind() {
        Assert.Equal(["Book Author", "Book"],
            ReleaseQueryLadder.For(new AcquisitionSearchInput(Guid.NewGuid(), "Book", "Author")).ToArray());
        Assert.Equal(["Daft Punk Discovery", "Discovery"],
            ReleaseQueryLadder.For(new AcquisitionSearchInput(Guid.NewGuid(), "Discovery", "Daft Punk", EntityKind.AudioLibrary)).ToArray());
        Assert.Equal(["Game of Thrones complete", "Game of Thrones"],
            ReleaseQueryLadder.For(new AcquisitionSearchInput(Guid.NewGuid(), "Game of Thrones", null, EntityKind.VideoSeries)).ToArray());
        // Movies lead with the year when it is known, and collapse to one rung when it isn't.
        Assert.Equal(["Dune 2021", "Dune"],
            ReleaseQueryLadder.For(new AcquisitionSearchInput(Guid.NewGuid(), "Dune", null, EntityKind.Movie, Year: 2021)).ToArray());
        Assert.Equal(["Dune"],
            ReleaseQueryLadder.For(new AcquisitionSearchInput(Guid.NewGuid(), "Dune", null, EntityKind.Movie)).ToArray());
    }

    private sealed class QueryAwareIndexerSearchClient(IReadOnlyDictionary<string, IReadOnlyList<IndexerRelease>> byQuery) : IIndexerSearchClient {
        public List<string> Queries { get; } = [];
        public IndexerKind Kind => IndexerKind.Prowlarr;
        public Task<IReadOnlyList<IndexerRelease>> SearchAsync(IndexerConnection connection, IndexerQuery query, CancellationToken cancellationToken) {
            Queries.Add(query.Text);
            return Task.FromResult(byQuery.GetValueOrDefault(query.Text) ?? []);
        }
        public Task<IndexerConnectionTest> TestAsync(IndexerConnection connection, CancellationToken cancellationToken) => throw new NotSupportedException();
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
