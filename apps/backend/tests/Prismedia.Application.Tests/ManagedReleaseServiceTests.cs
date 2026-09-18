using Prismedia.Application.Integrations;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Integrations;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;

namespace Prismedia.Application.Tests;

public sealed class ManagedReleaseServiceTests {
    [Fact]
    public async Task RemovedPreviewAcceptsOnlyMutuallyExclusiveAbsentEvidence() {
        var removed = new Fixture();
        removed.Work = removed.Work with { Holding = removed.Work.Holding with { Status = ManagedTrackingStatus.Removed } };
        removed.Observation = new(null, QueueEmpty: true, CommandsIdle: true, RemoteItemAbsent: true);

        var preview = await removed.Preview();

        Assert.True(preview.CanRelease);
        Assert.True(preview.Observation.RemoteItemAbsent);
        Assert.Null(preview.Observation.State);

        var presentAgain = new Fixture();
        presentAgain.Work = presentAgain.Work with { Holding = presentAgain.Work.Holding with { Status = ManagedTrackingStatus.Removed } };
        await Assert.ThrowsAsync<ManagedControlConflictException>(presentAgain.Preview);

        var unconfirmed = new Fixture {
            Observation = new(null, QueueEmpty: true, CommandsIdle: true, RemoteItemAbsent: true)
        };
        await Assert.ThrowsAsync<ManagedControlConflictException>(unconfirmed.Preview);
    }

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
            : fixture.Observation with { State = fixture.Observation.State! with { Targets = [new(fixture.Observation.State!.Targets[0].Target, true)] } };
        await fixture.Run(); Assert.Equal(0, fixture.Completions); Assert.NotNull(fixture.Problem);
    }
    [Fact]
    public async Task ReviewedConfirmedAbsenceReleasesOnlyAfterFreshIdleEvidence() {
        var fixture = new Fixture();
        fixture.ReviewConfirmedAbsence();

        await fixture.Run();

        Assert.Equal(1, fixture.Completions);
        Assert.Null(fixture.Problem);
    }
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task ConfirmedAbsenceStillRequiresEmptyQueueAndIdleCommands(bool queueEmpty, bool commandsIdle) {
        var fixture = new Fixture();
        fixture.ReviewConfirmedAbsence();
        fixture.Observation = fixture.Observation with { QueueEmpty = queueEmpty, CommandsIdle = commandsIdle };

        await fixture.Run();

        Assert.Equal(0, fixture.Completions);
        Assert.NotNull(fixture.Problem);
    }
    [Fact]
    public async Task UnreviewedAbsenceAndReappearanceAfterAbsentReviewRetainOwnership() {
        var unreviewed = new Fixture {
            Observation = new(null, QueueEmpty: true, CommandsIdle: true, RemoteItemAbsent: true)
        };
        await unreviewed.Run();
        Assert.Equal(0, unreviewed.Completions);
        Assert.NotNull(unreviewed.Problem);

        var reappeared = new Fixture();
        var present = reappeared.Observation;
        reappeared.ReviewConfirmedAbsence();
        reappeared.Observation = present;
        await reappeared.Run();
        Assert.Equal(0, reappeared.Completions);
        Assert.NotNull(reappeared.Problem);
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
            var fixture = new Fixture(); var state = fixture.Observation.State!;
            fixture.Observation = fixture.Observation with { State = identity
                ? state with { Item = state.Item with { ExternalIds = new Dictionary<string, string> { [ExternalIdProviders.Tmdb] = "another" } } }
                : state with { Path = "/another/folder" } };
            await fixture.Run(); Assert.Equal(0, fixture.Completions); Assert.NotNull(fixture.Problem);
        }
    }

    private sealed class Fixture : IManagedReleaseStore, IManagedTrackingStore, IManagedControlStore,
        IIntegrationConnectionStore, IIntegrationPluginGateway, IIntegrationManagerReleaseGateway {
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
        internal void ReviewConfirmedAbsence() {
            Work = Work with { Request = Work.Request with { ExpectedPath = null, RemoteItemAbsent = true } };
            Observation = new(null, QueueEmpty: true, CommandsIdle: true, RemoteItemAbsent: true);
        }
        internal Task<bool> Run() => new ManagedReleaseService(this, null!, null!, new(this, this), this).ProcessAsync(Work.Holding.Id, default);
        internal Task<ManagedReleasePreview> Preview() =>
            new ManagedReleaseService(this, this, this, new(this, this), this)
                .PreviewAsync(Work.Holding.ConnectionId, Work.Holding.Id, default);
        public Task<ManagedReleaseWork?> FindAsync(Guid id, CancellationToken token) => Task.FromResult<ManagedReleaseWork?>(Work);
        public Task CompleteAsync(ManagedReleaseWork work, ManagedReleaseObservation evidence, CancellationToken token) {
            Assert.Equal(ManagedTrackingStatus.ReleasePending, Work.Holding.Status);
            Completions++; Work = Work with { Holding = Work.Holding with { Status = ManagedTrackingStatus.Released } }; return Task.CompletedTask;
        }
        public Task RecordProblemAsync(ManagedReleaseWork work, string problem, CancellationToken token) { Problem = problem; return Task.CompletedTask; }
        public Task<ManagedReleaseObservation> InspectReleaseAsync(string pluginId, IntegrationConnectionContext connection, InspectManagedReleaseInput input, CancellationToken token) {
            Invocations++;
            Assert.True(Work.Holding.Status is ManagedTrackingStatus.ReleasePending or ManagedTrackingStatus.Removed);
            if (Offline) throw new IntegrationInvocationException("Offline");
            return Task.FromResult(Observation);
        }
        Task<StoredIntegrationConnection?> IIntegrationConnectionStore.FindAsync(Guid id, CancellationToken token) => Task.FromResult<StoredIntegrationConnection?>(new(Connection, []));
        public Task<PluginManifest?> FindAsync(string id, CancellationToken token) => Task.FromResult<PluginManifest?>(manifest);
        public Task<IReadOnlyDictionary<string, string>> ReadSecretsAsync(Guid id, IReadOnlyCollection<string> credentialKeys, CancellationToken token) => Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());
        public Task RequireSettledControlsAsync(Guid holdingId, CancellationToken token) => Task.CompletedTask;
        public Task<ManagedTrackingResponse> BeginAsync(Guid connectionId, Guid holdingId, ReleaseManagedHoldingRequest request, CancellationToken token) => throw new NotImplementedException();
        public Task<ConnectionProbeResult> ProbeAsync(string pluginId, IntegrationConnectionContext connection, CancellationToken token) => throw new NotImplementedException();
        public Task<IReadOnlyList<StoredIntegrationConnection>> ListAsync(CancellationToken token) => throw new NotImplementedException();
        public Task SaveAsync(IntegrationConnection connection, long? revision, IReadOnlyDictionary<string, string?> secrets, CancellationToken token) => throw new NotImplementedException();
        public Task DeleteAsync(Guid id, long revision, CancellationToken token) => throw new NotImplementedException();
        Task<ManagedTrackingWork?> IManagedTrackingStore.FindAsync(Guid id, CancellationToken token) =>
            Task.FromResult<ManagedTrackingWork?>(new(Work.Holding, []));
        Task<IReadOnlyList<ManagedTrackingResponse>> IManagedTrackingStore.ListAsync(Guid connectionId, CancellationToken token) => throw new NotImplementedException();
        Task<ManagedTrackingObservation> IManagedTrackingStore.ObserveAsync(Guid connectionId, ManagedItemSnapshot snapshot, CancellationToken token) => throw new NotImplementedException();
        Task<ManagedTrackingResponse> IManagedTrackingStore.CreateAsync(Guid connectionId, TrackManagedHoldingRequest request, string title, CancellationToken token) => throw new NotImplementedException();
        Task IManagedTrackingStore.ApplyAsync(ManagedTrackingWork work, ManagedTrackingObservation observation,
            IReadOnlyList<ManagedFileBinding>? adoption, IReadOnlyList<ManagedSourceChange> changes, CancellationToken token) => throw new NotImplementedException();
        Task IManagedTrackingStore.ConfirmRemovalAsync(ManagedTrackingWork work, string problem, CancellationToken token) => throw new NotImplementedException();
        Task IManagedTrackingStore.RecordProblemAsync(Guid id, long revision, ManagedTrackingStatus status, string problem, CancellationToken token) => throw new NotImplementedException();
        Task IManagedTrackingStore.QueueAsync(Guid connectionId, Guid id, CancellationToken token) => throw new NotImplementedException();
        Task IManagedTrackingStore.QueueDueAsync(CancellationToken token) => throw new NotImplementedException();
        Task<OwnedManagedControlScope> IManagedControlStore.RequireScopeAsync(Guid connectionId, Guid holdingId, CancellationToken token) {
            var owned = ManagedControlIdentity.From(Work.Holding);
            return Task.FromResult(new OwnedManagedControlScope(owned.Scope, owned.Fingerprint));
        }
        Task<StoredManagedControl?> IManagedControlStore.FindAsync(Guid id, CancellationToken token) => throw new NotImplementedException();
        Task<IReadOnlyList<StoredManagedControl>> IManagedControlStore.ListAsync(Guid connectionId, Guid holdingId, CancellationToken token) => throw new NotImplementedException();
        Task<StoredManagedControl> IManagedControlStore.CreateAsync(ManagedControlOperation operation, ManagedControlPlan plan, CancellationToken token) => throw new NotImplementedException();
        Task IManagedControlStore.SaveAsync(ManagedControlOperation operation, long expectedRevision, string? problem,
            bool beforeDispatch, CancellationToken token) => throw new NotImplementedException();
        Task IManagedControlStore.QueueAsync(Guid id, CancellationToken token) => throw new NotImplementedException();
        Task IManagedControlStore.QueueDueAsync(CancellationToken token) => throw new NotImplementedException();
    }
}
