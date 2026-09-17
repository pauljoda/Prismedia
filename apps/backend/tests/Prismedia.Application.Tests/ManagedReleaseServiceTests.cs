using Prismedia.Application.Integrations;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Integrations;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;

namespace Prismedia.Application.Tests;

public sealed class ManagedReleaseServiceTests {
    [Fact]
    public async Task OutageRetainsFrozenOwnerAndRecoveryUsesTheSameIntent() {
        var fixture = new Fixture { Offline = true };
        await fixture.Run(); await fixture.Run();
        Assert.Equal(0, fixture.Completions); Assert.NotNull(fixture.Problem);
        Assert.Equal(ManagedTrackingStatus.ReleasePending, fixture.Work.Holding.Status);
        var operation = fixture.Work.Request.OperationId;
        fixture.Offline = false; await fixture.Run(); await fixture.Run();
        Assert.Equal(1, fixture.Completions); Assert.Equal(operation, fixture.Work.Request.OperationId);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RemoteActivityOrMonitoringCannotReleaseTheOwner(bool activity) {
        var fixture = new Fixture();
        fixture.Observation = activity ? fixture.Observation with { CommandsIdle = false }
            : fixture.Observation with { State = fixture.Observation.State with { Targets = [new(fixture.Observation.State.Targets[0].Target, true)] } };
        await fixture.Run(); Assert.Equal(0, fixture.Completions); Assert.NotNull(fixture.Problem);
    }
    [Fact]
    public async Task DisabledConnectionDoesNotInvokeThePluginOrReleaseOwnership() {
        var fixture = new Fixture();
        var state = fixture.Connection.State;
        fixture.Connection.Configure(state.Name, state.BaseUrl, false, state.EnabledCapabilities, state.Settings);
        await fixture.Run(); Assert.Equal(0, fixture.Invocations); Assert.Equal(0, fixture.Completions);
    }
    [Fact]
    public async Task ChangedIdentityOrFolderRemainsPendingForReview() {
        foreach (var identity in new[] { false, true }) {
            var fixture = new Fixture(); var state = fixture.Observation.State;
            fixture.Observation = fixture.Observation with { State = identity
                ? state with { Item = state.Item with { ExternalIds = new Dictionary<string, string> { [ExternalIdProviders.Tmdb] = "another" } } }
                : state with { Path = "/another/folder" } };
            await fixture.Run(); Assert.Equal(0, fixture.Completions); Assert.NotNull(fixture.Problem);
        }
    }

    private sealed class Fixture : IManagedReleaseStore, IIntegrationConnectionStore, IIntegrationPluginGateway, IIntegrationManagerReleaseGateway {
        private const string PluginId = "release-fixture", Runtime = "fixture-runtime";
        internal ManagedReleaseWork Work;
        internal ManagedReleaseObservation Observation;
        internal IntegrationConnection Connection;
        private readonly PluginManifest manifest;
        internal bool Offline;
        internal int Completions, Invocations;
        internal string? Problem;
        internal Fixture() {
            IntegrationSupport[] support = [new(PluginCapability.ExternalManager, [IntegrationOperation.InspectManagedRelease], [EntityKind.Movie])];
            Connection = IntegrationConnection.Create(PluginId, "Manager", "https://manager.test", true, [PluginCapability.ExternalManager], new Dictionary<string, string>());
            Connection.RecordProbe(null, support, null, DateTimeOffset.UtcNow, false);
            manifest = new(2, [], PluginId, "Manager", "1.0.0", Runtime, "plugin.dll", new("2.0.0", null, "3.8.0", null), [], false, [],
                Integration: new(1, support.Select(item => new PluginIntegrationCapability(item.Kind, item.Operations, item.EntityKinds)).ToArray(), []));
            var item = new ManagedItemInput(EntityKind.Movie, "1", new Dictionary<string, string> { [ExternalIdProviders.Tmdb] = "42" });
            var holding = new ManagedTrackingResponse(Guid.NewGuid(), Connection.State.Id, Guid.NewGuid(), item, "Film",
                ManagedTrackingStatus.ReleasePending, 3, null, null, [], [new(new("1", EntityKind.Movie, null, null, null), Guid.NewGuid())]);
            var owned = ManagedControlIdentity.From(holding);
            Work = new(holding, new(Guid.NewGuid(), 2, owned.Fingerprint, "/movies/film"));
            Observation = new(new(new("1", EntityKind.Movie, "Film", 2024, item.ExpectedExternalIds, false, "1", 0), "/movies/film",
                [new(owned.Scope.Targets[0], false)], new(true, true, true)), true, true);
        }
        internal Task<bool> Run() => new ManagedReleaseService(this, null!, null!, new(this, this), this).ProcessAsync(Work.Holding.Id, default);
        public Task<ManagedReleaseWork?> FindAsync(Guid id, CancellationToken token) => Task.FromResult<ManagedReleaseWork?>(Work);
        public Task CompleteAsync(ManagedReleaseWork work, ManagedReleaseObservation evidence, CancellationToken token) {
            Assert.Equal(ManagedTrackingStatus.ReleasePending, Work.Holding.Status);
            Completions++; Work = Work with { Holding = Work.Holding with { Status = ManagedTrackingStatus.Released } }; return Task.CompletedTask;
        }
        public Task RecordProblemAsync(ManagedReleaseWork work, string problem, CancellationToken token) { Problem = problem; return Task.CompletedTask; }
        public Task<ManagedReleaseObservation> InspectReleaseAsync(string pluginId, IntegrationConnectionContext connection, InspectManagedReleaseInput input, CancellationToken token) {
            Invocations++; Assert.Equal(ManagedTrackingStatus.ReleasePending, Work.Holding.Status);
            if (Offline) throw new IntegrationInvocationException("Offline");
            return Task.FromResult(Observation);
        }
        Task<StoredIntegrationConnection?> IIntegrationConnectionStore.FindAsync(Guid id, CancellationToken token) => Task.FromResult<StoredIntegrationConnection?>(new(Connection, []));
        public Task<PluginManifest?> FindAsync(string id, CancellationToken token) => Task.FromResult<PluginManifest?>(manifest);
        public Task<IReadOnlyDictionary<string, string>> ReadSecretsAsync(Guid id, CancellationToken token) => Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());
        public Task RequireSettledControlsAsync(Guid holdingId, CancellationToken token) => throw new NotImplementedException();
        public Task<ManagedTrackingResponse> BeginAsync(Guid connectionId, Guid holdingId, ReleaseManagedHoldingRequest request, CancellationToken token) => throw new NotImplementedException();
        public Task<ConnectionProbeResult> ProbeAsync(string pluginId, IntegrationConnectionContext connection, CancellationToken token) => throw new NotImplementedException();
        public Task<IReadOnlyList<StoredIntegrationConnection>> ListAsync(CancellationToken token) => throw new NotImplementedException();
        public Task SaveAsync(IntegrationConnection connection, long? revision, IReadOnlyDictionary<string, string?> secrets, CancellationToken token) => throw new NotImplementedException();
        public Task DeleteAsync(Guid id, long revision, CancellationToken token) => throw new NotImplementedException();
    }
}
