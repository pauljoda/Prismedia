using Prismedia.Application.Integrations;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Integrations;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;

namespace Prismedia.Application.Tests;

public sealed class ManagedControlProcessorTests {
    [Fact] public async Task Configuration_response_loss_recovers_by_reading_and_never_repeats_write() {
        var fixture = new Fixture(configure: true, search: true) { LoseConfigureResponse = true };
        await fixture.Run();
        Assert.Equal(ManagedControlPhase.ConfigurationUncertain, fixture.Saved.Operation.State.Phase);
        Assert.True(fixture.Saved.Operation.State.ReviewRequired);
        await fixture.Run();
        Assert.True(fixture.Saved.Operation.State.ConfigurationConfirmed);
        Assert.Equal(ManagedControlPhase.PendingSearch, fixture.Saved.Operation.State.Phase);
        await fixture.Run(); await fixture.Run();
        Assert.Equal(1, fixture.ConfigureCalls); Assert.Equal(1, fixture.SearchCalls);
        Assert.Equal(ManagedControlPhase.Completed, fixture.Saved.Operation.State.Phase);
        Assert.Equal(0, fixture.Observed.Item.RemoteFileCount);
    }
    [Fact] public async Task Search_response_loss_survives_repeated_recovery_without_another_post() {
        var fixture = new Fixture(false, true) { LoseSearchResponse = true };
        await fixture.Run(); await fixture.Run(); await fixture.Run();
        Assert.Equal(1, fixture.SearchCalls);
        Assert.Equal(ManagedControlPhase.SearchUncertain, fixture.Saved.Operation.State.Phase);
        Assert.Null(fixture.Saved.Operation.State.Command);
        Assert.True(fixture.Saved.Operation.CanCloseUnverified);
    }
    [Fact] public async Task Crash_after_dispatch_fence_does_not_assume_the_write_was_never_sent() {
        var fixture = new Fixture(false, true);
        fixture.Saved.Operation.BeginSearch();
        await fixture.Run();
        Assert.Equal(0, fixture.SearchCalls); Assert.True(fixture.Saved.Operation.State.ReviewRequired);
    }
    [Fact] public async Task Unknown_command_history_does_not_become_success_or_a_new_search() {
        var fixture = new Fixture(false, true);
        await fixture.Run();
        fixture.Observed = fixture.Observed with { Command = fixture.Observed.Command! with { Status = ManagedCommandStatus.Unknown } };
        await fixture.Run();
        Assert.True(fixture.Saved.Operation.IsActive); Assert.True(fixture.Saved.Operation.State.ReviewRequired);
        fixture.Observed = fixture.Observed with { Command = fixture.Observed.Command! with { Status = ManagedCommandStatus.Completed } };
        await fixture.Run();
        Assert.Equal(1, fixture.SearchCalls); Assert.Equal(ManagedControlPhase.Completed, fixture.Saved.Operation.State.Phase);
    }
    [Fact] public async Task Reused_command_id_with_another_timestamp_stays_unverified() {
        var fixture = new Fixture(false, true); await fixture.Run();
        fixture.Observed = fixture.Observed with { Command = fixture.Observed.Command! with {
            Reference = fixture.Observed.Command.Reference with { QueuedAt = fixture.Observed.Command.Reference.QueuedAt.AddSeconds(1) } } };
        await fixture.Run();
        Assert.Equal(ManagedCommandStatus.Unknown, fixture.Saved.Operation.State.CommandStatus);
        Assert.Equal(1, fixture.SearchCalls);
    }
    [Fact] public async Task Cancel_winning_before_dispatch_prevents_remote_effect() {
        var fixture = new Fixture(false, true) { CancelAtFence = true };
        await fixture.Run();
        Assert.Equal(ManagedControlPhase.Cancelled, fixture.Saved.Operation.State.Phase);
        Assert.Equal(0, fixture.SearchCalls);
    }
    [Fact] public async Task Changed_profile_refuses_search_and_preserves_confirmed_settings() {
        var fixture = new Fixture(true, true); await fixture.Run();
        fixture.Observed = fixture.Observed with { Item = fixture.Observed.Item with { ProfileId = "another" } };
        await fixture.Run();
        Assert.True(fixture.Saved.Operation.State.ConfigurationConfirmed);
        Assert.Equal(ManagedControlPhase.Rejected, fixture.Saved.Operation.State.Phase); Assert.Equal(0, fixture.SearchCalls);
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task Revoked_capability_or_changed_target_prevents_writes(bool revoke) {
        var fixture = new Fixture(true, true);
        if (revoke) fixture.Manifest = fixture.Manifest with { Integration = new(1, [], []) };
        else fixture.Observed = fixture.Observed with { Targets = [] };
        await fixture.Run();
        Assert.True(fixture.Saved.Operation.State.ReviewRequired);
        Assert.Equal(0, fixture.SearchCalls); Assert.Equal(0, fixture.ConfigureCalls);
    }

    private sealed class Fixture : IManagedControlStore, IIntegrationConnectionStore, IIntegrationPluginGateway, IIntegrationManagerControlGateway {
        private const string PluginId = "fixture-manager";
        internal StoredManagedControl Saved;
        internal ManagedControlState Observed;
        internal PluginManifest Manifest;
        private readonly IntegrationConnection connection;
        internal bool LoseConfigureResponse, LoseSearchResponse, CancelAtFence;
        internal int ConfigureCalls, SearchCalls;
        internal Fixture(bool configure, bool search) {
            IntegrationSupport[] support = [new(PluginCapability.ExternalManager,
                [IntegrationOperation.ReconcileManaged, IntegrationOperation.ConfigureManaged, IntegrationOperation.RequestManaged], [EntityKind.Movie])];
            connection = IntegrationConnection.Create(PluginId, "Manager", "https://manager.test", true, [PluginCapability.ExternalManager], new Dictionary<string, string>());
            connection.RecordProbe(null, support, null, DateTimeOffset.UtcNow, false);
            Manifest = new(2, [], PluginId, "Manager", "1.0.0", "dotnet-process", "plugin.dll", new("2.0.0", null, "3.8.0", null), [], false, [],
                Integration: new(1, support.Select(value => new PluginIntegrationCapability(value.Kind, value.Operations, value.EntityKinds)).ToArray(), []));
            var action = ManagedControlOperation.Create(Guid.NewGuid(), connection.State.Id, Guid.NewGuid(), configure, search);
            var scope = new ManagedControlScope(new(EntityKind.Movie, "1", new Dictionary<string, string> { [ExternalIdProviders.Tmdb] = "42" }), [new("1", EntityKind.Movie)]);
            var request = new CreateManagedControlRequest(action.State.OperationId, new string('a', 64), "/movies/film", "1",
                new Dictionary<string, bool> { ["1"] = false }, new(Monitored: configure ? true : null), search);
            Saved = new(action, new(scope, request, ManagedControlIdentity.RequestFingerprint(request)), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null);
            Observed = new(new("1", EntityKind.Movie, "Film", 2024, scope.Item.ExpectedExternalIds, false, "1", 0), "/movies/film",
                [new(scope.Targets[0], false)], new(true, true, true));
        }
        internal Task Run() => new ManagedControlProcessor(this, new(this, this), this).ProcessAsync(Saved.Operation.State.OperationId, default);
        public Task<StoredManagedControl?> FindAsync(Guid id, CancellationToken token) => Task.FromResult<StoredManagedControl?>(Saved with { Operation = new(Saved.Operation.State) });
        public Task<OwnedManagedControlScope> RequireScopeAsync(Guid connectionId, Guid holdingId, CancellationToken token) => Task.FromResult(new OwnedManagedControlScope(Saved.Plan.Scope, Saved.Plan.Request.ScopeFingerprint));
        public Task<IReadOnlyList<StoredManagedControl>> ListAsync(Guid connectionId, Guid holdingId, CancellationToken token) => throw new NotImplementedException();
        public Task<StoredManagedControl> CreateAsync(ManagedControlOperation operation, ManagedControlPlan plan, CancellationToken token) => throw new NotImplementedException();
        public Task SaveAsync(ManagedControlOperation operation, long expectedRevision, string? problem, bool beforeDispatch, CancellationToken token) {
            if (CancelAtFence && beforeDispatch) Saved.Operation.Cancel();
            if (Saved.Operation.State.Revision != expectedRevision) throw new ManagedControlConflictException("stale");
            Saved = Saved with { Operation = new(operation.State), Problem = problem };
            return Task.CompletedTask;
        }
        public Task QueueAsync(Guid id, CancellationToken token) => throw new NotImplementedException();
        public Task QueueDueAsync(CancellationToken token) => throw new NotImplementedException();
        public Task<ManagedControlState> ReconcileAsync(string pluginId, IntegrationConnectionContext context, ReconcileManagedInput input, CancellationToken token) => Task.FromResult(Observed);
        public Task<ManagedMutationResult> ConfigureAsync(string pluginId, IntegrationConnectionContext context, ConfigureManagedInput input, CancellationToken token) {
            Assert.Equal(ManagedControlPhase.ConfigurationUncertain, Saved.Operation.State.Phase); ConfigureCalls++;
            Observed = Observed with { Targets = [new(Observed.Targets[0].Target, true)] };
            if (LoseConfigureResponse) throw new IntegrationInvocationException("response lost");
            return Task.FromResult(new ManagedMutationResult(ManagedMutationOutcome.Applied));
        }
        public Task<ManagedMutationResult> RequestAsync(string pluginId, IntegrationConnectionContext context, RequestManagedInput input, CancellationToken token) {
            Assert.Equal(ManagedControlPhase.SearchUncertain, Saved.Operation.State.Phase); SearchCalls++;
            if (LoseSearchResponse) throw new IntegrationInvocationException("response lost");
            var command = new ManagedCommandSnapshot(new("12", DateTimeOffset.Parse("2026-09-16T20:00:00.1234567Z")), ManagedCommandStatus.Pending);
            Observed = Observed with { Command = command with { Status = ManagedCommandStatus.Completed } };
            return Task.FromResult(new ManagedMutationResult(ManagedMutationOutcome.Accepted, command));
        }
        Task<StoredIntegrationConnection?> IIntegrationConnectionStore.FindAsync(Guid id, CancellationToken token) => Task.FromResult<StoredIntegrationConnection?>(new(connection, []));
        public Task<IReadOnlyDictionary<string, string>> ReadSecretsAsync(Guid id, CancellationToken token) => Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());
        public Task<PluginManifest?> FindAsync(string id, CancellationToken token) => Task.FromResult<PluginManifest?>(Manifest);
        public Task<ConnectionProbeResult> ProbeAsync(string pluginId, IntegrationConnectionContext context, CancellationToken token) => throw new NotImplementedException();
        public Task<IReadOnlyList<StoredIntegrationConnection>> ListAsync(CancellationToken token) => throw new NotImplementedException();
        public Task SaveAsync(IntegrationConnection value, long? revision, IReadOnlyDictionary<string, string?> secrets, CancellationToken token) => throw new NotImplementedException();
        public Task DeleteAsync(Guid id, long revision, CancellationToken token) => throw new NotImplementedException();
    }
}
