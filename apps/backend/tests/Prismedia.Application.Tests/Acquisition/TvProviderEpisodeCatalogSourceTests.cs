using Microsoft.Extensions.Logging.Abstractions;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Entities;
using Prismedia.Application.Plugins;
using Prismedia.Application.Requests;
using Prismedia.Contracts.Plugins;
using Prismedia.Contracts.Requests;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Tests.Acquisition;

public sealed class TvProviderEpisodeCatalogSourceTests {
    private const string PluginId = "catalog-test";
    private static readonly Guid SeriesId = Guid.NewGuid();
    private static readonly ExternalIdentity SeriesIdentity = new("catalog-series", "opaque:series/key");

    [Fact]
    public async Task ReadsOnlySixNearbyOrDeclaredSeasonsAndPreservesOpaqueProviderIdentities() {
        var fixture = new Fixture();
        var result = await fixture.Source.ReadAsync(Guid.NewGuid(), 2, [new("Show.S19E01.mkv", 100)], default);

        Assert.Equal(7, fixture.Reviews.Requests.Count);
        Assert.Equal(RequestMediaKind.Series, fixture.Reviews.Requests[0].Kind);
        Assert.Equal(SeriesIdentity, fixture.Reviews.Requests[0].ExternalIdentity);
        Assert.Equal([2, 19, 1, 3, 4, 5], result.Select(season => season.SeasonNumber));
        Assert.All(result, season => {
            Assert.Null(season.SeasonEntityId);
            Assert.Equal(SeasonIdentity(season.SeasonNumber), season.ProviderIdentity);
            var episode = Assert.Single(season.Episodes);
            Assert.Null(episode.EntityId);
            Assert.Equal(1, episode.Episode);
            Assert.Equal(season.SeasonNumber * 10, episode.AbsoluteEpisode);
            Assert.Equal(EpisodeIdentity(season.SeasonNumber), episode.ProviderIdentity);
        });
        Assert.All(fixture.Reviews.Requests, request => Assert.Equal(PluginId, request.PluginId));
        Assert.Equal(EntityKind.VideoSeries.ToCode(), fixture.Router.Kind);
        Assert.Equal(IdentifyAction.LookupId, fixture.Router.Action);
    }

    [Fact]
    public async Task UnavailablePluginDoesNotStartAnyReview() {
        var fixture = new Fixture();
        fixture.Router.Enabled = false;
        Assert.Empty(await fixture.Source.ReadAsync(Guid.NewGuid(), 2, [], default));
        Assert.Empty(fixture.Reviews.Requests);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SeasonEvidenceMustAgreeWithTheExistingCatalogIdentity(bool sameOrdering) {
        var fixture = new Fixture();
        fixture.Targets.SeasonId = Guid.NewGuid();
        fixture.Identities.SeasonIdentity = sameOrdering ? SeasonIdentity(2) : new("different-ordering", "season:2");

        var result = await fixture.Source.ReadAsync(Guid.NewGuid(), 2, [], default);

        Assert.Equal(sameOrdering ? 6 : 0, result.Count);
        Assert.Equal(sameOrdering ? 7 : 1, fixture.Reviews.Requests.Count);
    }

    [Fact]
    public async Task RejectsAReviewForADifferentSeriesIdentity() {
        var fixture = new Fixture();
        fixture.Reviews.Transform = response => response with { ExternalIdentity = new("catalog-series", "different") };
        Assert.Empty(await fixture.Source.ReadAsync(Guid.NewGuid(), 2, [], default));
        Assert.Single(fixture.Reviews.Requests);
    }

    [Fact]
    public async Task RejectsEpisodePositionsFromADifferentParentSeason() {
        var fixture = new Fixture();
        fixture.Reviews.Transform = response => response.Kind != RequestMediaKind.Season ? response : response with {
            Proposal = response.Proposal with { Children = response.Proposal.Children.Select(child => child with {
                Patch = child.Patch with { Positions = new Dictionary<string, int> {
                    [EntityPositionCodes.Season] = 999, [EntityPositionCodes.Episode] = 1
                } }
            }).ToArray() }
        };
        Assert.Empty(await fixture.Source.ReadAsync(Guid.NewGuid(), 2, [], default));
    }

    [Fact]
    public async Task ProviderFailureLeavesLocalImportUsable() {
        var fixture = new Fixture();
        fixture.Reviews.Transform = _ => throw new HttpRequestException("Provider is unavailable.");
        Assert.Empty(await fixture.Source.ReadAsync(Guid.NewGuid(), 2, [], default));
        Assert.Single(fixture.Reviews.Requests);
    }

    [Fact]
    public async Task CallerCancellationIsNotSwallowed() {
        var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Source.ReadAsync(Guid.NewGuid(), 2, [], cancellation.Token));
    }

    private static ExternalIdentity SeasonIdentity(int number) => new("catalog-season", $"opaque:{number}/season");
    private static ExternalIdentity EpisodeIdentity(int number) => new("catalog-episode", $"opaque:{number}/episode");

    private sealed class Fixture {
        public RecordingRouter Router { get; } = new();
        public RecordingReviews Reviews { get; } = new();
        public Targets Targets { get; } = new();
        public Identities Identities { get; } = new();
        public TvProviderEpisodeCatalogSource Source => new(Targets, Identities, Router, Reviews,
            NullLogger<TvProviderEpisodeCatalogSource>.Instance);
    }

    private sealed class RecordingRouter : IPluginIdentityRouter {
        public bool Enabled { get; set; } = true;
        public string? Kind { get; private set; }
        public IdentifyAction? Action { get; private set; }
        public Task<IReadOnlyList<PluginIdentityRoute>> ResolveAsync(string entityKindCode, IdentifyAction action,
            IReadOnlyList<ExternalIdentity> identities, CancellationToken cancellationToken) {
            Kind = entityKindCode;
            Action = action;
            Assert.Equal([SeriesIdentity], identities);
            return Task.FromResult<IReadOnlyList<PluginIdentityRoute>>(Enabled ? [new(PluginId, SeriesIdentity)] : []);
        }
    }

    private sealed class RecordingReviews : IPluginRequestProgressiveReviewSource {
        public Task<string?> GetReviewProviderRevisionAsync(RequestReviewRequest request, bool hideNsfw, CancellationToken cancellationToken) =>
            Task.FromResult<string?>("1.0.0");
        public List<RequestReviewRequest> Requests { get; } = [];
        public Func<RequestReviewResponse, RequestReviewResponse> Transform { get; set; } = response => response;
        public Task<RequestReviewResponse?> StartReviewAsync(RequestReviewRequest request, bool hideNsfw, CancellationToken cancellationToken) {
            Requests.Add(request);
            var number = Enumerable.Range(1, 20).SingleOrDefault(number => SeasonIdentity(number) == request.ExternalIdentity);
            var root = request.Kind == RequestMediaKind.Series
                ? Proposal("series", EntityKind.VideoSeries, SeriesIdentity, 0, null,
                    Enumerable.Range(1, 20).Select(number => Proposal($"season-{number}", EntityKind.VideoSeason, SeasonIdentity(number), number, null, [])).ToArray())
                : Proposal($"season-{number}", EntityKind.VideoSeason, SeasonIdentity(number), number, null,
                    [Proposal($"episode-{number}", EntityKind.VideoEpisode, EpisodeIdentity(number), number, 1, [])]);
            var targets = root.Children.Prepend(root).Select(node => new RequestReviewTarget(node.ProposalId,
                node.TargetKind == EntityKind.VideoSeries ? RequestMediaKind.Series : node.TargetKind == EntityKind.VideoSeason ? RequestMediaKind.Season : RequestMediaKind.Episode,
                node.TargetKind, new(node.Patch.ExternalIds.Single().Key, node.Patch.ExternalIds.Single().Value), true)).ToArray();
            return Task.FromResult<RequestReviewResponse?>(Transform(new(PluginId, request.ExternalIdentity, root.TargetKind, request.Kind, root, "revision", targets)));
        }
        public Task<RequestReviewResponse> EnrichReviewAsync(RequestReviewResponse seed, bool hideNsfw,
            Func<RequestReviewProgressUpdate, CancellationToken, Task> publish, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Acquisition must not expand an entire series.");
    }

    private static EntityMetadataProposal Proposal(string id, EntityKind kind, ExternalIdentity identity, int season, int? episode,
        IReadOnlyList<EntityMetadataProposal> children) {
        var positions = new Dictionary<string, int> { [EntityPositionCodes.Season] = season };
        if (episode is { } number) {
            positions[EntityPositionCodes.Episode] = number;
            positions[EntityPositionCodes.AbsoluteEpisode] = season * 10;
        }
        return new(id, PluginId, kind, 1, null, new("Hidden Garden", null,
            new Dictionary<string, string> { [identity.Namespace] = identity.Value }, [], [], null, [],
            new Dictionary<string, string>(), new Dictionary<string, int>(), positions, null), [], children, []);
    }

    private sealed class Identities : IEntityExternalIdentityStore {
        public ExternalIdentity? SeasonIdentity { get; set; }
        public Task<IReadOnlyList<EntityExternalId>> ListAsync(Guid entityId, CancellationToken cancellationToken) {
            return Task.FromResult<IReadOnlyList<EntityExternalId>>(entityId == SeriesId ? [new(SeriesIdentity)]
                : SeasonIdentity is { } identity ? [new(identity)] : []);
        }
        public Task<ExternalIdentityResolution> ResolveAsync(EntityKind kind, IReadOnlyCollection<ExternalIdentity> identities,
            Guid? parentEntityId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task WriteAsync(Guid entityId, IReadOnlyCollection<EntityExternalId> identities, ExternalIdentityWriteMode mode,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class Targets : IImportTargetIndex {
        public Guid? SeasonId { get; set; }
        public Task<IReadOnlyList<TvSeasonEpisodeCatalog>> GetSeriesEpisodeCatalogAsync(Guid entityId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TvSeasonEpisodeCatalog>>(SeasonId is { } id ? [new(id, 2, [])] : []);
        public Task<Guid?> GetTvSeriesEntityIdAsync(Guid entityId, CancellationToken cancellationToken) => Task.FromResult<Guid?>(SeriesId);
        public Task<TvSeriesDiskLayout?> GetTvLayoutAsync(Guid entityId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<MovieDiskTarget?> GetMovieTargetAsync(Guid entityId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AlbumDiskTarget?> GetAlbumTargetAsync(Guid entityId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
