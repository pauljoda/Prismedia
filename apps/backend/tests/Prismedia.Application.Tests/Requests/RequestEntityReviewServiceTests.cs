using Prismedia.Application.Entities;
using Prismedia.Application.Plugins;
using Prismedia.Application.Requests;
using Prismedia.Contracts.Plugins;
using Prismedia.Contracts.Requests;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Tests.Requests;

public sealed class RequestEntityReviewServiceTests {
    private static readonly Guid EntityId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task ReviewTriesDeterministicIdentityRoutesUntilOneReturnsAProposal() {
        var firstIdentity = new ExternalIdentity("imdb", "tt:opaque:001");
        var secondIdentity = new ExternalIdentity("tmdb", "series:603");
        var routes = new[] {
            new PluginIdentityRoute("alpha-metadata", firstIdentity),
            new PluginIdentityRoute("zeta-metadata", secondIdentity)
        };
        var identities = new RecordingIdentityStore([
            new EntityExternalId(secondIdentity),
            new EntityExternalId(firstIdentity)
        ]);
        var router = new RecordingIdentityRouter(routes);
        var reviews = new RecordingReviewSource(request =>
            request.PluginId == "zeta-metadata" ? Review(request) : null);
        var service = new RequestEntityReviewService(identities, router, reviews);

        var result = await service.ReviewAsync(
            new RequestEntityReviewRequest(EntityId, RequestMediaKind.Series),
            hideNsfw: true,
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("zeta-metadata", result.PluginId);
        Assert.Equal(secondIdentity, result.ExternalIdentity);
        Assert.Equal(EntityKindRegistry.VideoSeries.Code, router.LastEntityKindCode);
        Assert.Equal(IdentifyAction.LookupId, router.LastAction);
        Assert.Equal([secondIdentity, firstIdentity], router.LastIdentities);
        Assert.Equal(
            [("alpha-metadata", firstIdentity), ("zeta-metadata", secondIdentity)],
            reviews.Requests.Select(request => (request.PluginId, request.ExternalIdentity)).ToArray());
        Assert.All(reviews.HideNsfwValues, Assert.True);
    }

    [Fact]
    public async Task ReviewReturnsNullWithoutCallingPluginsWhenEntityHasNoIdentities() {
        var router = new RecordingIdentityRouter([]);
        var reviews = new RecordingReviewSource(_ => throw new InvalidOperationException("Review should not run."));
        var service = new RequestEntityReviewService(new RecordingIdentityStore([]), router, reviews);

        var result = await service.ReviewAsync(
            new RequestEntityReviewRequest(EntityId, RequestMediaKind.Series),
            hideNsfw: false,
            CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(0, router.CallCount);
        Assert.Empty(reviews.Requests);
    }

    [Fact]
    public async Task ReviewReturnsNullWhenNoPluginCanRouteTheEntityIdentities() {
        var identity = new ExternalIdentity("tmdb", "series:603");
        var router = new RecordingIdentityRouter([]);
        var reviews = new RecordingReviewSource(_ => throw new InvalidOperationException("Review should not run."));
        var service = new RequestEntityReviewService(
            new RecordingIdentityStore([new EntityExternalId(identity)]),
            router,
            reviews);

        var result = await service.ReviewAsync(
            new RequestEntityReviewRequest(EntityId, RequestMediaKind.Series),
            hideNsfw: false,
            CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(1, router.CallCount);
        Assert.Empty(reviews.Requests);
    }

    private static RequestReviewResponse Review(RequestReviewRequest request) {
        var proposal = new EntityMetadataProposal(
            "series-root",
            request.PluginId,
            ProposalKind.VideoSeries,
            1,
            "external-id",
            new EntityMetadataPatch(
                "Series",
                null,
                new Dictionary<string, string> {
                    [request.ExternalIdentity.Namespace] = request.ExternalIdentity.Value
                },
                [],
                [],
                null,
                [],
                new Dictionary<string, string>(),
                new Dictionary<string, int>(),
                new Dictionary<string, int>(),
                null),
            [],
            [],
            [],
            null,
            []);
        return new RequestReviewResponse(
            request.PluginId,
            request.ExternalIdentity,
            EntityKind.VideoSeries,
            request.Kind,
            proposal,
            "revision",
            [new RequestReviewTarget(
                proposal.ProposalId,
                request.Kind,
                EntityKind.VideoSeries,
                request.ExternalIdentity,
                Requestable: true)]);
    }

    private sealed class RecordingIdentityStore(IReadOnlyList<EntityExternalId> identities)
        : IEntityExternalIdentityStore {
        public Task<IReadOnlyList<EntityExternalId>> ListAsync(
            Guid entityId,
            CancellationToken cancellationToken) {
            Assert.Equal(EntityId, entityId);
            return Task.FromResult(identities);
        }

        public Task<ExternalIdentityResolution> ResolveAsync(
            EntityKind kind,
            IReadOnlyCollection<ExternalIdentity> identities,
            Guid? parentEntityId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task WriteAsync(
            Guid entityId,
            IReadOnlyCollection<EntityExternalId> identities,
            ExternalIdentityWriteMode mode,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingIdentityRouter(IReadOnlyList<PluginIdentityRoute> routes)
        : IPluginIdentityRouter {
        public int CallCount { get; private set; }
        public string? LastEntityKindCode { get; private set; }
        public IdentifyAction? LastAction { get; private set; }
        public IReadOnlyList<ExternalIdentity> LastIdentities { get; private set; } = [];

        public Task<IReadOnlyList<PluginIdentityRoute>> ResolveAsync(
            string entityKindCode,
            IdentifyAction action,
            IReadOnlyList<ExternalIdentity> identities,
            CancellationToken cancellationToken) {
            CallCount++;
            LastEntityKindCode = entityKindCode;
            LastAction = action;
            LastIdentities = identities;
            return Task.FromResult(routes);
        }
    }

    private sealed class RecordingReviewSource(
        Func<RequestReviewRequest, RequestReviewResponse?> resolve) : IPluginRequestReviewSource {
        public List<RequestReviewRequest> Requests { get; } = [];
        public List<bool> HideNsfwValues { get; } = [];

        public Task<RequestReviewResponse?> ReviewAsync(
            RequestReviewRequest request,
            bool hideNsfw,
            CancellationToken cancellationToken) {
            Requests.Add(request);
            HideNsfwValues.Add(hideNsfw);
            return Task.FromResult(resolve(request));
        }

        public Task<RequestReviewResponse?> RevalidateAsync(
            RequestReviewRequest request,
            bool hideNsfw,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
