using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Prismedia.Application.Integrations;
using Prismedia.Application.Entities;
using Prismedia.Application.Jobs;
using Prismedia.Application.Plugins;
using Prismedia.Application.Requests;
using Prismedia.Application.Settings;
using Prismedia.Contracts.Integrations;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Plugins;
using Prismedia.Contracts.Requests;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;
using Prismedia.Infrastructure.Integrations;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;
using Prismedia.Infrastructure.Plugins;
using Prismedia.Infrastructure.Settings;
using DomainEntityExternalId = Prismedia.Domain.Entities.EntityExternalId;

namespace Prismedia.Infrastructure.Tests;

public sealed class ExternalPeopleEnrichmentRunnerTests {
    [Fact]
    public async Task SavedProviderConfigurationChangeProducesFreshRetryFingerprint() {
        await using var db = CreateContext();
        var fixture = await SeedAsync(db);
        var item = new ManagedItemInput(EntityKind.VideoSeries, "remote-series", new Dictionary<string, string> {
            [ExternalIdProviders.Tvdb] = "42"
        });
        var holding = await db.ManagedHoldings.SingleAsync();
        holding.ItemJson = JsonSerializer.Serialize(item, PluginProcessTransport.JsonOptions);
        db.ManagedSourceBindings.Add(new ManagedSourceBindingRow {
            Id = Guid.NewGuid(),
            HoldingId = fixture.HoldingId,
            RemoteTargetId = "remote-series",
            Kind = EntityKind.VideoSeries,
            EntityId = fixture.EntityId,
            SourceFileId = Guid.NewGuid(),
            RemoteFileId = "remote-file",
            LocalPath = "/library/series.mkv",
            SizeBytes = 1,
            WrittenAt = DateTimeOffset.UtcNow,
            IsAvailable = true
        });
        var now = DateTimeOffset.UtcNow;
        db.AppSettings.AddRange(
            new AppSettingRow { Key = AppSettings.AutoIdentify.Enabled.Key, ValueJson = "true", CreatedAt = now, UpdatedAt = now },
            new AppSettingRow { Key = AppSettings.AutoIdentify.Providers.Key, ValueJson = "[\"tmdb\"]", CreatedAt = now, UpdatedAt = now },
            new AppSettingRow { Key = AppSettings.AutoIdentify.EntityKinds.Key, ValueJson = "[\"video\"]", CreatedAt = now, UpdatedAt = now });
        var providerConfig = new ProviderConfigRow {
            Id = Guid.NewGuid(),
            ProviderCode = "tmdb",
            DisplayName = "TMDB",
            ProviderType = ProviderType.ExternalProcess,
            Enabled = true,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.ProviderConfigs.Add(providerConfig);
        await db.SaveChangesAsync();

        var route = new PluginIdentityRoute("tmdb", new ExternalIdentity(ExternalIdProviders.Tvdb, "42"));
        var resolver = new ExternalPeopleEnrichmentPlanResolver(
            db,
            new SettingsService(new EfSettingsPersistence(db)),
            new FixedIdentityRouter(route),
            new FixedIdentifyProvider(new PluginProvider(
                "tmdb", "TMDB", "1.0.0", true, true, false, [], [], [])),
            new IntegrationConnectionAccess(new MissingConnectionStore(), new MissingIntegrationGateway()));

        var before = Assert.IsType<ExternalPeopleEnrichmentPlan>(
            await resolver.ResolveAsync(fixture.HoldingId, default));
        providerConfig.UpdatedAt = now.AddMinutes(1);
        await db.SaveChangesAsync();
        var after = Assert.IsType<ExternalPeopleEnrichmentPlan>(
            await resolver.ResolveAsync(fixture.HoldingId, default));

        Assert.NotEqual(before.Fingerprint, after.Fingerprint);
        Assert.Equal(route, Assert.Single(after.MetadataRoutes));
    }

    [Fact]
    public async Task SchedulerBackfillsOnceAndAttachesPinnedIdentityBeforeQueueing() {
        await using var db = CreateContext();
        var fixture = await SeedAsync(db);
        var route = new PluginIdentityRoute("tmdb", new ExternalIdentity(ExternalIdProviders.Tvdb, "42"));
        var plan = Plan(fixture, route);
        var lifecycle = new ImmediateLifecycleLease();
        var identities = new RecordingIdentityStore(() => lifecycle.Inside);
        var jobs = new RecordingJobQueue();
        var scheduler = new ExternalPeopleEnrichmentScheduler(
            db,
            new StubPlanResolver(plan),
            identities,
            lifecycle,
            jobs,
            TimeProvider.System);

        await scheduler.ScheduleAsync(fixture.HoldingId, default);
        await scheduler.ScheduleAsync(fixture.HoldingId, default);

        var queued = Assert.Single(jobs.Enqueued);
        Assert.Equal(JobType.AutoIdentify, queued.Type);
        Assert.Equal(fixture.EntityId.ToString(), queued.TargetEntityId);
        var payload = AutoIdentifyJobPayload.Parse(queued.PayloadJson);
        Assert.Equal(fixture.HoldingId, payload.ExternalPeopleHoldingId);
        Assert.Equal(plan.Fingerprint, payload.ExternalPeopleFingerprint);
        Assert.Equal(ExternalIdentityWriteMode.AddMissing, identities.LastWriteMode);
        Assert.Equal(new ExternalIdentity(ExternalIdProviders.Tvdb, "42"), Assert.Single(identities.Written).Identity);
        Assert.True(identities.ReadsObservedInsideLifecycle);
    }

    [Fact]
    public async Task ExactConfiguredProposalAppliesOnlyCreditsAndMarksHoldingComplete() {
        await using var db = CreateContext();
        var fixture = await SeedAsync(db);
        var route = new PluginIdentityRoute("tmdb", new ExternalIdentity(ExternalIdProviders.Tvdb, "42"));
        var plan = Plan(fixture, route);
        var proposal = Proposal(credits: [new CreditPatch("Lead Actor", CreditRole.Actor.ToCode(), "Hero", 0)]) with {
            Children = [Proposal(credits: [new CreditPatch("Episode Guest", CreditRole.Actor.ToCode(), null, 0)])]
        };
        var proposals = new StubProposalSource(proposal);
        var identify = new RecordingCreditsApplier();
        var runner = new ExternalPeopleEnrichmentRunner(
            db,
            new StubPlanResolver(plan),
            new StubManagerGateway(),
            proposals,
            identify,
            TimeProvider.System);

        var result = await runner.RunAsync(
            fixture.HoldingId,
            fixture.EntityId,
            plan.Fingerprint,
            CancellationToken.None);

        Assert.True(result.Applied);
        Assert.Equal(route, proposals.LastRoute);
        Assert.Empty(Assert.IsType<EntityMetadataProposal>(identify.AppliedProposal).Children);
        Assert.Equal(fixture.EntityId, identify.AppliedProposal.TargetEntityId);
        var holding = await db.ManagedHoldings.SingleAsync();
        Assert.Equal(plan.Fingerprint, holding.PeopleEnrichmentFingerprint);
        Assert.NotNull(holding.PeopleEnrichmentCompletedAt);
    }

    [Fact]
    public async Task ManualCreditsClearBeforeFirstEnrichmentRemainsEmpty() {
        await using var db = CreateContext();
        var fixture = await SeedAsync(db);
        var metadata = new EntityMetadataApplyService(
            db,
            new PluginArtworkServiceOptions(Path.GetTempPath()));
        Assert.True(await metadata.ApplyPatchAsync(
            fixture.EntityId,
            new EntityMetadataUpdateRequest(
                [MetadataPatchField.Credits.ToCode()],
                Proposal([]).Patch),
            default));
        var route = new PluginIdentityRoute("tmdb", new ExternalIdentity(ExternalIdProviders.Tvdb, "42"));
        var plan = Plan(fixture, route);
        var proposals = new StubProposalSource(
            Proposal([new CreditPatch("Provider Actor", CreditRole.Actor.ToCode(), null, 0)]));
        var runner = new ExternalPeopleEnrichmentRunner(
            db,
            new StubPlanResolver(plan),
            new StubManagerGateway(),
            proposals,
            metadata,
            TimeProvider.System);

        var result = await runner.RunAsync(
            fixture.HoldingId,
            fixture.EntityId,
            plan.Fingerprint,
            default);

        Assert.False(result.Applied);
        Assert.Equal("User-protected credits preserved", result.Message);
        Assert.Empty(await db.EntityRelationshipLinks.Where(row => row.EntityId == fixture.EntityId).ToArrayAsync());
        var evidence = await db.EntityMetadataFields.FindAsync(fixture.EntityId, MetadataPatchField.Credits);
        Assert.NotNull(evidence);
        Assert.Equal(MetadataValueOrigin.User, evidence.Origin);
        Assert.True(evidence.IsCleared);
        Assert.True(evidence.IsLocked);
        Assert.NotNull((await db.ManagedHoldings.SingleAsync()).PeopleEnrichmentCompletedAt);
    }

    [Fact]
    public async Task ExactManagerCreditsArePreferredWithoutCallingConfiguredMetadata() {
        await using var db = CreateContext();
        var fixture = await SeedAsync(db);
        var item = new ManagedItemInput(EntityKind.Movie, "remote-movie", new Dictionary<string, string> {
            [ExternalIdProviders.Tmdb] = "99"
        });
        var connectionId = Guid.NewGuid();
        var connection = new IntegrationConnection(new IntegrationConnectionState(
            connectionId, "radarr", "Radarr", "http://radarr.test/", true,
            [PluginCapability.ExternalManager], new Dictionary<string, string>(), 2, ConnectionStatus.Ready,
            "instance", [], DateTimeOffset.UtcNow, null, true));
        var manifest = new PluginManifest(
            2, ["prismedia"], "radarr", "Radarr", "1.0.0", "dotnet-process", "plugin.dll",
            new("2.0.0", null, "3.8.0", null), [], false, []);
        var managerContext = new AuthorizedIntegrationConnection(
            connection,
            manifest,
            new(connectionId, "http://radarr.test/", "instance", new Dictionary<string, string>(), new Dictionary<string, string>()));
        var plan = new ExternalPeopleEnrichmentPlan(
            fixture.HoldingId, fixture.EntityId, EntityKind.Movie, item, "manager-fingerprint", managerContext, []);
        var gateway = new StubManagerGateway {
            Result = new ManagedLookupResult(new ManagedCandidate(
                EntityKind.Movie,
                "Movie",
                2024,
                item.ExpectedExternalIds,
                new ManagedDiscoveryMetadata(Credits: [new ManagedPersonCredit(
                    "Director",
                    CreditRole.Director,
                    null,
                    0,
                    new Dictionary<string, string> { [ExternalIdProviders.Tmdb] = "100" },
                    "https://images.example/director.jpg")])),
                Existing: null)
        };
        var proposals = new StubProposalSource(Proposal([]));
        var identify = new RecordingCreditsApplier();
        var runner = new ExternalPeopleEnrichmentRunner(
            db, new StubPlanResolver(plan), gateway, proposals, identify, TimeProvider.System);

        var result = await runner.RunAsync(fixture.HoldingId, fixture.EntityId, plan.Fingerprint, default);

        Assert.True(result.Applied);
        Assert.Equal("radarr", result.Provider);
        Assert.Equal(1, gateway.LookupCalls);
        Assert.Null(proposals.LastRoute);
        Assert.Equal("Director", Assert.Single(identify.AppliedProposal!.Patch.Credits).Name);
        var person = Assert.Single(identify.AppliedProposal.Relationships);
        Assert.Equal(EntityKind.Person, person.TargetKind);
        Assert.Equal(MediaImageKind.Profile.ToCode(), Assert.Single(person.Images).Kind);
    }

    [Fact]
    public async Task ExistingCreditsPreventEveryExternalLookupAndAreMarkedComplete() {
        await using var db = CreateContext();
        var fixture = await SeedAsync(db);
        db.EntityRelationshipLinks.Add(new EntityRelationshipLinkRow {
            EntityId = fixture.EntityId,
            RelationshipCode = RelationshipKind.Cast.ToCode(),
            Label = "Cast",
            TargetEntityId = Guid.NewGuid(),
            TargetKindCode = EntityKind.Person.ToCode(),
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var plan = Plan(fixture, new PluginIdentityRoute("tmdb", new ExternalIdentity(ExternalIdProviders.Tvdb, "42")));
        var proposals = new StubProposalSource(Proposal([new CreditPatch("Provider Actor", CreditRole.Actor.ToCode(), null, 0)]));
        var identify = new RecordingCreditsApplier();
        var runner = new ExternalPeopleEnrichmentRunner(
            db,
            new StubPlanResolver(plan),
            new StubManagerGateway(),
            proposals,
            identify,
            TimeProvider.System);

        var result = await runner.RunAsync(fixture.HoldingId, fixture.EntityId, plan.Fingerprint, default);

        Assert.False(result.Applied);
        Assert.Null(proposals.LastRoute);
        Assert.Null(identify.AppliedProposal);
        Assert.NotNull((await db.ManagedHoldings.SingleAsync()).PeopleEnrichmentCompletedAt);
    }

    [Fact]
    public async Task CreditsAddedWhileProviderRunsArePreservedByFinalLifecycleGuard() {
        await using var db = CreateContext();
        var fixture = await SeedAsync(db);
        var lease = new CreditInjectingLifecycleLease(db);
        var service = new EntityMetadataApplyService(
            db,
            new PluginArtworkServiceOptions(Path.GetTempPath()),
            lifecycle: lease);

        var result = await service.ApplyIfMissingAsync(
            fixture.EntityId,
            Proposal([new CreditPatch("Provider Actor", CreditRole.Actor.ToCode(), null, 0)]),
            default);

        Assert.Equal(ExternalPeopleCreditsApplyResult.ExistingCredits, result);
        var saved = Assert.Single(await db.EntityRelationshipLinks.ToArrayAsync());
        Assert.Equal(lease.ManualPersonId, saved.TargetEntityId);
    }

    private static ExternalPeopleEnrichmentPlan Plan(
        Fixture fixture,
        PluginIdentityRoute route) =>
        new(
            fixture.HoldingId,
            fixture.EntityId,
            EntityKind.VideoSeries,
            new ManagedItemInput(EntityKind.VideoSeries, "remote-series", new Dictionary<string, string> {
                [ExternalIdProviders.Tvdb] = "42"
            }),
            "fingerprint",
            Manager: null,
            MetadataRoutes: [route]);

    private static EntityMetadataProposal Proposal(IReadOnlyList<CreditPatch> credits) =>
        new(
            "proposal",
            "tmdb",
            EntityKind.VideoSeries,
            Confidence: null,
            MatchReason: "exact",
            new EntityMetadataPatch(
                "Series",
                null,
                new Dictionary<string, string> { [ExternalIdProviders.Tvdb] = "42" },
                [],
                [],
                null,
                credits,
                new Dictionary<string, string>(),
                new Dictionary<string, int>(),
                new Dictionary<string, int>(),
                null),
            Images: [],
            Children: [],
            Candidates: [],
            Relationships: []);

    private static async Task<Fixture> SeedAsync(PrismediaDbContext db) {
        var entityId = Guid.NewGuid();
        var holdingId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.Entities.Add(new EntityRow {
            Id = entityId,
            KindCode = EntityKind.VideoSeries.ToCode(),
            Title = "Series",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.ManagedHoldings.Add(new ManagedHoldingRow {
            Id = holdingId,
            ConnectionId = Guid.NewGuid(),
            LibraryRootId = Guid.NewGuid(),
            Kind = EntityKind.VideoSeries,
            RemoteId = "remote-series",
            Title = "Series",
            ItemJson = "{}",
            Status = ManagedTrackingStatus.Tracking,
            Revision = 1,
            NextCheckAt = now
        });
        await db.SaveChangesAsync();
        return new(holdingId, entityId);
    }

    private static PrismediaDbContext CreateContext() => new(
        new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase($"external-people-{Guid.NewGuid():N}")
            .Options);

    private sealed record Fixture(Guid HoldingId, Guid EntityId);

    private sealed class StubPlanResolver(ExternalPeopleEnrichmentPlan plan)
        : IExternalPeopleEnrichmentPlanResolver {
        public Task<ExternalPeopleEnrichmentPlan?> ResolveAsync(Guid holdingId, CancellationToken cancellationToken) =>
            Task.FromResult<ExternalPeopleEnrichmentPlan?>(plan);
    }

    private sealed class FixedIdentityRouter(params PluginIdentityRoute[] routes) : IPluginIdentityRouter {
        public Task<IReadOnlyList<PluginIdentityRoute>> ResolveAsync(
            string entityKindCode,
            IdentifyAction action,
            IReadOnlyList<ExternalIdentity> identities,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PluginIdentityRoute>>(routes);
    }

    private sealed class FixedIdentifyProvider(params PluginProvider[] providers) : IIdentifyProviderService {
        public Task<IReadOnlyList<PluginProvider>> ListProvidersAsync(
            string? entityKind,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PluginProvider>>(providers);

        public Task<IdentifyPluginResponse> IdentifyAsync(
            Guid entityId,
            string providerId,
            IdentifyQuery? query,
            IReadOnlyDictionary<string, string>? parentExternalIds,
            bool hideNsfw,
            CancellationToken cancellationToken,
            bool cascadeChildren = true,
            IIdentifyCascadeSink? sink = null,
            bool hydrateRelationships = true) => throw new NotSupportedException();

        public Task<bool> ApplyAsync(
            Guid entityId,
            EntityMetadataProposal proposal,
            IReadOnlyCollection<string> selectedFields,
            IReadOnlyDictionary<string, string?>? selectedImages,
            CancellationToken cancellationToken,
            IIdentifyApplyProgressReporter? progress = null) => throw new NotSupportedException();
    }

    private sealed class MissingConnectionStore : IIntegrationConnectionStore {
        public Task<IReadOnlyList<StoredIntegrationConnection>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<StoredIntegrationConnection>>([]);
        public Task<StoredIntegrationConnection?> FindAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<StoredIntegrationConnection?>(null);
        public Task SaveAsync(IntegrationConnection connection, long? expectedRevision, IReadOnlyDictionary<string, string?> secretChanges, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<IReadOnlyDictionary<string, string>> ReadSecretsAsync(Guid id, IReadOnlyCollection<string> credentialKeys, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task DeleteAsync(Guid id, long expectedRevision, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class MissingIntegrationGateway : IIntegrationPluginGateway {
        public Task<PluginManifest?> FindAsync(string pluginId, CancellationToken cancellationToken) =>
            Task.FromResult<PluginManifest?>(null);
        public Task<ConnectionProbeResult> ProbeAsync(
            string pluginId,
            IntegrationConnectionContext connection,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubManagerGateway : IIntegrationManagerCreationGateway {
        public ManagedLookupResult? Result { get; init; }
        public int LookupCalls { get; private set; }

        public Task<ManagedLookupResult> LookupAsync(
            string pluginId,
            IntegrationConnectionContext connection,
            ManagedLookupInput input,
            CancellationToken token) {
            LookupCalls++;
            return Task.FromResult(Result ?? throw new NotSupportedException());
        }

        public Task<EnsureManagedResult> EnsureAsync(
            string pluginId,
            IntegrationConnectionContext connection,
            EnsureManagedInput input,
            CancellationToken token) => throw new NotSupportedException();
    }

    private sealed class StubProposalSource(EntityMetadataProposal proposal) : IPluginRequestProposalSource {
        public PluginIdentityRoute? LastRoute { get; private set; }

        public Task<EntityMetadataProposal?> ResolveProposalAsync(
            RequestKindDescriptor descriptor,
            PluginIdentityRoute route,
            bool hideNsfw,
            bool includeChildren,
            CancellationToken cancellationToken) {
            LastRoute = route;
            Assert.False(includeChildren);
            return Task.FromResult<EntityMetadataProposal?>(proposal);
        }

        public Task<RoutedRequestProposal?> ResolveProposalAsync(RequestKindDescriptor descriptor, ExternalIdentity identity, bool hideNsfw, bool includeChildren, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<EntityMetadataProposal?> ResolveFreshProposalAsync(RequestKindDescriptor descriptor, PluginIdentityRoute route, bool hideNsfw, bool includeChildren, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<RequestReviewResponse?> ResolveFreshReviewAsync(RequestKindDescriptor descriptor, PluginIdentityRoute route, bool hideNsfw, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RecordingCreditsApplier : IExternalPeopleCreditsApplier {
        public EntityMetadataProposal? AppliedProposal { get; private set; }

        public Task<ExternalPeopleCreditsApplyResult> ApplyIfMissingAsync(
            Guid entityId,
            EntityMetadataProposal proposal,
            CancellationToken cancellationToken) {
            AppliedProposal = proposal;
            return Task.FromResult(ExternalPeopleCreditsApplyResult.Applied);
        }
    }

    private sealed class CreditInjectingLifecycleLease(PrismediaDbContext db) : IEntityLifecycleMutationLease {
        public Guid ManualPersonId { get; } = Guid.NewGuid();

        public async Task<bool> ExecuteAsync(
            Guid entityId,
            Func<CancellationToken, Task> mutation,
            CancellationToken cancellationToken) {
            db.EntityRelationshipLinks.Add(new EntityRelationshipLinkRow {
                EntityId = entityId,
                RelationshipCode = RelationshipKind.Cast.ToCode(),
                Label = "Cast",
                TargetEntityId = ManualPersonId,
                TargetKindCode = EntityKind.Person.ToCode(),
                CreatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync(cancellationToken);
            await mutation(cancellationToken);
            return true;
        }
    }

    private sealed class RecordingIdentityStore(Func<bool>? insideLifecycle = null) : IEntityExternalIdentityStore {
        public IReadOnlyList<DomainEntityExternalId> Written { get; private set; } = [];
        public ExternalIdentityWriteMode? LastWriteMode { get; private set; }
        public bool ReadsObservedInsideLifecycle { get; private set; } = true;

        public Task<IReadOnlyList<DomainEntityExternalId>> ListAsync(Guid entityId, CancellationToken cancellationToken) {
            ReadsObservedInsideLifecycle &= insideLifecycle?.Invoke() ?? true;
            return Task.FromResult<IReadOnlyList<DomainEntityExternalId>>([]);
        }

        public Task<ExternalIdentityResolution> ResolveAsync(EntityKind kind, IReadOnlyCollection<ExternalIdentity> identities, Guid? parentEntityId, CancellationToken cancellationToken) {
            ReadsObservedInsideLifecycle &= insideLifecycle?.Invoke() ?? true;
            return Task.FromResult(new ExternalIdentityResolution([]));
        }

        public Task WriteAsync(Guid entityId, IReadOnlyCollection<DomainEntityExternalId> identities, ExternalIdentityWriteMode mode, CancellationToken cancellationToken) {
            Written = identities.ToArray();
            LastWriteMode = mode;
            return Task.CompletedTask;
        }
    }

    private sealed class ImmediateLifecycleLease : IEntityLifecycleMutationLease {
        public bool Inside { get; private set; }

        public async Task<bool> ExecuteAsync(Guid entityId, Func<CancellationToken, Task> mutation, CancellationToken cancellationToken) {
            Inside = true;
            try {
                await mutation(cancellationToken);
                return true;
            } finally {
                Inside = false;
            }
        }
    }

    private sealed class RecordingJobQueue : IJobQueueService {
        public List<EnqueueJobRequest> Enqueued { get; } = [];

        public Task<JobRunSnapshot> EnqueueAsync(EnqueueJobRequest request, CancellationToken cancellationToken) {
            Enqueued.Add(request);
            return Task.FromResult(Snapshot(request));
        }

        public Task<bool> HasPendingAsync(JobType type, string? targetEntityId, CancellationToken cancellationToken) =>
            Task.FromResult(Enqueued.Any(request => request.Type == type && request.TargetEntityId == targetEntityId));

        public Task<IReadOnlyList<JobRunSnapshot>> ListAsync(bool hideNsfw, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<JobRunSnapshot>>([]);
        public Task<JobRunSnapshot> EnqueueAsync(JobType type, CancellationToken cancellationToken) => EnqueueAsync(new EnqueueJobRequest(type), cancellationToken);
        public Task<int> EnqueueBatchAsync(IReadOnlyList<EnqueueJobRequest> requests, CancellationToken cancellationToken) => Task.FromResult(0);
        public Task<int> CancelAsync(JobType? type, CancellationToken cancellationToken) => Task.FromResult(0);
        public Task<bool> CancelRunAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<int> ClearFailuresAsync(JobType? type, CancellationToken cancellationToken) => Task.FromResult(0);
        public Task<JobRunSnapshot?> ClaimNextAsync(string workerId, CancellationToken cancellationToken) => Task.FromResult<JobRunSnapshot?>(null);
        public Task<int> RecoverStaleRunningAsync(string currentWorkerId, TimeSpan staleAfter, CancellationToken cancellationToken) => Task.FromResult(0);
        public Task UpdateProgressAsync(Guid id, int progress, string? message, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task CompleteAsync(Guid id, string? message, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task FailAsync(Guid id, string message, TimeSpan retryDelay, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DeferAsync(Guid id, string message, TimeSpan retryDelay, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<JobQueueCount>> GetQueueCountsAsync(bool hideNsfw, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<JobQueueCount>>([]);
        public Task<int> PruneHistoryAsync(TimeSpan retention, CancellationToken cancellationToken) => Task.FromResult(0);

        private static JobRunSnapshot Snapshot(EnqueueJobRequest request) => new(
            Guid.NewGuid(), request.Type, JobRunStatus.Queued, 0, null, request.PayloadJson ?? "{}",
            request.TargetEntityKind, request.TargetEntityId, request.TargetLabel, DateTimeOffset.UtcNow, null, null);
    }
}
