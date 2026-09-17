using Prismedia.Application.Entities;
using Prismedia.Application.Plugins;
using Prismedia.Application.Requests;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Requests;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Tests.Requests;

public sealed partial class RequestCommitServiceTests {
    [Fact]
    public async Task PreparingReviewedSeriesSavesOnlySelectedEpisodesAcrossRegularAndSpecialSeasons() {
        var request = ManagedSeriesReview();
        var writer = new FakeWantedEntityWriter();
        var suppression = new FakeSuppressionStore();
        var router = new SeriesRouter();
        var service = new ReviewedWantedSeriesService(writer, suppression, router, new MovieLease());

        var first = await service.PrepareAsync(request, default);
        writer.ExistingWanted.UnionWith(["series-7", "season-0", "season-1", "episode-special", "episode-pilot"]);
        var replay = await service.PrepareAsync(request, default);

        Assert.Equal(first.SeriesEntityId, replay.SeriesEntityId);
        Assert.Equal(first.Title, replay.Title);
        Assert.True(first.Episodes.SequenceEqual(replay.Episodes));
        Assert.Equal(FakeWantedEntityWriter.EntityIdFor("series-7"), first.SeriesEntityId);
        Assert.Collection(
            first.Episodes,
            special => {
                Assert.Equal(0, special.SeasonNumber);
                Assert.Equal(1, special.EpisodeNumber);
                Assert.Equal(100, special.AbsoluteNumber);
                Assert.Equal(FakeWantedEntityWriter.EntityIdFor("season-0"), special.SeasonEntityId);
            },
            pilot => {
                Assert.Equal(1, pilot.SeasonNumber);
                Assert.Equal(1, pilot.EpisodeNumber);
                Assert.Null(pilot.AbsoluteNumber);
                Assert.Equal(FakeWantedEntityWriter.EntityIdFor("season-1"), pilot.SeasonEntityId);
            });
        Assert.All(first.Episodes, episode => Assert.False(episode.HasFile));
        Assert.Equal(10, writer.ProviderIdentityBindings.Count);
        Assert.Equal(10, writer.DeferredArtworkApplied.Count);
        Assert.All(writer.DeferredArtworkApplied, call =>
            Assert.DoesNotContain(call.Proposal.Children, child => !child.TargetKind.IsRelationship()));
        Assert.Equal(6, router.Calls.Count);
        Assert.Contains("tmdb:series-7", suppression.Cleared);
        Assert.Contains("tvdb:episode-special", suppression.Cleared);
        Assert.Contains("tvdb:episode-pilot", suppression.Cleared);
    }

    [Fact]
    public async Task PreparingReviewedSeriesPreservesOwnedEpisodeMetadataAndReportsPartialOwnership() {
        var request = ManagedSeriesReview();
        var writer = new FakeWantedEntityWriter();
        writer.ExistingWithFile.Add("episode-pilot");

        var result = await new ReviewedWantedSeriesService(
            writer,
            new FakeSuppressionStore(),
            new SeriesRouter(),
            new MovieLease()).PrepareAsync(request, default);

        Assert.False(result.Episodes[0].HasFile);
        Assert.True(result.Episodes[1].HasFile);
        Assert.DoesNotContain(
            writer.DeferredArtworkApplied,
            call => call.EntityId == FakeWantedEntityWriter.EntityIdFor("episode-pilot"));
        Assert.Contains(
            writer.DeferredArtworkApplied,
            call => call.EntityId == FakeWantedEntityWriter.EntityIdFor("episode-special"));
    }

    [Fact]
    public async Task InvalidFiniteSeriesCoordinatesAreRejectedBeforeAnyWantedWrite() {
        var request = ManagedSeriesReview(duplicateCoordinates: true);
        var writer = new FakeWantedEntityWriter();

        var error = await Assert.ThrowsAsync<RequestCommitValidationException>(() =>
            new ReviewedWantedSeriesService(
                writer,
                new FakeSuppressionStore(),
                new SeriesRouter(),
                new MovieLease()).PrepareAsync(request, default));

        Assert.Contains("unique season and episode", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(writer.Ensured);
        Assert.Empty(writer.EnsuredChildren);
    }

    [Fact]
    public async Task MissingExactEpisodeRouteIsRejectedBeforeAnyWantedWrite() {
        var request = ManagedSeriesReview();
        var writer = new FakeWantedEntityWriter();

        await Assert.ThrowsAsync<RequestCommitValidationException>(() =>
            new ReviewedWantedSeriesService(
                writer,
                new FakeSuppressionStore(),
                new SeriesRouter { MissingIdentity = new ExternalIdentity("tvdb", "episode-pilot") },
                new MovieLease()).PrepareAsync(request, default));

        Assert.Empty(writer.Ensured);
        Assert.Empty(writer.EnsuredChildren);
        Assert.Empty(writer.ProviderIdentityBindings);
    }

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

    private static ReviewedRequestCommitRequest ManagedSeriesReview(bool duplicateCoordinates = false) {
        const string pluginId = "fixture-series";
        var seriesIdentity = new ExternalIdentity("tmdb", "series-7");
        var specialsIdentity = new ExternalIdentity("tvdb", "season-0");
        var firstSeasonIdentity = new ExternalIdentity("tvdb", "season-1");
        var specialIdentity = new ExternalIdentity("tvdb", "episode-special");
        var pilotIdentity = new ExternalIdentity("tvdb", "episode-pilot");
        var special = Node(
            "episode:special",
            pluginId,
            EntityKind.VideoEpisode,
            "Special",
            specialIdentity,
            new Dictionary<string, int> {
                [EntityPositionCodes.Season] = 0,
                [EntityPositionCodes.Episode] = 1,
                [EntityPositionCodes.AbsoluteEpisode] = 100
            });
        var pilot = Node(
            "episode:pilot",
            pluginId,
            EntityKind.VideoEpisode,
            "Pilot",
            pilotIdentity,
            new Dictionary<string, int> {
                [EntityPositionCodes.Season] = duplicateCoordinates ? 0 : 1,
                [EntityPositionCodes.Episode] = 1
            });
        var specials = Node(
            "season:0",
            pluginId,
            EntityKind.VideoSeason,
            "Specials",
            specialsIdentity,
            new Dictionary<string, int> { [EntityPositionCodes.Season] = 0 },
            special);
        var firstSeason = Node(
            "season:1",
            pluginId,
            EntityKind.VideoSeason,
            "Season 1",
            firstSeasonIdentity,
            new Dictionary<string, int> { [EntityPositionCodes.Season] = duplicateCoordinates ? 0 : 1 },
            pilot);
        var series = Node(
            "series:7",
            pluginId,
            EntityKind.VideoSeries,
            "Fixture Series",
            seriesIdentity,
            specials,
            firstSeason);
        var review = Review(
            pluginId,
            RequestMediaKind.Series,
            seriesIdentity,
            series,
            [
                Target(series, RequestMediaKind.Series, seriesIdentity),
                Target(specials, RequestMediaKind.Season, specialsIdentity, position: 0),
                Target(special, RequestMediaKind.Episode, specialIdentity, position: 1),
                Target(firstSeason, RequestMediaKind.Season, firstSeasonIdentity, position: duplicateCoordinates ? 0 : 1),
                Target(pilot, RequestMediaKind.Episode, pilotIdentity, position: 1)
            ]);
        return new ReviewedRequestCommitRequest(
            RequestMediaKind.Series,
            pluginId,
            seriesIdentity,
            review.Revision,
            [special.ProposalId, pilot.ProposalId],
            Review: review,
            Proposal: series,
            SelectedFields: [MetadataPatchField.Title.ToCode(), MetadataPatchField.ExternalIds.ToCode()],
            SelectedImages: new Dictionary<string, string?>());
    }

    private sealed class SeriesRouter : IPluginIdentityRouter {
        internal ExternalIdentity? MissingIdentity { get; init; }
        internal List<(string Kind, IReadOnlyList<ExternalIdentity> Identities)> Calls { get; } = [];

        public Task<IReadOnlyList<PluginIdentityRoute>> ResolveAsync(
            string kind,
            IdentifyAction action,
            IReadOnlyList<ExternalIdentity> identities,
            CancellationToken token) {
            Assert.Equal(IdentifyAction.LookupId, action);
            Calls.Add((kind, identities));
            return Task.FromResult<IReadOnlyList<PluginIdentityRoute>>(identities
                .Where(identity => identity != MissingIdentity)
                .Select(identity => new PluginIdentityRoute("fixture-series", identity))
                .ToArray());
        }
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
