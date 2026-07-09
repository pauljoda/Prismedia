using Prismedia.Application.Acquisition;
using Prismedia.Application.Settings;
using Prismedia.Contracts.Settings;
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
            new FakeDownloadClientConfigStore(DownloadProtocol.Torrent),
            new FakeIndexerStatusStore(),
            new IndexerQueryWindow(),
            Policies(new BookAcquisitionPolicyModule()),
            Settings());

        var outcome = await runner.RunAsync(new AcquisitionSearchInput(Guid.NewGuid(), "Book", null), CancellationToken.None);

        var blockedResult = outcome.Candidates.Single(candidate => candidate.Release.Title == "Blocked Book (epub)");
        var cleanResult = outcome.Candidates.Single(candidate => candidate.Release.Title == "Clean Book (epub)");
        Assert.False(blockedResult.Accepted);
        Assert.Contains(ReleaseRejectionReason.Blocklisted, blockedResult.Rejections);
        Assert.True(cleanResult.Accepted);
    }

    [Fact]
    public async Task UsenetReleasesAreAcceptedOnlyWhenAUsenetClientIsEnabled() {
        var usenet = new IndexerRelease("Usenet Book (epub)", 5_000_000, null, null, DownloadProtocol.Usenet, "http://dl/nzb", null, null, null, null, null);

        async Task<ScoredRelease> SearchWith(params DownloadProtocol[] protocols) {
            var runner = new AcquisitionSearchRunner(
                new FakeIndexerConfigStore(),
                new FakeClientFactory(new FakeIndexerSearchClient([usenet])),
                new FakeProfileStore(),
                new FakeBlocklistStore("unrelated"),
                new FakeDownloadClientConfigStore(protocols),
                new FakeIndexerStatusStore(),
                new IndexerQueryWindow(),
                Policies(new BookAcquisitionPolicyModule()),
                Settings());
            var outcome = await runner.RunAsync(new AcquisitionSearchInput(Guid.NewGuid(), "Book", null), CancellationToken.None);
            return outcome.Candidates.Single();
        }

        // Torrent-only setup: the usenet release is visible but rejected as the wrong protocol.
        var torrentOnly = await SearchWith(DownloadProtocol.Torrent);
        Assert.False(torrentOnly.Accepted);
        Assert.Contains(ReleaseRejectionReason.WrongProtocol, torrentOnly.Rejections);

        // A usenet client (e.g. SABnzbd) makes the same release acceptable.
        var withUsenet = await SearchWith(DownloadProtocol.Torrent, DownloadProtocol.Usenet);
        Assert.True(withUsenet.Accepted);

        // No clients configured at all keeps the permissive torrent-only default (candidates still surface).
        var noClients = await SearchWith();
        Assert.Contains(ReleaseRejectionReason.WrongProtocol, noClients.Rejections);
    }

    [Fact]
    public async Task QueryLadderFallsThroughToTheBroaderRungWhenNothingAcceptableIsFound() {
        // The context-rich rung ("Book Author") returns nothing; the bare-title rung finds the release.
        var release = new IndexerRelease("Author - Book (epub)", 5_000_000, 60, 3, DownloadProtocol.Torrent, "http://dl", "magnet:?c", "hash", "http://info", null, null);
        var client = new QueryAwareIndexerSearchClient(new Dictionary<string, IReadOnlyList<IndexerRelease>>(StringComparer.OrdinalIgnoreCase) {
            ["Book"] = [release],
        });

        var runner = new AcquisitionSearchRunner(
            new FakeIndexerConfigStore(),
            new FakeClientFactory(client),
            new FakeProfileStore(),
            new FakeBlocklistStore("unrelated"),
            new FakeDownloadClientConfigStore(DownloadProtocol.Torrent),
            new FakeIndexerStatusStore(),
            new IndexerQueryWindow(),
            Policies(new BookAcquisitionPolicyModule()),
            Settings());

        var outcome = await runner.RunAsync(new AcquisitionSearchInput(Guid.NewGuid(), "Book", "Author"), CancellationToken.None);

        Assert.Equal(["Book Author", "Book"], client.Queries.ToArray());
        Assert.Single(outcome.Candidates, candidate => candidate.Accepted);
    }

    [Fact]
    public async Task QueryLadderStopsAtTheFirstRungWithAnAcceptableRelease() {
        var release = new IndexerRelease("Author - Book (epub)", 5_000_000, 60, 3, DownloadProtocol.Torrent, "http://dl", "magnet:?c", "hash", "http://info", null, null);
        var client = new QueryAwareIndexerSearchClient(new Dictionary<string, IReadOnlyList<IndexerRelease>>(StringComparer.OrdinalIgnoreCase) {
            ["Book Author"] = [release],
        });

        var runner = new AcquisitionSearchRunner(
            new FakeIndexerConfigStore(),
            new FakeClientFactory(client),
            new FakeProfileStore(),
            new FakeBlocklistStore("unrelated"),
            new FakeDownloadClientConfigStore(DownloadProtocol.Torrent),
            new FakeIndexerStatusStore(),
            new IndexerQueryWindow(),
            Policies(new BookAcquisitionPolicyModule()),
            Settings());

        await runner.RunAsync(new AcquisitionSearchInput(Guid.NewGuid(), "Book", "Author"), CancellationToken.None);

        Assert.Equal(["Book Author"], client.Queries.ToArray());
    }

    [Fact]
    public void QueryLadderBuildsContextRichRungsPerKind() {
        var policies = Policies(
            new BookAcquisitionPolicyModule(),
            new MovieAcquisitionPolicyModule(),
            new MusicAcquisitionPolicyModule(),
            new TvAcquisitionPolicyModule());
        var book = new AcquisitionSearchInput(Guid.NewGuid(), "Book", "Author");
        var album = new AcquisitionSearchInput(Guid.NewGuid(), "Discovery", "Daft Punk", EntityKind.AudioLibrary);
        var series = new AcquisitionSearchInput(Guid.NewGuid(), "Game of Thrones", null, EntityKind.VideoSeries);
        var movie = new AcquisitionSearchInput(Guid.NewGuid(), "Dune", null, EntityKind.Movie, Year: 2021);
        var movieWithoutYear = new AcquisitionSearchInput(Guid.NewGuid(), "Dune", null, EntityKind.Movie);

        Assert.Equal(["Book Author", "Book"],
            policies.Get(book.Kind).BuildQueries(book).ToArray());
        Assert.Equal(["Daft Punk Discovery", "Discovery"],
            policies.Get(album.Kind).BuildQueries(album).ToArray());
        Assert.Equal(["Game of Thrones complete", "Game of Thrones"],
            policies.Get(series.Kind).BuildQueries(series).ToArray());
        // Movies lead with the year when it is known, and collapse to one rung when it isn't.
        Assert.Equal(["Dune 2021", "Dune"],
            policies.Get(movie.Kind).BuildQueries(movie).ToArray());
        Assert.Equal(["Dune"],
            policies.Get(movieWithoutYear.Kind).BuildQueries(movieWithoutYear).ToArray());
    }

    [Fact]
    public async Task ProperPolicyFromSettingsReachesScoring() {
        // A movie search with two same-quality releases — one a PROPER — under a DoNotPrefer override: the
        // revision boost is suppressed, so the higher-seeded plain release wins. Proves the app setting is
        // decoded and threaded into the pure scoring functions via the rules.
        var proper = new IndexerRelease("Movie 1080p BluRay PROPER", 5_000_000_000, 50, 5, DownloadProtocol.Torrent, "http://dl", null, "properhash", "http://i", null, null);
        var plain = new IndexerRelease("Movie 1080p BluRay", 5_000_000_000, 900, 5, DownloadProtocol.Torrent, "http://dl", null, "plainhash", "http://i", null, null);

        var runner = new AcquisitionSearchRunner(
            new FakeIndexerConfigStore(),
            new FakeClientFactory(new FakeIndexerSearchClient([proper, plain])),
            new FakeProfileStore(),
            new FakeBlocklistStore("unrelated"),
            new FakeDownloadClientConfigStore(DownloadProtocol.Torrent),
            new FakeIndexerStatusStore(),
            new IndexerQueryWindow(),
            Policies(new MovieAcquisitionPolicyModule()),
            Settings(ProperDownloadPolicy.DoNotPrefer));

        var outcome = await runner.RunAsync(new AcquisitionSearchInput(Guid.NewGuid(), "Movie", null, EntityKind.Movie), CancellationToken.None);

        // DoNotPrefer drops the proper's revision boost, so seeders break the same-quality tie for the plain release.
        Assert.Equal("Movie 1080p BluRay", outcome.Candidates.First(c => c.Accepted).Release.Title);
    }

    [Fact]
    public async Task SearchRejectsASubtitledSpinOffAndPicksTheExactSeriesTitle() {
        var exact = new IndexerRelease("How.Its.Made.S02.720p.HDTV", 5_000_000_000, 2, 2, DownloadProtocol.Torrent, "http://dl", null, "exact", "http://i", null, null);
        var subtitle = new IndexerRelease("How.Its.Made.Cars.S02.1080p.WEB-DL", 5_000_000_000, 900, 20, DownloadProtocol.Torrent, "http://dl", null, "subtitle", "http://i", null, null);

        var runner = new AcquisitionSearchRunner(
            new FakeIndexerConfigStore(),
            new FakeClientFactory(new FakeIndexerSearchClient([subtitle, exact])),
            new FakeProfileStore(),
            new FakeBlocklistStore("unrelated"),
            new FakeDownloadClientConfigStore(DownloadProtocol.Torrent),
            new FakeIndexerStatusStore(),
            new IndexerQueryWindow(),
            Policies(new TvAcquisitionPolicyModule()),
            Settings());

        var outcome = await runner.RunAsync(
            new AcquisitionSearchInput(Guid.NewGuid(), "Season 2", null, EntityKind.VideoSeason, Series: "How it's Made", SeasonNumber: 2),
            CancellationToken.None);

        // The spin-off names a DIFFERENT work, so it is rejected outright (not merely outranked) —
        // otherwise it would win the auto-pick whenever the exact title is absent or lower quality.
        Assert.Equal(exact.Title, outcome.Candidates[0].Release.Title);
        Assert.True(outcome.Candidates[0].Accepted);
        var spinOff = outcome.Candidates.Single(candidate => candidate.Release.Title == subtitle.Title);
        Assert.False(spinOff.Accepted);
        Assert.Contains(ReleaseRejectionReason.TitleMismatch, spinOff.Rejections);
    }

    /// <summary>Builds a real SettingsService over an in-memory override map; an unset AcquisitionDownloadPropers defaults to prefer-and-upgrade.</summary>
    private static SettingsService Settings(ProperDownloadPolicy? policy = null) {
        var overrides = new Dictionary<string, string>(StringComparer.Ordinal);
        if (policy is { } chosen) {
            overrides[AppSettingKeys.AcquisitionDownloadPropers] = System.Text.Json.JsonSerializer.Serialize(chosen.ToCode());
        }

        return new SettingsService(new FakeSettingsPersistence(overrides));
    }

    private static AcquisitionPolicyRegistry Policies(params IAcquisitionPolicyModule[] modules) =>
        new(modules);

    private sealed class FakeSettingsPersistence(IReadOnlyDictionary<string, string> overrides) : ISettingsPersistence {
        public Task<IReadOnlyDictionary<string, string>> LoadSettingOverridesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(overrides);
        public Task SaveSettingOverrideAsync(string key, string valueJson, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SaveSettingOverridesAsync(IReadOnlyDictionary<string, string> values, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ReplaceSettingOverridesAsync(IReadOnlyDictionary<string, string> upserts, IReadOnlyCollection<string> deletes, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteSettingOverrideAsync(string key, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<LibraryRoot>> ListLibraryRootsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<LibraryRoot?> GetLibraryRootAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<LibraryRoot> AddLibraryRootAsync(LibraryRoot state, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<LibraryRoot> SaveLibraryRootAsync(LibraryRoot state, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> DeleteLibraryRootAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
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
        public Task<BookAcquisitionRules> GetRulesAsync(Guid? profileId, EntityKind kind, CancellationToken cancellationToken) => Task.FromResult(BookAcquisitionRules.Default);
        public Task<BookImportProfile?> GetImportProfileAsync(Guid? profileId, EntityKind kind, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> GetAutoPickAsync(Guid? profileId, EntityKind kind, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> GetAutoRedownloadAsync(Guid? profileId, EntityKind kind, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string?> GetDownloadCategoryAsync(Guid? profileId, EntityKind kind, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
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

    private sealed class FakeIndexerStatusStore : IIndexerStatusStore {
        public Task<IReadOnlyDictionary<Guid, IndexerHealth>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, IndexerHealth>>(new Dictionary<Guid, IndexerHealth>());
        public Task RecordFailureAsync(Guid indexerConfigId, string message, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RecordSuccessAsync(Guid indexerConfigId, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>Supplies only the enabled-protocol set the runner consults; everything else is unused by the search path.</summary>
    private sealed class FakeDownloadClientConfigStore(params DownloadProtocol[] protocols) : IDownloadClientConfigStore {
        public Task<IReadOnlyList<DownloadProtocol>> GetEnabledProtocolsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DownloadProtocol>>(protocols);
        public Task<IReadOnlyList<DownloadClientSummary>> ListAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<DownloadClientDetail>> ListDetailsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<DownloadClientDetail?> GetAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<DownloadClientDetail?> GetDefaultAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<DownloadClientDetail?> GetDefaultAsync(DownloadProtocol protocol, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<DownloadClientDetail>> ListEnabledAsync(DownloadProtocol protocol, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<DownloadClientSummary> SaveAsync(DownloadClientSaveCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
