using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Prismedia.Application.Integrations;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Integrations;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;

namespace Prismedia.Application.Tests;

public sealed class ManagedRequestProcessorTests {
    [Fact]
    public void FiniteSeriesRequestMustSearchExactEpisodesWithoutBroadMonitoring() {
        var targetId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var work = SeriesWork();
        var request = new CreateManagedRequestInput(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            work,
            "1",
            Monitored: false,
            Search: true,
            [targetId]);

        ManagedRequestService.Validate(request);
        Assert.Null(ManagedRequestProcessor.InitialConfiguration(request).Monitored);
        Assert.Throws<ArgumentException>(() => ManagedRequestService.Validate(request with { Monitored = true }));
        Assert.Throws<ArgumentException>(() => ManagedRequestService.Validate(request with { Search = false }));
    }

    [Fact]
    public void ComicRequestRequiresOneLabeledIssueAndNoProfile() {
        var work = new ManagedLookupInput(EntityKind.ComicSeries,
            new Dictionary<string, string> { [ExternalIdProviders.ComicVine] = "4050-1" },
            [new(EntityKind.ComicInstallment,
                new Dictionary<string, string> { [ExternalIdProviders.ComicVine] = "4000-2" }, IssueLabel: "½")]);
        var request = new CreateManagedRequestInput(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), work,
            null, Monitored: true, Search: true, [Guid.NewGuid()]);
        ManagedRequestService.Validate(request);
        Assert.True(ManagedRequestProcessor.InitialConfiguration(request).Monitored);
        Assert.Throws<ArgumentException>(() => ManagedRequestService.Validate(request with { ProfileId = "profile" }));
        Assert.Throws<ArgumentException>(() => ManagedRequestService.Validate(request with { Search = false }));
        Assert.Throws<ArgumentException>(() => ManagedRequestService.Validate(request with { TargetEntityIds = [] }));
        Assert.Throws<ArgumentException>(() => ManagedRequestService.Validate(request with {
            ReviewedWork = work with { Targets = [work.Targets![0] with { IssueLabel = null }] }
        }));
        var plan = new ManagedRequestPlan(request,
            new(request.OperationId, work, null, "1", "/comics"), "Fixture Run", "fingerprint");
        Assert.Equal("Fixture Run · #½", plan.DisplayTitle());
    }

    [Fact]
    public void MovieFingerprintRemainsCompatibleWithArchivedContractBeforeFiniteTargets() {
        const string archivedJson = """{"OperationId":"11111111-1111-1111-1111-111111111111","EntityId":"22222222-2222-2222-2222-222222222222","LibraryRootId":"33333333-3333-3333-3333-333333333333","ReviewedWork":{"EntityKind":15,"ExternalIds":{"tmdb":"42"}},"ProfileId":"1","Monitored":true,"Search":true}""";
        const string archivedFingerprint = "11c79bfde3e2129600000e151f40128ad0dc6369aa7c3cbd0b9b45edf9ec63a8";
        var request = new CreateManagedRequestInput(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            new(EntityKind.Movie, new Dictionary<string, string> { [ExternalIdProviders.Tmdb] = "42" }),
            "1",
            true,
            true);

        Assert.Equal(archivedJson, JsonSerializer.Serialize(request));
        Assert.Equal(archivedFingerprint,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(archivedJson))));
        Assert.Equal(archivedFingerprint, ManagedRequestIdentity.Fingerprint(request));
        Assert.Equal(archivedFingerprint, ManagedRequestIdentity.Fingerprint(request with {
            ReviewedWork = request.ReviewedWork with { Targets = [] },
            TargetEntityIds = []
        }));
    }

    [Fact]
    public void MissingSeriesLookupMayDeferEpisodeIdsButExistingHoldingMustResolveEveryTarget() {
        var work = SeriesWork();
        var candidate = new ManagedCandidate(
            EntityKind.VideoSeries,
            "Series",
            2026,
            work.ExternalIds);

        ManagedCreationEvidence.ValidateLookup(work, new(candidate, Existing: null, Targets: null));

        var holding = new ManagedItemSnapshot(
            new("series", EntityKind.VideoSeries, "Series", 2026, work.ExternalIds, false, "profile", 0),
            "/series/Series",
            [],
            DateTimeOffset.UtcNow);
        Assert.Throws<IntegrationInvocationException>(() =>
            ManagedCreationEvidence.ValidateLookup(work, new(candidate, holding, Targets: null)));
    }

    [Fact]
    public void FiniteTargetEvidenceRequiresUniqueExactEpisodeCoverageAndAllowsOmittedAbsoluteNumber() {
        var work = SeriesWork();
        var resolved = new[] {
            new ManagedResolvedTarget(
                "episode-1",
                EntityKind.VideoEpisode,
                new Dictionary<string, string> { [ExternalIdProviders.Tvdb] = "101" },
                0,
                1,
                AbsoluteNumber: null)
        };

        ManagedCreationEvidence.ValidateTargets(work, resolved);
        Assert.Throws<IntegrationInvocationException>(() =>
            ManagedCreationEvidence.ValidateTargets(work, [resolved[0], resolved[0] with { RemoteId = "duplicate" }]));
        Assert.Throws<IntegrationInvocationException>(() =>
            ManagedCreationEvidence.ValidateTargets(work, [resolved[0] with { EpisodeNumber = 2 }]));
    }

    [Fact]
    public void ComicTargetEvidencePinsFractionalIssueLabelsWithoutChangingMovieFingerprints() {
        var work = new ManagedLookupInput(EntityKind.ComicSeries,
            new Dictionary<string, string> { [ExternalIdProviders.ComicVine] = "4050-42" },
            [new(EntityKind.ComicInstallment,
                new Dictionary<string, string> { [ExternalIdProviders.ComicVine] = "4000-7" }, IssueLabel: "½")]);
        var resolved = new ManagedResolvedTarget("17", EntityKind.ComicInstallment,
            new Dictionary<string, string> { [ExternalIdProviders.ComicVine] = "4000-7" }, IssueLabel: "½");

        ManagedCreationEvidence.ValidateTargets(work, [resolved]);
        var snapshot = new ManagedItemSnapshot(
            new("run", EntityKind.ComicSeries, "Run", 2024, work.ExternalIds, false, null, 0),
            "/comics/run", [], DateTimeOffset.UtcNow,
            [new("17", "½", "Half issue", false, resolved.ExternalIds)]);
        ManagedCreationEvidence.ValidateComicTargets(work, snapshot, [resolved]);
        Assert.Throws<IntegrationInvocationException>(() => ManagedCreationEvidence.ValidateComicTargets(work,
            snapshot with { ComicIssues = [snapshot.ComicIssues![0] with { IssueLabel = "0.5" }] }, [resolved]));
        Assert.Throws<IntegrationInvocationException>(() => ManagedCreationEvidence.ValidateComicTargets(work,
            snapshot with { ComicIssues = null }, [resolved]));
        Assert.False(ManagedRequestIdentity.SameWork(work, work with { Targets = [work.Targets![0] with { IssueLabel = "0.5" }] }));
        Assert.Throws<IntegrationInvocationException>(() => ManagedCreationEvidence.ValidateTargets(work,
            [resolved with { IssueLabel = "0.5" }]));
        Assert.Throws<IntegrationInvocationException>(() => ManagedCreationEvidence.ValidateTargets(work,
            [resolved with { IssueLabel = null }]));
        Assert.Throws<IntegrationInvocationException>(() => ManagedCreationEvidence.ValidateTargets(
            work with { Targets = [work.Targets![0] with { IssueLabel = null }] }, [resolved]));
    }

    private static ManagedLookupInput SeriesWork() => new(
        EntityKind.VideoSeries,
        new Dictionary<string, string> { [ExternalIdProviders.Tvdb] = "42" },
        [new(
            EntityKind.VideoEpisode,
            new Dictionary<string, string> { [ExternalIdProviders.Tvdb] = "101" },
            SeasonNumber: 0,
            EpisodeNumber: 1,
            AbsoluteNumber: 100)]);

    [Fact]
    public async Task LostCreationResponseRecoversExactHoldingWithoutAnotherPost() {
        var fixture = new Fixture { LoseResponse = true };
        await fixture.Run();
        Assert.Equal(ManagedRequestPhase.CreationUncertain, fixture.Saved.Operation.State.Phase);
        Assert.True(fixture.Saved.Operation.State.ReviewRequired);
        await fixture.Run();
        Assert.Equal(ManagedRequestPhase.AwaitingFiles, fixture.Saved.Operation.State.Phase);
        Assert.Equal("1", fixture.Saved.Operation.State.RemoteId);
        Assert.Equal(1, fixture.Writes);
    }
    [Fact]
    public async Task AbsentHoldingAfterAmbiguousCreationNeverTriggersAnotherPost() {
        var fixture = new Fixture { LoseResponse = true };
        await fixture.Run(); fixture.Exists = false;
        await fixture.Run(); await fixture.Run();
        Assert.Equal(1, fixture.Writes); Assert.True(fixture.Saved.Operation.State.ReviewRequired);
        Assert.Equal(ManagedRequestPhase.CreationUncertain, fixture.Saved.Operation.State.Phase);
    }
    [Fact]
    public async Task CrashAfterFenceBeforeActualCallStillRequiresObservation() {
        var fixture = new Fixture(); fixture.Saved.Operation.BeginCreation();
        await fixture.Run();
        Assert.Equal(0, fixture.Writes); Assert.True(fixture.Saved.Operation.State.ReviewRequired);
        Assert.False(fixture.Saved.Operation.CanCancel);
    }
    [Fact]
    public async Task ExistingHoldingDoesNotRequireCreationMutation() {
        var fixture = new Fixture { Exists = true };
        await fixture.Run();
        Assert.Equal(0, fixture.Writes);
        Assert.Equal(ManagedRequestPhase.AwaitingFiles, fixture.Saved.Operation.State.Phase);
    }
    [Fact]
    public async Task MissingExpansionHoldingNeverCreatesAReplacement() {
        var fixture = new Fixture();
        fixture.Saved = fixture.Saved with {
            Plan = fixture.Saved.Plan with { ExistingHoldingId = Guid.NewGuid() }
        };

        await fixture.Run();

        Assert.Equal(0, fixture.Writes);
        Assert.Equal(ManagedRequestPhase.PendingCreation, fixture.Saved.Operation.State.Phase);
        Assert.True(fixture.Saved.Operation.State.ReviewRequired);
    }
    [Fact]
    public async Task MissingComicRunNeverTriggersCreationMutation() {
        var fixture = new Fixture();
        var work = new ManagedLookupInput(EntityKind.ComicSeries,
            new Dictionary<string, string> { [ExternalIdProviders.ComicVine] = "4050-1" },
            [new(EntityKind.ComicInstallment,
                new Dictionary<string, string> { [ExternalIdProviders.ComicVine] = "4000-2" }, IssueLabel: "½")]);
        var request = fixture.Saved.Plan.Request with { ReviewedWork = work, ProfileId = null,
            TargetEntityIds = [Guid.NewGuid()] };
        fixture.Saved = fixture.Saved with { Plan = fixture.Saved.Plan with {
            Request = request,
            Creation = fixture.Saved.Plan.Creation with { Work = work, ProfileId = null },
            Fingerprint = ManagedRequestIdentity.Fingerprint(request)
        } };
        await fixture.Run();
        Assert.Equal(0, fixture.Writes);
        Assert.True(fixture.Saved.Operation.State.ReviewRequired);
        Assert.Equal(ManagedRequestPhase.PendingCreation, fixture.Saved.Operation.State.Phase);
    }
    [Fact]
    public async Task CancellationWinningTheFencePreventsCreation() {
        var fixture = new Fixture { CancelAtFence = true };
        await Assert.ThrowsAsync<ManagedRequestConflictException>(fixture.Run);
        Assert.Equal(0, fixture.Writes); Assert.Equal(ManagedRequestPhase.Cancelled, fixture.Saved.Operation.State.Phase);
    }
    [Fact]
    public async Task DefiniteRejectionRetainsRequestAndAllowsSafeCancellation() {
        var fixture = new Fixture { Reject = true };
        await fixture.Run(); await fixture.Run();
        Assert.Equal(1, fixture.Writes); Assert.Equal(ManagedRequestPhase.Rejected, fixture.Saved.Operation.State.Phase);
        Assert.True(fixture.Saved.Operation.CanCancel);
    }
    [Fact]
    public async Task MalformedCreationAcknowledgementStaysUncertainInsteadOfClaimingAvailability() {
        var fixture = new Fixture { MalformedResult = true };
        await fixture.Run();
        Assert.Equal(ManagedRequestPhase.CreationUncertain, fixture.Saved.Operation.State.Phase);
        Assert.True(fixture.Saved.Operation.State.ReviewRequired);
        Assert.Equal(1, fixture.Writes);
    }
    [Fact]
    public async Task MismatchedLookupIdentityPreventsAnyMutation() {
        var fixture = new Fixture { WrongIdentity = true };
        await fixture.Run();
        Assert.Equal(0, fixture.Writes); Assert.Equal(ManagedRequestPhase.PendingCreation, fixture.Saved.Operation.State.Phase);
        Assert.True(fixture.Saved.Operation.State.ReviewRequired);
    }
    [Fact]
    public async Task RevokedCreationCapabilityPreventsDispatch() {
        var fixture = new Fixture();
        fixture.Manifest = fixture.Manifest with { Integration = new(1, [new(PluginCapability.ExternalManager, [IntegrationOperation.LookupManaged], [EntityKind.Movie])], []) };
        await fixture.Run();
        Assert.Equal(0, fixture.Writes); Assert.Equal(ManagedRequestPhase.PendingCreation, fixture.Saved.Operation.State.Phase);
    }

    [Fact]
    public async Task ChangedMappedBoundaryStopsBeforeAutomaticControlsAndFileImport() {
        var fixture = new Fixture { Exists = true, InvalidBoundary = true };
        await fixture.Run(); await fixture.Run();
        Assert.Equal(ManagedRequestPhase.AwaitingFiles, fixture.Saved.Operation.State.Phase);
        Assert.True(fixture.Saved.Operation.State.ReviewRequired);
        Assert.Equal("Holding moved outside the mapped library", fixture.Saved.Problem);
        Assert.Equal(0, fixture.Writes);
    }

    [Fact]
    public async Task TypedMissingHoldingConfirmsRemovalWithoutRepeatingCreationOrMatchingDisplayText() {
        var fixture = new Fixture { Exists = true };
        await fixture.Run();
        fixture.HoldingRemoved = true;

        await fixture.Run();

        Assert.True(fixture.ConfirmedRemoval);
        Assert.Equal(ManagedRequestPhase.RemoteRemoved, fixture.Saved.Operation.State.Phase);
        Assert.Equal(0, fixture.Writes);
    }

    [Fact]
    public async Task ReviewedAwaitingFilesObservationDoesNotReplayControlsOrClearReview() {
        var fixture = new Fixture { Exists = true };
        await fixture.Run();
        fixture.Saved.Operation.RequireReview();
        var revision = fixture.Saved.Operation.State.Revision;

        await fixture.Run();

        Assert.Equal(revision, fixture.Saved.Operation.State.Revision);
        Assert.True(fixture.Saved.Operation.State.ReviewRequired);
        Assert.False(fixture.HoldingValidated);
        Assert.Equal(0, fixture.Writes);
    }

    [Fact]
    public async Task TemporarilyUnavailableConnectionKeepsAwaitingFilesRetryable() {
        var fixture = new Fixture { Exists = true };
        await fixture.Run();
        var revision = fixture.Saved.Operation.State.Revision;
        fixture.Manifest = fixture.Manifest with {
            Integration = new(1, [new(PluginCapability.ExternalManager,
                [IntegrationOperation.LookupManaged, IntegrationOperation.EnsureManaged], [EntityKind.Movie])], [])
        };

        await fixture.Run();

        Assert.Equal(ManagedRequestPhase.AwaitingFiles, fixture.Saved.Operation.State.Phase);
        Assert.Equal(revision + 1, fixture.Saved.Operation.State.Revision);
        Assert.False(fixture.Saved.Operation.State.ReviewRequired);
        Assert.NotNull(fixture.Saved.Problem);
        Assert.False(fixture.HoldingValidated);
    }

    [Fact]
    public async Task TemporaryReadOnlySnapshotFailureRetriesAndLaterResumesMaterialization() {
        var fixture = new Fixture { Exists = true };
        await fixture.Run();
        var revision = fixture.Saved.Operation.State.Revision;
        fixture.FailObservationOnce = true;

        await fixture.Run();

        Assert.Equal(ManagedRequestPhase.AwaitingFiles, fixture.Saved.Operation.State.Phase);
        Assert.Equal(revision + 1, fixture.Saved.Operation.State.Revision);
        Assert.False(fixture.Saved.Operation.State.ReviewRequired);
        Assert.False(fixture.HoldingValidated);

        await fixture.Run();

        Assert.True(fixture.HoldingValidated);
        Assert.True(fixture.Materialized);
        Assert.False(fixture.Saved.Operation.State.ReviewRequired);
    }

    private sealed class Fixture : IManagedRequestStore, IManagedTrackingStore, IManagedControlStore,
        IIntegrationManagerCreationGateway, IIntegrationConnectionStore, IIntegrationPluginGateway, IIntegrationManagerGateway {
        private const string PluginId = "fixture-manager";
        internal StoredManagedRequest Saved;
        internal PluginManifest Manifest;
        private readonly IntegrationConnection connection;
        internal bool LoseResponse, Exists, CancelAtFence, Reject, MalformedResult, WrongIdentity, InvalidBoundary, HoldingRemoved,
            FailObservationOnce;
        internal bool ConfirmedRemoval;
        internal bool HoldingValidated;
        internal bool Materialized;
        internal int Writes;
        internal Fixture() {
            IntegrationSupport[] support = [new(PluginCapability.ExternalManager, [IntegrationOperation.LookupManaged, IntegrationOperation.EnsureManaged], [EntityKind.Movie, EntityKind.ComicSeries]),
                new(PluginCapability.ConnectedLibrary, [IntegrationOperation.GetLibraryItem], [EntityKind.Movie])];
            connection = IntegrationConnection.Create(PluginId, "Manager", "https://manager.test", true, [PluginCapability.ExternalManager, PluginCapability.ConnectedLibrary], new Dictionary<string,string>());
            connection.RecordProbe(null, support, null, DateTimeOffset.UtcNow, false);
            Manifest = new(2, [], PluginId, "Manager", "1.0.0", "dotnet-process", "plugin.dll", new("2.0.0", null, "3.8.0", null), [], false, [],
                Integration: new(1, support.Select(value => new PluginIntegrationCapability(value.Kind, value.Operations, value.EntityKinds)).ToArray(), []));
            var operation = ManagedRequestOperation.Create(Guid.NewGuid(), connection.State.Id, Guid.NewGuid(), Guid.NewGuid());
            var work = new ManagedLookupInput(EntityKind.Movie, new Dictionary<string,string> { [ExternalIdProviders.Tmdb] = "42" });
            var request = new CreateManagedRequestInput(operation.State.OperationId, operation.State.EntityId, operation.State.LibraryRootId, work, "1", true, true);
            Saved = new(operation, new(request, new(operation.State.OperationId, work, "1", "1", "/movies"), "Film", ManagedRequestIdentity.Fingerprint(request)), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null);
        }
        internal async Task Run() => await new ManagedRequestProcessor(this, new(this, this), this,
            new(new(this, this), this), this, null!, this).ProcessAsync(Saved.Operation.State.OperationId, default);
        private ManagedItemSnapshot Holding() => new(new("1", Saved.Plan.Creation.Work.EntityKind, "Film", 2024,
            Saved.Plan.Creation.Work.ExternalIds, false, Saved.Plan.Creation.ProfileId, 0), "/movies/film", [], DateTimeOffset.UtcNow);
        public Task<ManagedLookupResult> LookupAsync(string pluginId, IntegrationConnectionContext context, ManagedLookupInput input, CancellationToken token) =>
            Task.FromResult(new ManagedLookupResult(new(input.EntityKind, "Film", 2024,
                WrongIdentity ? new Dictionary<string,string> { [ExternalIdProviders.Tmdb] = "999" } : input.ExternalIds), Exists ? Holding() : null));
        public Task<EnsureManagedResult> EnsureAsync(string pluginId, IntegrationConnectionContext context, EnsureManagedInput input, CancellationToken token) {
            Assert.Equal(ManagedRequestPhase.CreationUncertain, Saved.Operation.State.Phase); Writes++;
            if (Reject) return Task.FromResult(new EnsureManagedResult(ManagedMutationOutcome.Rejected));
            Exists = true;
            if (LoseResponse) throw new IntegrationInvocationException("Creation response lost");
            return Task.FromResult(MalformedResult ? new EnsureManagedResult(ManagedMutationOutcome.Applied) : new(ManagedMutationOutcome.Applied, Holding(), true));
        }
        public Task<StoredManagedRequest?> FindAsync(Guid id, CancellationToken token) => Task.FromResult<StoredManagedRequest?>(Saved with { Operation = new(Saved.Operation.State) });
        public Task SaveAsync(ManagedRequestOperation operation, long expectedRevision, string? problem, bool beforeDispatch, CancellationToken token) {
            if (beforeDispatch && CancelAtFence) Saved.Operation.Cancel();
            if (Saved.Operation.State.Revision != expectedRevision) throw new ManagedRequestConflictException("stale");
            Saved = Saved with { Operation = new(operation.State), Problem = problem }; return Task.CompletedTask;
        }
        public Task AcceptHoldingAsync(StoredManagedRequest work, ManagedItemSnapshot snapshot, CancellationToken token) {
            Assert.Equal(Saved.Operation.State.Revision, work.Operation.State.Revision);
            Saved.Operation.AcceptHolding(snapshot.Item.RemoteId); return Task.CompletedTask;
        }
        public Task<ManagedRequestTarget> RequireTargetAsync(Guid connectionId, Guid entityId, Guid libraryRootId, CancellationToken token) => throw new NotImplementedException();
        public Task<IReadOnlyList<StoredManagedRequest>> ListAsync(Guid connectionId, CancellationToken token) => throw new NotImplementedException();
        public Task<StoredManagedRequest> CreateAsync(ManagedRequestOperation operation, ManagedRequestPlan plan, CancellationToken token) => throw new NotImplementedException();
        public Task<ManagedRequestMaterialization> MaterializeAsync(StoredManagedRequest work, ManagedItemSnapshot snapshot, CancellationToken token) {
            Materialized = true;
            return Task.FromResult(new ManagedRequestMaterialization(true));
        }
        public Task ValidateHoldingAsync(StoredManagedRequest work, ManagedItemSnapshot snapshot, CancellationToken token) {
            HoldingValidated = true;
            return InvalidBoundary ? throw new ArgumentException("Holding moved outside the mapped library") : Task.CompletedTask;
        }
        public Task<ManagedLibraryPage> SearchLibraryAsync(string pluginId, IntegrationConnectionContext connection, ManagedLibraryQuery input, CancellationToken token) => throw new NotImplementedException();
        public Task<ManagedItemSnapshot> GetLibraryItemAsync(string pluginId, IntegrationConnectionContext connection, ManagedItemInput input, CancellationToken token) {
            if (HoldingRemoved)
                return Task.FromException<ManagedItemSnapshot>(new IntegrationInvocationException(
                    "Provider display text may change.", IntegrationErrorCode.ManagedItemNotFound));
            if (FailObservationOnce) {
                FailObservationOnce = false;
                return Task.FromException<ManagedItemSnapshot>(new IntegrationInvocationException("Temporary GET failure."));
            }
            return Task.FromResult(Holding());
        }
        public Task<ManagerOptions> GetOptionsAsync(string pluginId, IntegrationConnectionContext connection, ManagerOptionsInput input, CancellationToken token) => throw new NotImplementedException();
        public Task QueueAsync(Guid id, CancellationToken token) => throw new NotImplementedException();
        public Task QueueDueAsync(CancellationToken token) => throw new NotImplementedException();
        Task<StoredManagedControl?> IManagedControlStore.FindAsync(Guid id, CancellationToken token) =>
            Task.FromResult<StoredManagedControl?>(new(
                new(new(id, connection.State.Id, id, 1, false, true, ManagedControlPhase.PendingSearch)),
                null!,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                null));
        Task<OwnedManagedControlScope> IManagedControlStore.RequireScopeAsync(
            Guid connectionId, Guid holdingId, CancellationToken token) => throw new NotImplementedException();
        Task<OwnedManagedControlScope> IManagedControlStore.RequireScopeAsync(
            Guid connectionId, Guid holdingId, IReadOnlyList<Guid> entityIds, CancellationToken token) =>
            throw new NotImplementedException();
        Task<IReadOnlyList<StoredManagedControl>> IManagedControlStore.ListAsync(
            Guid connectionId, Guid holdingId, CancellationToken token) => throw new NotImplementedException();
        Task<StoredManagedControl> IManagedControlStore.CreateAsync(
            ManagedControlOperation operation, ManagedControlPlan plan, CancellationToken token) => throw new NotImplementedException();
        Task IManagedControlStore.SaveAsync(
            ManagedControlOperation operation, long expectedRevision, string? problem, bool beforeDispatch, CancellationToken token) =>
            throw new NotImplementedException();
        Task IManagedControlStore.QueueAsync(Guid id, CancellationToken token) => throw new NotImplementedException();
        Task IManagedControlStore.QueueDueAsync(CancellationToken token) => throw new NotImplementedException();
        Task<ManagedTrackingWork?> IManagedTrackingStore.FindAsync(Guid id, CancellationToken token) =>
            Task.FromResult<ManagedTrackingWork?>(new(new(id, connection.State.Id, Saved.Operation.State.LibraryRootId,
                new(EntityKind.Movie, "1", Saved.Plan.Creation.Work.ExternalIds), "Film", ManagedTrackingStatus.WaitingForFiles,
                Saved.Operation.State.Revision, DateTimeOffset.UtcNow, null, [],
                [new(new("1", EntityKind.Movie, null, null, null), Saved.Operation.State.EntityId)]), []));
        Task<IReadOnlyList<ManagedTrackingResponse>> IManagedTrackingStore.ListAsync(Guid connectionId, CancellationToken token) => throw new NotImplementedException();
        Task<ManagedTrackingObservation> IManagedTrackingStore.ObserveAsync(Guid connectionId, ManagedItemSnapshot snapshot, CancellationToken token) => throw new NotImplementedException();
        Task<ManagedTrackingResponse> IManagedTrackingStore.CreateAsync(Guid connectionId, TrackManagedHoldingRequest request, string title, CancellationToken token) => throw new NotImplementedException();
        Task IManagedTrackingStore.ApplyAsync(ManagedTrackingWork work, ManagedTrackingObservation observation, IReadOnlyList<ManagedFileBinding>? adoption, IReadOnlyList<ManagedSourceChange> changes, CancellationToken token) => throw new NotImplementedException();
        Task IManagedTrackingStore.ConfirmRemovalAsync(ManagedTrackingWork work, string problem, CancellationToken token) {
            Saved.Operation.ConfirmRemoteRemoval(); ConfirmedRemoval = true; return Task.CompletedTask;
        }
        Task IManagedTrackingStore.RecordProblemAsync(Guid id, long revision, ManagedTrackingStatus status, string problem, CancellationToken token) => Task.CompletedTask;
        Task IManagedTrackingStore.QueueAsync(Guid connectionId, Guid id, CancellationToken token) => throw new NotImplementedException();
        Task IManagedTrackingStore.QueueDueAsync(CancellationToken token) => throw new NotImplementedException();
        Task<StoredIntegrationConnection?> IIntegrationConnectionStore.FindAsync(Guid id, CancellationToken token) => Task.FromResult<StoredIntegrationConnection?>(new(connection, []));
        public Task<IReadOnlyDictionary<string,string>> ReadSecretsAsync(Guid id, IReadOnlyCollection<string> credentialKeys, CancellationToken token) => Task.FromResult<IReadOnlyDictionary<string,string>>(new Dictionary<string,string>());
        public Task<PluginManifest?> FindAsync(string id, CancellationToken token) => Task.FromResult<PluginManifest?>(Manifest);
        public Task<ConnectionProbeResult> ProbeAsync(string pluginId, IntegrationConnectionContext context, CancellationToken token) => throw new NotImplementedException();
        public Task<IReadOnlyList<StoredIntegrationConnection>> ListAsync(CancellationToken token) => throw new NotImplementedException();
        public Task SaveAsync(IntegrationConnection value, long? revision, IReadOnlyDictionary<string,string?> secrets, CancellationToken token) => throw new NotImplementedException();
        public Task DeleteAsync(Guid id, long revision, CancellationToken token) => throw new NotImplementedException();
    }
}
