using Prismedia.Application.Requests;
using Prismedia.Contracts.Requests;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Tests.Requests;

/// <summary>
/// Covers the registry-driven request layer: the search aggregator's kind gating and error capture
/// across the requestable kinds, the detail service's registry routing, and the registry's own shape
/// invariants (containers with committable children, leaves with acquisition kinds).
/// </summary>
public sealed class RequestServicesTests {
    [Fact]
    public async Task SearchWithNoKindFilterQueriesEveryDiscoverableKind() {
        var source = new FakeSearchSource();
        var service = new RequestSearchService(source);

        await service.SearchAsync(Request("query", []), CancellationToken.None);

        // Unit kinds (a season, an episode) exist only inside their parent's flow — never searched.
        Assert.Equal(
            RequestKindRegistry.All.Where(descriptor => descriptor.Discoverable).Select(descriptor => descriptor.Kind).ToArray(),
            source.QueriedKinds.ToArray());
        Assert.DoesNotContain(RequestMediaKind.Season, source.QueriedKinds);
        Assert.DoesNotContain(RequestMediaKind.Episode, source.QueriedKinds);
    }

    [Fact]
    public async Task SearchRefusesAnExplicitlyRequestedUnitKind() {
        var source = new FakeSearchSource();
        var service = new RequestSearchService(source);

        await service.SearchAsync(Request("query", [RequestMediaKind.Season, RequestMediaKind.Episode]), CancellationToken.None);

        Assert.Empty(source.QueriedKinds);
    }

    [Fact]
    public async Task SearchScopedToKindsQueriesOnlyThose() {
        var source = new FakeSearchSource();
        var service = new RequestSearchService(source);

        await service.SearchAsync(Request("query", [RequestMediaKind.Movie, RequestMediaKind.Artist]), CancellationToken.None);

        Assert.Equal([RequestMediaKind.Movie, RequestMediaKind.Artist], source.QueriedKinds.ToArray());
    }

    [Fact]
    public async Task SearchCapturesAFailingKindAsAProviderErrorWithoutThrowing() {
        var source = new FakeSearchSource { FailingKind = RequestMediaKind.Book };
        var service = new RequestSearchService(source);

        var response = await service.SearchAsync(Request("query", [RequestMediaKind.Book, RequestMediaKind.Movie]), CancellationToken.None);

        var error = Assert.Single(response.ProviderErrors);
        Assert.Equal(RequestProviderKind.Plugin, error.Kind);
        Assert.Contains(RequestMediaKind.Book.ToCode(), error.DisplayName);
        Assert.Equal([RequestMediaKind.Movie], response.Results.Select(result => result.Kind).ToArray());
    }

    [Fact]
    public async Task DetailRoutesThroughTheRegistryDescriptor() {
        var source = new FakeDetailSource();
        var service = new RequestDetailService(source);

        await service.GetAsync(RequestProviderKind.Plugin, RequestMediaKind.Artist, "musicbrainz:MB1", null, hideNsfw: true, CancellationToken.None);

        Assert.Equal(RequestMediaKind.Artist, source.LastDescriptor?.Kind);
        Assert.Equal(EntityKind.MusicArtist, source.LastDescriptor?.PluginEntityKind);
        Assert.True(source.LastHideNsfw);
    }

    [Fact]
    public async Task DetailForAnUnregisteredKindReturnsNull() {
        var source = new FakeDetailSource();
        var service = new RequestDetailService(source);

        var detail = await service.GetAsync(RequestProviderKind.Plugin, RequestMediaKind.Plugin, "x:y", null, hideNsfw: false, CancellationToken.None);

        Assert.Null(detail);
        Assert.Null(source.LastDescriptor);
    }

    [Fact]
    public void RegistryShapeInvariantsHold() {
        foreach (var descriptor in RequestKindRegistry.All) {
            // A committable container must fan out into a committable child kind, or a commit could
            // never start an acquisition for it.
            if (descriptor is { IsContainer: true, Committable: true }) {
                var child = RequestKindRegistry.ChildOf(descriptor);
                Assert.NotNull(child);
                Assert.True(child!.Committable, $"{descriptor.Kind} fans out into non-committable {child.Kind}");
                Assert.False(child.IsContainer, $"{descriptor.Kind}'s child {child.Kind} must be a leaf");
            }
        }

        // The registry is the closed set for the flow; kinds must be unique.
        Assert.Equal(RequestKindRegistry.All.Count, RequestKindRegistry.All.Select(d => d.Kind).Distinct().Count());
    }

    private static RequestSearchRequest Request(string query, IReadOnlyList<RequestMediaKind> kinds) =>
        new(query, kinds, [], HideNsfw: false);

    private static RequestSearchResult Result(RequestMediaKind kind, string externalId) =>
        new(Guid.Empty, RequestProviderKind.Plugin, kind, externalId, externalId, null, null, null, null, null,
            null, null, null, null, [], false, null, null, true);

    private sealed class FakeSearchSource : IRequestMetadataSearchSource {
        public List<RequestMediaKind> QueriedKinds { get; } = [];
        public RequestMediaKind? FailingKind { get; set; }

        public Task<IReadOnlyList<RequestSearchResult>> SearchAsync(
            RequestKindDescriptor descriptor, string query, bool hideNsfw, CancellationToken cancellationToken) {
            QueriedKinds.Add(descriptor.Kind);
            if (descriptor.Kind == FailingKind) {
                return Task.FromException<IReadOnlyList<RequestSearchResult>>(new InvalidOperationException("boom"));
            }

            return Task.FromResult<IReadOnlyList<RequestSearchResult>>([Result(descriptor.Kind, $"p:{descriptor.Kind.ToCode()}")]);
        }
    }

    private sealed class FakeDetailSource : IPluginRequestDetailSource {
        public RequestKindDescriptor? LastDescriptor { get; private set; }
        public bool LastHideNsfw { get; private set; }

        public Task<RequestDetailResponse?> GetDetailAsync(
            RequestKindDescriptor descriptor, string externalId, bool hideNsfw, CancellationToken cancellationToken) {
            LastDescriptor = descriptor;
            LastHideNsfw = hideNsfw;
            return Task.FromResult<RequestDetailResponse?>(null);
        }
    }
}
