using Prismedia.Application.Entities;
using Prismedia.Application.Integrations;
using Prismedia.Application.Plugins;
using Prismedia.Application.Requests;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Integrations;
using Prismedia.Contracts.Plugins;
using Prismedia.Contracts.Requests;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;

namespace Prismedia.Application.Tests;

public sealed class ManagedDiscoveryServiceTests {
    [Fact]
    public async Task ForgedReviewedProposalIsRejectedBeforeWantedMaterialization() {
        var fixture = new Fixture();
        var reviewed = await fixture.Service.ReviewAsync(fixture.Connection.State.Id,
            new(EntityKind.Movie, fixture.Identity), default);
        var forged = reviewed.Review.Proposal with {
            Patch = reviewed.Review.Proposal.Patch with { Title = "Client-forged title" }
        };
        var request = Commit(reviewed.Review, forged);

        await Assert.ThrowsAsync<RequestCommitValidationException>(() => fixture.Service.PrepareAsync(
            fixture.Connection.State.Id, new(reviewed.ConnectionRevision, request), default));

        Assert.Equal(0, fixture.Writer.EnsureCalls);
        Assert.Equal(2, fixture.LookupCalls);
    }

    [Fact]
    public async Task ChangedConnectionRevisionIsRejectedBeforeWantedMaterialization() {
        var fixture = new Fixture();
        var reviewed = await fixture.Service.ReviewAsync(fixture.Connection.State.Id,
            new(EntityKind.Movie, fixture.Identity), default);

        await Assert.ThrowsAsync<ConnectionConflictException>(() => fixture.Service.PrepareAsync(
            fixture.Connection.State.Id,
            new(reviewed.ConnectionRevision - 1, Commit(reviewed.Review, reviewed.Review.Proposal)),
            default));

        Assert.Equal(0, fixture.Writer.EnsureCalls);
    }

    [Fact]
    public async Task SearchReturnsCanonicalIdentityAndPerformsNoLookupOrWantedWrite() {
        var fixture = new Fixture();
        var response = await fixture.Service.SearchAsync(fixture.Connection.State.Id,
            new(EntityKind.Movie, "Metropolis", 10), default);

        Assert.Equal(fixture.Identity, Assert.Single(response.Items).ExternalIdentity);
        Assert.Equal(1, fixture.DiscoveryCalls);
        Assert.Equal(0, fixture.LookupCalls);
        Assert.Equal(0, fixture.Writer.EnsureCalls);
    }

    [Fact]
    public async Task ComicRunSearchKeepsTheExactComicVineSeriesIdentityWithoutWriting() {
        var fixture = new Fixture(comic: true);

        var response = await fixture.Service.SearchAsync(fixture.Connection.State.Id,
            new(EntityKind.ComicSeries, "Atomic Attack", 10), default);

        var result = Assert.Single(response.Items);
        Assert.Equal(new ExternalIdentity(ExternalIdProviders.ComicVine, "4050-1001"), result.ExternalIdentity);
        Assert.Equal(0, fixture.LookupCalls);
        Assert.Equal(0, fixture.Writer.EnsureCalls);
    }

    [Fact]
    public async Task SeriesSearchUsesSonarrCanonicalTvdbIdentityAndOpensNormalFiniteMetadataReview() {
        var router = new SeriesRouter();
        var reviews = new SeriesReviewPreparation();
        var fixture = new Fixture(series: true, router, reviews);

        var searched = await fixture.Service.SearchAsync(fixture.Connection.State.Id,
            new(EntityKind.VideoSeries, "Breaking Bad", 10), default);
        var result = Assert.Single(searched.Items);
        Assert.Equal(new ExternalIdentity(ExternalIdProviders.Tvdb, "81189"), result.ExternalIdentity);
        Assert.Equal("https://images.example.test/series-poster.jpg", result.Metadata!.PosterUrl);

        var reviewed = await fixture.Service.ReviewAsync(fixture.Connection.State.Id,
            new(EntityKind.VideoSeries, result.ExternalIdentity), default);

        Assert.Equal("tmdb", reviewed.Review.PluginId);
        Assert.Equal(RequestMediaKind.Series, reviewed.Review.Kind);
        Assert.Equal(EntityKind.VideoSeries, reviewed.Review.EntityKind);
        var season = Assert.Single(reviewed.Review.Proposal.Children);
        Assert.Equal(EntityKind.VideoSeason, season.TargetKind);
        Assert.Equal(EntityKind.VideoEpisode, Assert.Single(season.Children).TargetKind);
        Assert.Equal("81189", reviewed.Review.Proposal.Patch.ExternalIds[ExternalIdProviders.Tvdb]);
        Assert.Equal(1, router.Calls);
        Assert.Equal(1, reviews.Calls);

        var commit = Commit(reviewed.Review, reviewed.Review.Proposal);
        var canonical = await fixture.Service.CanonicalizeAsync(
            fixture.Connection.State.Id, reviewed.ConnectionRevision, commit, default);

        Assert.Equal(new ExternalIdentity(ExternalIdProviders.Tmdb, "1396"), canonical.Request.RootExternalIdentity);
        Assert.Equal(ExternalIdProviders.Tvdb, fixture.LookupInputs.Last().ExternalIds.Single().Key);
        Assert.Equal("81189", fixture.LookupInputs.Last().ExternalIds.Single().Value);
    }

    [Fact]
    public async Task SeriesReviewUsesConfiguredProviderOrderBeforeAlphabeticalRouteOrder() {
        var router = new SeriesRouter("alphabetical-fixture", "tmdb");
        var reviews = new SeriesReviewPreparation();
        var providers = new SeriesProviders("tmdb", "alphabetical-fixture");
        var fixture = new Fixture(series: true, router, reviews, providers);

        var reviewed = await fixture.Service.ReviewAsync(fixture.Connection.State.Id,
            new(EntityKind.VideoSeries, new(ExternalIdProviders.Tvdb, "81189")), default);

        Assert.Equal("tmdb", reviewed.Review.PluginId);
        Assert.Equal(["tmdb"], reviews.ProviderCalls);
    }

    [Fact]
    public async Task ReviewFromAnotherConnectionCannotBePreparedWhenRevisionsMatch() {
        var fixture = new Fixture();
        var reviewed = await fixture.Service.ReviewAsync(fixture.Connection.State.Id,
            new(EntityKind.Movie, fixture.Identity), default);

        await Assert.ThrowsAsync<RequestProposalChangedException>(() => fixture.Service.PrepareAsync(
            fixture.OtherConnection.State.Id,
            new(reviewed.ConnectionRevision, Commit(reviewed.Review, reviewed.Review.Proposal)),
            default));

        Assert.Equal(fixture.Connection.State.Revision, fixture.OtherConnection.State.Revision);
        Assert.Equal(0, fixture.Writer.EnsureCalls);
    }

    [Fact]
    public async Task FreshExactReviewPreparesWantedMovieWithoutMetadataPluginBinding() {
        var fixture = new Fixture();
        var reviewed = await fixture.Service.ReviewAsync(fixture.Connection.State.Id,
            new(EntityKind.Movie, fixture.Identity), default);

        var prepared = await fixture.Service.PrepareAsync(fixture.Connection.State.Id,
            new(reviewed.ConnectionRevision, Commit(reviewed.Review, reviewed.Review.Proposal)), default);

        Assert.False(prepared.HasFile);
        Assert.Equal(1, fixture.Writer.EnsureCalls);
        Assert.Equal(1, fixture.Writer.ApplyCalls);
    }

    [Fact]
    public async Task ExactReviewProjectsManagerCreditsIntoStandardPeopleRelationships() {
        var fixture = new Fixture();

        var reviewed = await fixture.Service.ReviewAsync(fixture.Connection.State.Id,
            new(EntityKind.Movie, fixture.Identity), default);

        var credits = reviewed.Review.Proposal.Patch.Credits;
        var actor = Assert.Single(credits, credit => credit.Role == CreditRole.Actor.ToCode());
        Assert.Equal("Lead Actor", actor.Name);
        Assert.Equal("Hero", actor.Character);
        var person = Assert.Single(reviewed.Review.Proposal.Relationships,
            relationship => relationship.Patch.Title == actor.Name);
        Assert.Equal(EntityKind.Person, person.TargetKind);
        Assert.Equal("101", person.Patch.ExternalIds[ExternalIdProviders.Tmdb]);
        Assert.Equal("https://www.themoviedb.org/person/101", Assert.Single(person.Patch.Urls));
        Assert.Equal(MediaImageKind.Profile.ToCode(), Assert.Single(person.Images).Kind);
    }

    [Fact]
    public async Task InvalidManagerPersonEvidenceIsRejectedBeforeReview() {
        var fixture = new Fixture {
            Credits = [new("Lead Actor", CreditRole.Actor, null, 0, ProfileUrl: "http://127.0.0.1/person.jpg")]
        };

        await Assert.ThrowsAsync<IntegrationInvocationException>(() => fixture.Service.ReviewAsync(
            fixture.Connection.State.Id,
            new(EntityKind.Movie, fixture.Identity),
            default));

        Assert.Equal(0, fixture.Writer.EnsureCalls);
    }

    [Fact]
    public async Task NoncanonicalManagerPersonTmdbIdentityIsRejectedBeforeUrlProjection() {
        var fixture = new Fixture {
            Credits = [new("Lead Actor", CreditRole.Actor, null, 0,
                new Dictionary<string, string> { [ExternalIdProviders.Tmdb] = "0" })]
        };

        await Assert.ThrowsAsync<IntegrationInvocationException>(() => fixture.Service.ReviewAsync(
            fixture.Connection.State.Id,
            new(EntityKind.Movie, fixture.Identity),
            default));

        Assert.Equal(0, fixture.Writer.EnsureCalls);
    }

    private static ReviewedRequestCommitRequest Commit(RequestReviewResponse review, EntityMetadataProposal proposal) => new(
        review.Kind,
        review.PluginId,
        review.ExternalIdentity,
        review.Revision,
        [review.Proposal.ProposalId],
        Review: review,
        Proposal: proposal,
        SelectedFields: [MetadataPatchField.Title.ToCode(), MetadataPatchField.ExternalIds.ToCode()],
        SelectedImages: new Dictionary<string, string?>());

    private sealed class Fixture : IIntegrationConnectionStore, IIntegrationPluginGateway,
        IIntegrationManagerGateway, IIntegrationManagerCreationGateway {
        private const string PluginId = "fixture-radarr";
        internal ExternalIdentity Identity { get; }
        internal EntityKind Kind { get; }
        internal IntegrationConnection Connection { get; }
        internal IntegrationConnection OtherConnection { get; }
        internal FakeWriter Writer { get; } = new();
        internal ManagedDiscoveryService Service { get; }
        internal int DiscoveryCalls { get; private set; }
        internal int LookupCalls { get; private set; }
        internal IReadOnlyList<ManagedPersonCredit>? Credits { get; set; } = [
            new("Lead Actor", CreditRole.Actor, "Hero", 0,
                new Dictionary<string, string> { [ExternalIdProviders.Tmdb] = "101" },
                "https://images.example.test/people/101.jpg"),
            new("Director Person", CreditRole.Director, null, 1000,
                new Dictionary<string, string> { [ExternalIdProviders.Tmdb] = "202" })
        ];
        private readonly PluginManifest manifest;
        internal List<ManagedLookupInput> LookupInputs { get; } = [];

        internal Fixture(
            bool series = false,
            IPluginIdentityRouter? router = null,
            IPluginRequestReviewSource? reviews = null,
            IIdentifyProviderService? providers = null,
            bool comic = false) {
            Kind = comic ? EntityKind.ComicSeries : series ? EntityKind.VideoSeries : EntityKind.Movie;
            Identity = comic ? new(ExternalIdProviders.ComicVine, "4050-1001")
                : new(ExternalIdProviders.Tmdb, series ? "1396" : "19");
            IntegrationSupport[] support = [new(PluginCapability.ExternalManager,
                [IntegrationOperation.DiscoverManaged, IntegrationOperation.LookupManaged], [Kind])];
            Connection = IntegrationConnection.Create(PluginId, "Radarr", "https://radarr.test", true,
                [PluginCapability.ExternalManager], new Dictionary<string, string>());
            Connection.RecordProbe(null, support, null, DateTimeOffset.UtcNow, false);
            OtherConnection = IntegrationConnection.Create(PluginId, "Other Radarr", "https://other-radarr.test", true,
                [PluginCapability.ExternalManager], new Dictionary<string, string>());
            OtherConnection.RecordProbe(null, support, null, DateTimeOffset.UtcNow, false);
            manifest = new(2, [], PluginId, "Radarr", "1.0.0", "dotnet-process", "plugin.dll",
                new("2.0.0", null, "3.8.0", null), [], false, [],
                Integration: new(1, support.Select(value => new PluginIntegrationCapability(
                    value.Kind, value.Operations, value.EntityKinds)).ToArray(), []));
            var wanted = new ReviewedWantedMovieService(Writer, new Suppressions(), new NeverRouter(), new Lease());
            Service = new(new(this, this), this, this, wanted,
                router ?? new NeverRouter(), reviews ?? new NeverReviewSource(),
                providers ?? new SeriesProviders("tmdb"));
        }

        private ManagedCandidate Candidate() => Kind == EntityKind.ComicSeries
            ? new(EntityKind.ComicSeries, "Atomic Attack", 1954,
                new Dictionary<string, string> { [ExternalIdProviders.ComicVine] = Identity.Value })
            : Kind == EntityKind.VideoSeries
            ? new(EntityKind.VideoSeries, "Breaking Bad", 2008,
                new Dictionary<string, string> {
                    [ExternalIdProviders.Tmdb] = "1396",
                    [ExternalIdProviders.Tvdb] = "81189",
                    [ExternalIdProviders.Imdb] = "tt0903747"
                },
                new(Overview: "A chemistry teacher builds a criminal empire.", Studio: "AMC",
                    PosterUrl: "https://images.example.test/series-poster.jpg"))
            : new(EntityKind.Movie, "Metropolis", 1927,
                new Dictionary<string, string> { [Identity.Namespace] = Identity.Value, [ExternalIdProviders.Imdb] = "tt0017136" },
                new(Overview: "A city divided.", Studio: "UFA", Classification: "PG",
                    Tags: ["Science Fiction"], PosterUrl: "https://images.example.test/poster.jpg",
                    Credits: Credits));

        public Task<ManagedDiscoveryPage> DiscoverAsync(string pluginId, IntegrationConnectionContext connection,
            ManagedDiscoveryQuery input, CancellationToken token) {
            DiscoveryCalls++;
            var candidate = Candidate();
            return Task.FromResult(new ManagedDiscoveryPage([new(candidate.EntityKind, candidate.Title, candidate.Year,
                candidate.ExternalIds, candidate.Metadata)]));
        }
        public Task<ManagedLookupResult> LookupAsync(string pluginId, IntegrationConnectionContext connection,
            ManagedLookupInput input, CancellationToken token) {
            LookupCalls++;
            LookupInputs.Add(input);
            return Task.FromResult(new ManagedLookupResult(Candidate(), null));
        }
        public Task<EnsureManagedResult> EnsureAsync(string pluginId, IntegrationConnectionContext connection, EnsureManagedInput input, CancellationToken token) => throw new NotImplementedException();
        public Task<StoredIntegrationConnection?> FindAsync(Guid id, CancellationToken token) => Task.FromResult<StoredIntegrationConnection?>(
            id == Connection.State.Id ? new(Connection, []) : id == OtherConnection.State.Id ? new(OtherConnection, []) : null);
        public Task<IReadOnlyDictionary<string, string>> ReadSecretsAsync(Guid id, IReadOnlyCollection<string> keys, CancellationToken token) => Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());
        public Task<PluginManifest?> FindAsync(string pluginId, CancellationToken token) => Task.FromResult<PluginManifest?>(pluginId == PluginId ? manifest : null);
        public Task<ConnectionProbeResult> ProbeAsync(string pluginId, IntegrationConnectionContext connection, CancellationToken token) => throw new NotImplementedException();
        public Task<IReadOnlyList<StoredIntegrationConnection>> ListAsync(CancellationToken token) => throw new NotImplementedException();
        public Task SaveAsync(IntegrationConnection connection, long? revision, IReadOnlyDictionary<string, string?> secrets, CancellationToken token) => throw new NotImplementedException();
        public Task DeleteAsync(Guid id, long revision, CancellationToken token) => throw new NotImplementedException();
        public Task<ManagedLibraryPage> SearchLibraryAsync(string pluginId, IntegrationConnectionContext connection, ManagedLibraryQuery input, CancellationToken token) => throw new NotImplementedException();
        public Task<ManagedItemSnapshot> GetLibraryItemAsync(string pluginId, IntegrationConnectionContext connection, ManagedItemInput input, CancellationToken token) => throw new NotImplementedException();
        public Task<ManagerOptions> GetOptionsAsync(string pluginId, IntegrationConnectionContext connection, ManagerOptionsInput input, CancellationToken token) => throw new NotImplementedException();
    }

    private sealed class FakeWriter : IWantedEntityWriter {
        internal int EnsureCalls;
        internal int ApplyCalls;
        public Task<WantedEntityResult> EnsureAsync(EntityKind kind, ExternalIdentity identity, string title, Guid? parent,
            bool matchTitle, CancellationToken token) { EnsureCalls++; return Task.FromResult(new WantedEntityResult(Guid.NewGuid(), true, false)); }
        public Task<bool> BindProviderIdentityAsync(Guid id, PluginIdentityRoute route, CancellationToken token) => throw new InvalidOperationException("Manager discovery must not bind a metadata route.");
        public Task ApplyProposalAsync(Guid id, EntityMetadataProposal proposal, CancellationToken token) => Task.CompletedTask;
        public Task ApplyProposalWithDeferredArtworkAsync(Guid id, EntityMetadataProposal proposal, CancellationToken token) { ApplyCalls++; return Task.CompletedTask; }
        public Task<bool> DeleteIfWantedAsync(Guid id, CancellationToken token) => throw new NotImplementedException();
        public Task<MonitorableEntity?> GetEntityAsync(Guid id, CancellationToken token) => throw new NotImplementedException();
        public Task<IReadOnlyList<Guid>> ListWantedChildIdsAsync(Guid id, EntityKind kind, CancellationToken token) => throw new NotImplementedException();
        public Task<IReadOnlyList<Guid>> ListChildIdsAsync(Guid id, EntityKind kind, CancellationToken token) => throw new NotImplementedException();
    }
    private sealed class Suppressions : IWantedSuppressionStore {
        public Task SuppressAsync(IReadOnlyList<ExternalIdentity> identities, EntityKind kind, string title, CancellationToken token) => Task.CompletedTask;
        public Task<IReadOnlySet<ExternalIdentity>> FilterSuppressedAsync(IReadOnlyList<ExternalIdentity> identities, CancellationToken token) => Task.FromResult<IReadOnlySet<ExternalIdentity>>(new HashSet<ExternalIdentity>());
        public Task ClearAsync(IReadOnlyList<ExternalIdentity> identities, CancellationToken token) => Task.CompletedTask;
    }
    private sealed class NeverRouter : IPluginIdentityRouter {
        public Task<IReadOnlyList<PluginIdentityRoute>> ResolveAsync(string kind, IdentifyAction action, IReadOnlyList<ExternalIdentity> identities, CancellationToken token) => throw new InvalidOperationException("Manager discovery must not route as metadata.");
    }
    private sealed class NeverReviewSource : IPluginRequestReviewSource {
        public Task<RequestReviewResponse?> ReviewAsync(RequestReviewRequest request, bool hideNsfw, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Movie manager discovery must not route as metadata.");
    }
    private sealed class SeriesRouter(params string[] providerIds) : IPluginIdentityRouter {
        internal int Calls;
        public Task<IReadOnlyList<PluginIdentityRoute>> ResolveAsync(
            string kind,
            IdentifyAction action,
            IReadOnlyList<ExternalIdentity> identities,
            CancellationToken token) {
            Calls++;
            Assert.Equal(EntityKind.VideoSeries.ToCode(), kind);
            Assert.Equal(IdentifyAction.LookupId, action);
            var ids = providerIds.Length == 0 ? ["tmdb"] : providerIds;
            return Task.FromResult<IReadOnlyList<PluginIdentityRoute>>(ids.Select(provider =>
                new PluginIdentityRoute(provider,
                    identities.Single(identity => identity.Namespace == ExternalIdProviders.Tmdb))).ToArray());
        }
    }
    private sealed class SeriesReviewPreparation : IPluginRequestReviewSource {
        internal int Calls;
        internal List<string> ProviderCalls { get; } = [];
        public Task<RequestReviewResponse?> ReviewAsync(
            RequestReviewRequest request,
            bool hideNsfw,
            CancellationToken cancellationToken) {
            Calls++;
            ProviderCalls.Add(request.PluginId);
            var episode = Proposal("episode", EntityKind.VideoEpisode, "Pilot",
                new Dictionary<string, string> { [ExternalIdProviders.Tmdb] = "1396:1:1" }, request.PluginId);
            var season = Proposal("season", EntityKind.VideoSeason, "Season 1",
                new Dictionary<string, string> { [ExternalIdProviders.Tmdb] = "1396:1" }, request.PluginId, [episode]);
            var root = Proposal("series", EntityKind.VideoSeries, "Breaking Bad",
                new Dictionary<string, string> {
                    [ExternalIdProviders.Tmdb] = "1396"
                }, request.PluginId, [season]);
            var review = new RequestReviewResponse(
                request.PluginId,
                request.ExternalIdentity,
                EntityKind.VideoSeries,
                RequestMediaKind.Series,
                root,
                RequestProposalRevision.Compute(root),
                [new(root.ProposalId, RequestMediaKind.Series, EntityKind.VideoSeries, request.ExternalIdentity, true)]);
            return Task.FromResult<RequestReviewResponse?>(review);
        }
        private static EntityMetadataProposal Proposal(
            string id,
            EntityKind kind,
            string title,
            IReadOnlyDictionary<string, string> ids,
            string pluginId,
            IReadOnlyList<EntityMetadataProposal>? children = null) => new(
                id,
                pluginId,
                kind,
                1,
                "Exact identity",
                new(title, null, ids, [], [], null, [], new Dictionary<string, string>(),
                    new Dictionary<string, int>(), new Dictionary<string, int>(), null),
                [],
                children ?? [],
                [],
                Relationships: []);
    }
    private sealed class SeriesProviders(params string[] ids) : IIdentifyProviderService {
        public Task<IReadOnlyList<PluginProvider>> ListProvidersAsync(
            string? entityKind,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<PluginProvider>>(ids.Select(id =>
                new PluginProvider(id, id, "1.0.0", true, true, false, [], [], [])).ToArray());
        public Task<IdentifyPluginResponse> IdentifyAsync(
            Guid entityId, string providerId, IdentifyQuery? query,
            IReadOnlyDictionary<string, string>? parentExternalIds, bool hideNsfw,
            CancellationToken cancellationToken, bool cascadeChildren = true,
            IIdentifyCascadeSink? sink = null, bool hydrateRelationships = true) => throw new NotImplementedException();
        public Task<bool> ApplyAsync(
            Guid entityId, EntityMetadataProposal proposal, IReadOnlyCollection<string> selectedFields,
            IReadOnlyDictionary<string, string?>? selectedImages, CancellationToken cancellationToken,
            IIdentifyApplyProgressReporter? progress = null) => throw new NotImplementedException();
    }
    private sealed class Lease : IEntityLifecycleMutationLease {
        public async Task<bool> ExecuteAsync(Guid id, Func<CancellationToken, Task> mutation, CancellationToken token) { await mutation(token); return true; }
    }
}
