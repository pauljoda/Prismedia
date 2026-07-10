using Prismedia.Application.Plugins;
using Prismedia.Application.Requests;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Plugins;
using Prismedia.Infrastructure.Requests;

namespace Prismedia.Infrastructure.Tests;

public sealed class PluginIdentityRouterTests {
    [Fact]
    public async Task RoutesExternalNamespaceIndependentlyFromPluginId() {
        var router = Router(Provider(
            "cinema-metadata",
            enabled: true,
            new PluginEntitySupport(
                EntityKindRegistry.Video.Code,
                [IdentifyAction.LookupId.ToCode()],
                ["tmdb"])));

        var routes = await router.ResolveAsync(
            EntityKindRegistry.Video.Code,
            IdentifyAction.LookupId,
            [new ExternalIdentity("tmdb", "123")],
            CancellationToken.None);

        var route = Assert.Single(routes);
        Assert.Equal("cinema-metadata", route.PluginId);
        Assert.Equal(new ExternalIdentity("tmdb", "123"), route.Identity);
    }

    [Fact]
    public async Task OnePluginCanRouteMultipleIdentityNamespaces() {
        var router = Router(Provider(
            "cinema-metadata",
            enabled: true,
            new PluginEntitySupport(
                EntityKindRegistry.Video.Code,
                [IdentifyAction.LookupId.ToCode()],
                ["tmdb", "imdb"])));

        var routes = await router.ResolveAsync(
            EntityKindRegistry.Video.Code,
            IdentifyAction.LookupId,
            [new ExternalIdentity("imdb", "tt123"), new ExternalIdentity("tmdb", "123")],
            CancellationToken.None);

        Assert.Equal(
            [("cinema-metadata", "imdb", "tt123"), ("cinema-metadata", "tmdb", "123")],
            routes.Select(route => (route.PluginId, route.Identity.Namespace, route.Identity.Value)).ToArray());
    }

    [Fact]
    public async Task MonitorTrackingReturnsTheSingleStablePluginIdForALegacyEntity() {
        var router = Router(Provider(
            "cinema-metadata",
            enabled: true,
            new PluginEntitySupport(
                EntityKindRegistry.VideoSeries.Code,
                [IdentifyAction.LookupId.ToCode()],
                ["tmdb"])));
        var tracking = new PluginProviderTrackingCatalog(router);

        var providers = await tracking.TrackableProvidersAsync(
            EntityKindRegistry.VideoSeries.Code,
            [new ExternalIdentity("tmdb", "123")],
            providerIdentity: null,
            CancellationToken.None);

        Assert.Equal(["cinema-metadata"], providers);
    }

    [Fact]
    public async Task MonitorTrackingRejectsAnAmbiguousLegacyEntityWithoutAStablePluginBinding() {
        var identity = new ExternalIdentity("tmdb", "123");
        var tracking = new PluginProviderTrackingCatalog(Router(
            Provider(
                "alpha-provider",
                enabled: true,
                new PluginEntitySupport(
                    EntityKindRegistry.VideoSeries.Code,
                    [IdentifyAction.LookupId.ToCode()],
                    [identity.Namespace])),
            Provider(
                "zeta-provider",
                enabled: true,
                new PluginEntitySupport(
                    EntityKindRegistry.VideoSeries.Code,
                    [IdentifyAction.LookupId.ToCode()],
                    [identity.Namespace]))));

        var providers = await tracking.TrackableProvidersAsync(
            EntityKindRegistry.VideoSeries.Code,
            [identity],
            providerIdentity: null,
            CancellationToken.None);

        Assert.Empty(providers);
    }

    [Fact]
    public async Task MonitorTrackingAcceptsOnlyTheAuthoritativeRouteWhenPluginsShareANamespace() {
        var identity = new ExternalIdentity("tmdb", "123");
        var router = Router(
            Provider(
                "alpha-provider",
                enabled: true,
                new PluginEntitySupport(
                    EntityKindRegistry.VideoSeries.Code,
                    [IdentifyAction.LookupId.ToCode()],
                    [identity.Namespace])),
            Provider(
                "zeta-provider",
                enabled: true,
                new PluginEntitySupport(
                    EntityKindRegistry.VideoSeries.Code,
                    [IdentifyAction.LookupId.ToCode()],
                    [identity.Namespace])));
        var tracking = new PluginProviderTrackingCatalog(router);

        var providers = await tracking.TrackableProvidersAsync(
            EntityKindRegistry.VideoSeries.Code,
            [identity],
            new PluginIdentityRoute("zeta-provider", identity),
            CancellationToken.None);

        Assert.Equal(["zeta-provider"], providers);
    }

    [Fact]
    public async Task MonitorTrackingDoesNotSubstituteAPluginThatSharesTheBoundNamespace() {
        var identity = new ExternalIdentity("tmdb", "123");
        var tracking = new PluginProviderTrackingCatalog(Router(Provider(
            "available-provider",
            enabled: true,
            new PluginEntitySupport(
                EntityKindRegistry.VideoSeries.Code,
                [IdentifyAction.LookupId.ToCode()],
                [identity.Namespace]))));

        var unavailableBinding = await tracking.TrackableProvidersAsync(
            EntityKindRegistry.VideoSeries.Code,
            [identity],
            new PluginIdentityRoute("removed-provider", identity),
            CancellationToken.None);

        Assert.Empty(unavailableBinding);
    }

    [Fact]
    public async Task BatchMonitorTrackingResolvesOncePerEntityKindAndKeepsBindingsExact() {
        var tmdbOne = new ExternalIdentity("tmdb", "1");
        var tmdbTwo = new ExternalIdentity("tmdb", "2");
        var counting = new CountingIdentityRouter(Router(Provider(
            "cinema-metadata",
            enabled: true,
            new PluginEntitySupport(
                EntityKindRegistry.VideoSeries.Code,
                [IdentifyAction.LookupId.ToCode()],
                ["tmdb"]))));
        var tracking = new PluginProviderTrackingCatalog(counting);
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();

        var result = await tracking.TrackableProvidersBatchAsync(
            [
                new ProviderTrackingQuery(
                    firstId,
                    EntityKindRegistry.VideoSeries.Code,
                    [tmdbOne],
                    new PluginIdentityRoute("cinema-metadata", tmdbOne)),
                new ProviderTrackingQuery(
                    secondId,
                    EntityKindRegistry.VideoSeries.Code,
                    [tmdbTwo],
                    ProviderIdentity: null)
            ],
            CancellationToken.None);

        Assert.Equal(1, counting.CallCount);
        Assert.Equal(["cinema-metadata"], result[firstId]);
        Assert.Equal(["cinema-metadata"], result[secondId]);
    }

    [Fact]
    public async Task FiltersByEnabledStateEntityKindAndIdentifyAction() {
        var router = Router(
            Provider(
                "disabled",
                enabled: false,
                new PluginEntitySupport(
                    EntityKindRegistry.Video.Code,
                    [IdentifyAction.LookupId.ToCode()],
                    ["tmdb"])),
            Provider(
                "wrong-kind",
                enabled: true,
                new PluginEntitySupport(
                    EntityKindRegistry.Book.Code,
                    [IdentifyAction.LookupId.ToCode()],
                    ["tmdb"])),
            Provider(
                "search-only",
                enabled: true,
                new PluginEntitySupport(
                    EntityKindRegistry.Video.Code,
                    [IdentifyAction.Search.ToCode()],
                    ["tmdb"],
                    new PluginSearchDefinition(
                    [
                        new PluginSearchField("title", "Title", PluginSearchFieldType.Text, true)
                    ]))));
        var identity = new ExternalIdentity("tmdb", "123");

        var lookupRoutes = await router.ResolveAsync(
            EntityKindRegistry.Video.Code,
            IdentifyAction.LookupId,
            [identity],
            CancellationToken.None);
        var searchRoutes = await router.ResolveAsync(
            EntityKindRegistry.Video.Code,
            IdentifyAction.Search,
            [identity],
            CancellationToken.None);

        Assert.Empty(lookupRoutes);
        Assert.Equal("search-only", Assert.Single(searchRoutes).PluginId);
    }

    [Fact]
    public async Task SharedNamespaceAmbiguityReturnsEveryRouteInStableOrder() {
        var providers = new[] {
            Provider(
                "zeta-provider",
                enabled: true,
                new PluginEntitySupport(
                    EntityKindRegistry.Video.Code,
                    [IdentifyAction.LookupId.ToCode()],
                    ["tmdb"])),
            Provider(
                "alpha-provider",
                enabled: true,
                new PluginEntitySupport(
                    EntityKindRegistry.Video.Code,
                    [IdentifyAction.LookupId.ToCode()],
                    ["tmdb"]))
        };
        var identity = new ExternalIdentity("tmdb", "123");

        var forward = await Router(providers).ResolveAsync(
            EntityKindRegistry.Video.Code,
            IdentifyAction.LookupId,
            [identity],
            CancellationToken.None);
        var reverse = await Router(providers.Reverse().ToArray()).ResolveAsync(
            EntityKindRegistry.Video.Code,
            IdentifyAction.LookupId,
            [identity],
            CancellationToken.None);

        Assert.Equal(["alpha-provider", "zeta-provider"], forward.Select(route => route.PluginId).ToArray());
        Assert.Equal(
            forward.Select(route => (route.PluginId, route.Identity)).ToArray(),
            reverse.Select(route => (route.PluginId, route.Identity)).ToArray());
    }

    private static PluginIdentityRouter Router(params PluginProvider[] providers) =>
        new(new FakePluginCatalog(providers));

    private static PluginProvider Provider(string id, bool enabled, params PluginEntitySupport[] supports) =>
        new(
            id,
            id,
            "2.0.0",
            Installed: true,
            Enabled: enabled,
            IsNsfw: false,
            supports,
            Auth: [],
            MissingAuthKeys: []);

    private sealed class FakePluginCatalog(IReadOnlyList<PluginProvider> providers) : IPluginCatalogService {
        public Task<IReadOnlyList<PluginProvider>> ListProvidersAsync(CancellationToken cancellationToken) =>
            Task.FromResult(providers);

        public Task<IReadOnlyList<PluginProvider>> ListInstalledProvidersAsync(CancellationToken cancellationToken) =>
            Task.FromResult(providers);

        public Task<PluginProvider?> InstallAsync(string providerId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PluginProvider?> UpdateAsync(string providerId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> RemoveAsync(string providerId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> SaveAuthAsync(
            string providerId,
            IReadOnlyDictionary<string, string?> values,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<StashScraperListing>> ListStashScrapersAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class CountingIdentityRouter(IPluginIdentityRouter inner) : IPluginIdentityRouter {
        public int CallCount { get; private set; }

        public Task<IReadOnlyList<PluginIdentityRoute>> ResolveAsync(
            string entityKindCode,
            IdentifyAction action,
            IReadOnlyList<ExternalIdentity> identities,
            CancellationToken cancellationToken) {
            CallCount++;
            return inner.ResolveAsync(entityKindCode, action, identities, cancellationToken);
        }
    }
}
