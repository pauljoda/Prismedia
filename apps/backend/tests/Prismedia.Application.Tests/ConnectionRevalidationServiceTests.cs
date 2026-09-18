using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Prismedia.Application.Integrations;
using Prismedia.Contracts.Integrations;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;

namespace Prismedia.Application.Tests;

public sealed class ConnectionRevalidationServiceTests {
    private static readonly PluginIntegrationCapability Capability = new(
        PluginCapability.CatalogDiscovery,
        [IntegrationOperation.Search],
        [EntityKind.Book]);

    [Fact]
    public async Task PendingConnectionsRevalidateIndependentlyAndPreserveRemoteIdentitySecurity() {
        var ready = Create("Ready");
        var unavailable = Create("Unavailable");
        var deferred = Create("Deferred");
        var replaced = new IntegrationConnection(new(
            Guid.NewGuid(), "fixture", "Replaced", "https://replaced.test/", true,
            [PluginCapability.CatalogDiscovery], new Dictionary<string, string>(), 4,
            ConnectionStatus.Unverified, "original-instance", [], null, null,
            HasPersistentRemoteIdentity: true));
        var disabled = Create("Disabled", enabled: false);
        var store = new RecordingConnectionStore([ready, unavailable, deferred, replaced, disabled]);
        var gateway = new RecordingGateway(
            unavailable.State.Id,
            deferred.State.Id,
            replaced.State.Id);
        var revalidation = new ConnectionRevalidationService(
            new RecordingScopeFactory(store, gateway),
            NullLogger<ConnectionRevalidationService>.Instance);

        var result = await revalidation.DrainBatchAsync(default);

        Assert.Equal(new ConnectionRevalidationResult(1, 2, 1, false), result);
        Assert.Equal(ConnectionStatus.Ready, store.Find(ready.State.Id).State.Status);
        Assert.Equal(ConnectionStatus.Unavailable, store.Find(unavailable.State.Id).State.Status);
        Assert.Equal(ConnectionStatus.Unverified, store.Find(deferred.State.Id).State.Status);
        Assert.Equal(ConnectionStatus.IdentityChanged, store.Find(replaced.State.Id).State.Status);
        Assert.Equal("original-instance", store.Find(replaced.State.Id).State.RemoteInstanceId);
        Assert.Empty(store.Find(replaced.State.Id).State.EffectiveCapabilities);
        Assert.Equal(ConnectionStatus.Disabled, store.Find(disabled.State.Id).State.Status);
        Assert.DoesNotContain(disabled.State.Id, gateway.ProbedIds);
    }

    [Fact]
    public async Task DeferredBatchDoesNotStarveLaterConnections() {
        var pending = Enumerable.Range(1, 9)
            .Select(index => Create($"Pending {index}", new Guid(index, 0, 0, new byte[8])))
            .ToArray();
        var deferredIds = pending.Take(8).Select(item => item.State.Id).ToHashSet();
        var store = new RecordingConnectionStore(pending);
        var gateway = new RecordingGateway(deferredIds: deferredIds);
        var revalidation = new ConnectionRevalidationService(
            new RecordingScopeFactory(store, gateway),
            NullLogger<ConnectionRevalidationService>.Instance);

        var first = await revalidation.DrainBatchAsync(default);
        var second = await revalidation.DrainBatchAsync(default);

        Assert.Equal(new ConnectionRevalidationResult(0, 0, 8, true), first);
        Assert.Equal(1, second.Ready);
        Assert.Contains(pending[^1].State.Id, gateway.ProbedIds);
        Assert.Equal(ConnectionStatus.Ready, store.Find(pending[^1].State.Id).State.Status);
    }

    private static IntegrationConnection Create(string name, bool enabled = true) =>
        Create(name, Guid.NewGuid(), enabled);

    private static IntegrationConnection Create(string name, Guid id, bool enabled = true) => new(new(
        id, "fixture", name, $"https://{name.Replace(' ', '-').ToLowerInvariant()}.test/", enabled,
        [PluginCapability.CatalogDiscovery], new Dictionary<string, string>(), 1,
        enabled ? ConnectionStatus.Unverified : ConnectionStatus.Disabled,
        null, [], null, null, HasPersistentRemoteIdentity: false));

    private sealed class RecordingGateway(
        Guid? unavailableId = null,
        Guid? deferredId = null,
        Guid? replacedId = null,
        IReadOnlySet<Guid>? deferredIds = null)
        : IIntegrationPluginGateway {
        public List<Guid> ProbedIds { get; } = [];

        public Task<PluginManifest?> FindAsync(string pluginId, CancellationToken cancellationToken) =>
            Task.FromResult<PluginManifest?>(new(
                2, ["prismedia"], pluginId, "Fixture", "1.0.0", "dotnet-process", "fixture.dll",
                new("2.0.0", null, "3.8.0", null), [], false, [],
                Integration: new(1, [Capability], [])));

        public Task<ConnectionProbeResult> ProbeAsync(
            string pluginId,
            IntegrationConnectionContext connection,
            CancellationToken cancellationToken) {
            lock (ProbedIds) ProbedIds.Add(connection.Id);
            if (connection.Id == unavailableId)
                throw new IntegrationInvocationException("The application is offline.");
            if (connection.Id == deferredId || deferredIds?.Contains(connection.Id) == true)
                throw new IOException("Transient persistence fixture.");
            return Task.FromResult(new ConnectionProbeResult(
                connection.Id == replacedId ? "replacement-instance" : "ready-instance",
                "Fixture",
                "1.0.0",
                [Capability]));
        }
    }

    private sealed class RecordingConnectionStore(IEnumerable<IntegrationConnection> connections)
        : IIntegrationConnectionStore {
        private readonly object gate = new();
        private readonly Dictionary<Guid, IntegrationConnectionState> values = connections
            .ToDictionary(item => item.State.Id, item => item.State);

        public IntegrationConnection Find(Guid id) { lock (gate) return new(values[id]); }

        public Task<IReadOnlyList<StoredIntegrationConnection>> ListAsync(CancellationToken cancellationToken) {
            lock (gate) return Task.FromResult<IReadOnlyList<StoredIntegrationConnection>>(
                values.Values.Select(value => new StoredIntegrationConnection(new(value), [])).ToArray());
        }

        public Task<StoredIntegrationConnection?> FindAsync(Guid id, CancellationToken cancellationToken) {
            lock (gate) return Task.FromResult(values.TryGetValue(id, out var value)
                ? new StoredIntegrationConnection(new(value), [])
                : null);
        }

        public Task SaveAsync(IntegrationConnection connection, long? expectedRevision,
            IReadOnlyDictionary<string, string?> secretChanges, CancellationToken cancellationToken) {
            lock (gate) {
                if (!values.TryGetValue(connection.State.Id, out var current)
                    || current.Revision != expectedRevision
                    || expectedRevision is null
                    || connection.State.Revision != expectedRevision.Value + 1)
                    throw new ConnectionConflictException();
                values[connection.State.Id] = connection.State;
            }
            return Task.CompletedTask;
        }

        public Task<IReadOnlyDictionary<string, string>> ReadSecretsAsync(Guid id,
            IReadOnlyCollection<string> credentialKeys, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());

        public Task DeleteAsync(Guid id, long expectedRevision, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingScopeFactory(
        IIntegrationConnectionStore store,
        IIntegrationPluginGateway gateway) : IServiceScopeFactory {
        public IServiceScope CreateScope() => new RecordingScope(new RecordingProvider(store, gateway));
    }

    private sealed class RecordingScope(IServiceProvider serviceProvider) : IServiceScope {
        public IServiceProvider ServiceProvider { get; } = serviceProvider;
        public void Dispose() { }
    }

    private sealed class RecordingProvider(
        IIntegrationConnectionStore store,
        IIntegrationPluginGateway gateway) : IServiceProvider {
        public object? GetService(Type serviceType) {
            if (serviceType == typeof(IIntegrationConnectionStore)) return store;
            if (serviceType == typeof(ConnectionService)) return new ConnectionService(store, gateway);
            return null;
        }
    }
}
