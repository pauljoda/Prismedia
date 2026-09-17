using Prismedia.Application.Entities;
using Prismedia.Application.Plugins;
using Prismedia.Application.Requests;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Requests;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Tests.Requests;

public sealed partial class RequestCommitServiceTests {
    [Fact]
    public async Task PreparingReviewedMovieSavesSelectedMetadataWithoutStartingAcquisitionOrMonitoring() {
        var request = ManagedMovieReview();
        var writer = new FakeWantedEntityWriter(); var suppression = new FakeSuppressionStore(); var router = new MovieRouter();
        var service = new ReviewedWantedMovieService(writer, suppression, router, new MovieLease());
        var first = await service.PrepareAsync(request, default);
        writer.ExistingWanted.Add(request.RootExternalIdentity.Value);
        var replay = await service.PrepareAsync(request, default);
        Assert.Equal(first.EntityId, replay.EntityId); Assert.False(first.HasFile);
        Assert.All(writer.Ensured, call => { Assert.Equal(EntityKind.Movie, call.Kind); Assert.False(call.MatchTitleKindWide); });
        Assert.Equal(2, writer.DeferredArtworkApplied.Count);
        Assert.All(writer.ProviderIdentityBindings, binding => Assert.Equal(request.PluginId, binding.Route.PluginId));
        Assert.Contains($"{request.RootExternalIdentity.Namespace}:{request.RootExternalIdentity.Value}", suppression.Cleared);
        Assert.Equal(2, router.Calls);
    }

    [Fact]
    public async Task PreparingAnOwnedMovieReturnsItsExistingIdentityWithoutReplacingMetadata() {
        var request = ManagedMovieReview(); var writer = new FakeWantedEntityWriter(); writer.ExistingWithFile.Add("19");
        var result = await new ReviewedWantedMovieService(writer, new FakeSuppressionStore(), new MovieRouter(), new MovieLease()).PrepareAsync(request, default);
        Assert.True(result.HasFile); Assert.Empty(writer.DeferredArtworkApplied); Assert.Empty(writer.ProviderIdentityBindings);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task InvalidOrExpandedMoviePreparationHasNoWrites(int scenario) {
        var request = ManagedMovieReview(); var writer = new FakeWantedEntityWriter();
        request = scenario switch {
            0 => request with { SelectedProposalIds = [] },
            1 => request with { Kind = RequestMediaKind.Series },
            2 => request with { Proposal = request.Proposal! with { Patch = request.Proposal.Patch with { Title = "Unreviewed replacement" } } },
            3 => request with { RootExternalIdentity = new ExternalIdentity(ExternalIdProviders.Imdb, "tt0017136") },
            4 => request with { Proposal = request.Proposal! with { Patch = request.Proposal.Patch with { PositionEntries = [new(EntityPositionCodes.Episode, 1, "1.5")] } } },
            _ => request with { Proposal = request.Proposal! with { Patch = request.Proposal.Patch with { AlternativeTitles = ["Unreviewed alias"] } } }
        };
        await Assert.ThrowsAsync<RequestCommitValidationException>(() => new ReviewedWantedMovieService(writer, new FakeSuppressionStore(), new MovieRouter(), new MovieLease()).PrepareAsync(request, default));
        Assert.Empty(writer.Ensured); Assert.Empty(writer.DeferredArtworkApplied);
    }

    [Fact]
    public async Task DisabledExactMetadataRouteCannotPrepareAPlaceholder() {
        var writer = new FakeWantedEntityWriter();
        await Assert.ThrowsAsync<RequestCommitValidationException>(() => new ReviewedWantedMovieService(writer, new FakeSuppressionStore(), new MovieRouter { Enabled = false }, new MovieLease()).PrepareAsync(ManagedMovieReview(), default));
        Assert.Empty(writer.Ensured);
    }

    private static ReviewedRequestCommitRequest ManagedMovieReview() {
        var identity = new ExternalIdentity(ExternalIdProviders.Tmdb, "19");
        var proposal = Node("movie:19", "fixture-movies", EntityKind.Movie, "Metropolis", identity);
        var review = Review(proposal.Provider, RequestMediaKind.Movie, identity, proposal, [Target(proposal, RequestMediaKind.Movie, identity)]);
        return new(RequestMediaKind.Movie, proposal.Provider, identity, review.Revision, [proposal.ProposalId],
            Review: review, Proposal: proposal, SelectedFields: [MetadataPatchField.Title.ToCode(), MetadataPatchField.ExternalIds.ToCode()]);
    }
    private sealed class MovieRouter : IPluginIdentityRouter {
        internal bool Enabled = true; internal int Calls;
        public Task<IReadOnlyList<PluginIdentityRoute>> ResolveAsync(string kind, IdentifyAction action, IReadOnlyList<ExternalIdentity> identities, CancellationToken token) {
            Calls++; Assert.Equal(EntityKind.Movie.ToCode(), kind); Assert.Equal(IdentifyAction.LookupId, action);
            return Task.FromResult<IReadOnlyList<PluginIdentityRoute>>(Enabled ? [new("fixture-movies", identities.Single())] : []);
        }
    }
    private sealed class MovieLease : IEntityLifecycleMutationLease {
        public async Task<bool> ExecuteAsync(Guid entityId, Func<CancellationToken, Task> mutation, CancellationToken token) { await mutation(token); return true; }
    }
}
