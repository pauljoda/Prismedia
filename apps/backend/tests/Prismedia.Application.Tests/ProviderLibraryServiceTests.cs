using Prismedia.Application.Integrations;
using Prismedia.Contracts.Integrations;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;

namespace Prismedia.Application.Tests;

public sealed class ProviderLibraryServiceTests {
    [Fact]
    public async Task ListsMultipleLibrariesFromEachConnectionAndIsolatesUnavailableProviders() {
        var fixture = new Fixture();
        fixture.Catalog = new([
            new("movies", "Movies", "/media/movies", [EntityKind.Movie], "https://manager.test/movies"),
            new("archive", "Archive", "/media/archive", [EntityKind.Movie]),
        ]);
        fixture.AddUnavailableConnection();

        var connections = await fixture.Service.ListAsync(default);

        Assert.Equal(2, connections.Count);
        Assert.Equal(2, Assert.Single(connections, item => item.Error is null).Libraries.Count);
        Assert.Empty(Assert.Single(connections, item => item.Error is not null).Libraries);
    }

    [Fact]
    public async Task RejectsDuplicateLibrariesUnsupportedKindsAndUnsafeManagementLinks() {
        var fixture = new Fixture();
        foreach (var catalog in new ProviderLibraryCatalog[] {
            new([new("same", "One", "/one", [EntityKind.Movie]), new("same", "Two", "/two", [EntityKind.Movie])]),
            new([new("book", "Books", "/books", [EntityKind.Book])]),
            new([new("movie", "Movies", "/movies", [EntityKind.Movie], "javascript:alert(1)")]),
            new([new("movie", "", "/movies", [EntityKind.Movie])]),
        }) {
            fixture.Catalog = catalog;
            await Assert.ThrowsAsync<IntegrationInvocationException>(() => fixture.Service.ListAsync(fixture.PrimaryId, default));
        }
    }

    private sealed class Fixture : IIntegrationConnectionStore, IIntegrationPluginGateway, IIntegrationLibraryGateway {
        private const string PluginId = "fixture-library";
        private readonly List<StoredIntegrationConnection> connections = [];
        private PluginManifest manifest;
        internal Guid PrimaryId { get; }
        internal ProviderLibraryCatalog Catalog { get; set; } = new([]);
        internal ProviderLibraryService Service { get; }

        internal Fixture() {
            var connection = CreateConnection("Primary", ready: true);
            PrimaryId = connection.Connection.State.Id;
            connections.Add(connection);
            var capability = new PluginIntegrationCapability(PluginCapability.ConnectedLibrary,
                [IntegrationOperation.ListLibraries], [EntityKind.Movie]);
            manifest = new(2, [], PluginId, "Fixture", "1.0.0", "dotnet-process", "plugin.dll",
                new("2.0.0", null, "3.8.0", null), [], false, [], Integration: new(1, [capability], []));
            Service = new(this, new(this, this), this);
        }

        internal void AddUnavailableConnection() => connections.Add(CreateConnection("Unavailable", ready: false));

        private static StoredIntegrationConnection CreateConnection(string name, bool ready) {
            var support = new IntegrationSupport(PluginCapability.ConnectedLibrary,
                [IntegrationOperation.ListLibraries], [EntityKind.Movie]);
            var connection = IntegrationConnection.Create(PluginId, name, "https://manager.test/", true,
                [PluginCapability.ConnectedLibrary], new Dictionary<string, string>());
            connection.RecordProbe(ready ? "fixture" : null, ready ? [support] : [], ready ? null : "offline", DateTimeOffset.UtcNow);
            return new(connection, []);
        }

        public Task<IReadOnlyList<StoredIntegrationConnection>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<StoredIntegrationConnection>>(connections);
        public Task<StoredIntegrationConnection?> FindAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(connections.FirstOrDefault(item => item.Connection.State.Id == id));
        public Task SaveAsync(IntegrationConnection connection, long? expectedRevision, IReadOnlyDictionary<string, string?> secretChanges, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<IReadOnlyDictionary<string, string>> ReadSecretsAsync(Guid id, IReadOnlyCollection<string> credentialKeys, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());
        public Task DeleteAsync(Guid id, long expectedRevision, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<PluginManifest?> FindAsync(string pluginId, CancellationToken cancellationToken) => Task.FromResult<PluginManifest?>(manifest);
        public Task<ConnectionProbeResult> ProbeAsync(string pluginId, IntegrationConnectionContext connection, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<ProviderLibraryCatalog> ListLibrariesAsync(string pluginId, IntegrationConnectionContext connection, CancellationToken cancellationToken) =>
            Task.FromResult(Catalog);
    }
}
