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
        RequestMediaKind.Movie,
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
        internal ExternalIdentity Identity { get; } = new(ExternalIdProviders.Tmdb, "19");
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

        internal Fixture() {
            IntegrationSupport[] support = [new(PluginCapability.ExternalManager,
                [IntegrationOperation.DiscoverManaged, IntegrationOperation.LookupManaged], [EntityKind.Movie])];
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
            Service = new(new(this, this), this, this, wanted);
        }

        private ManagedCandidate Candidate() => new(EntityKind.Movie, "Metropolis", 1927,
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
            ManagedLookupInput input, CancellationToken token) { LookupCalls++; return Task.FromResult(new ManagedLookupResult(Candidate(), null)); }
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
    private sealed class Lease : IEntityLifecycleMutationLease {
        public async Task<bool> ExecuteAsync(Guid id, Func<CancellationToken, Task> mutation, CancellationToken token) { await mutation(token); return true; }
    }
}
