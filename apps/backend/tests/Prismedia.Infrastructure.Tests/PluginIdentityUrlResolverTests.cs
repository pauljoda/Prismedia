using Prismedia.Application.Plugins;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Plugins;

namespace Prismedia.Infrastructure.Tests;

public sealed class PluginIdentityUrlResolverTests {
    [Fact]
    public async Task ResolvesCompositeIdentityThroughTheExactEnabledPluginRoute() {
        var resolver = Resolver(Provider(
            "tmdb",
            enabled: true,
            new PluginEntitySupport(
                EntityKindRegistry.VideoSeason.Code,
                [IdentifyAction.LookupId.ToCode()],
                ["tmdbseason"],
                IdentityUrls:
                [
                    new PluginIdentityUrlFormat(
                        "tmdbseason",
                        "{seriesId}:{seasonNumber}",
                        "https://www.themoviedb.org/tv/{seriesId}/season/{seasonNumber}")
                ])));

        var url = await resolver.ResolveAsync(
            EntityKindRegistry.VideoSeason.Code,
            new PluginIdentityRoute("TMDB", new ExternalIdentity("tmdbseason", "82728:2")),
            CancellationToken.None);

        Assert.Equal("https://www.themoviedb.org/tv/82728/season/2", url);
    }

    [Fact]
    public async Task ExactEntityKindUrlFormatWinsOverMovieVideoCompatibilityFallback() {
        var resolver = Resolver(Provider(
            "tmdb",
            enabled: true,
            new PluginEntitySupport(
                EntityKindRegistry.Movie.Code,
                [IdentifyAction.LookupId.ToCode()],
                ["tmdb"],
                IdentityUrls:
                [
                    new PluginIdentityUrlFormat(
                        "tmdb",
                        "{id}",
                        "https://www.themoviedb.org/movie/{id}")
                ]),
            new PluginEntitySupport(
                EntityKindRegistry.Video.Code,
                [IdentifyAction.LookupId.ToCode()],
                ["tmdb"],
                IdentityUrls:
                [
                    new PluginIdentityUrlFormat(
                        "tmdb",
                        "{id}",
                        "https://video-fallback.example/items/{id}")
                ])));

        var url = await resolver.ResolveAsync(
            EntityKindRegistry.Movie.Code,
            new PluginIdentityRoute("tmdb", new ExternalIdentity("tmdb", "603")),
            CancellationToken.None);

        Assert.Equal("https://www.themoviedb.org/movie/603", url);
    }

    [Fact]
    public async Task MovieUrlFallsBackToCompatibleVideoFormatWhenNoExactKindIsDeclared() {
        var resolver = Resolver(Provider(
            "legacy-video",
            enabled: true,
            new PluginEntitySupport(
                EntityKindRegistry.Video.Code,
                [IdentifyAction.LookupId.ToCode()],
                ["legacy"],
                IdentityUrls:
                [
                    new PluginIdentityUrlFormat(
                        "legacy",
                        "{id}",
                        "https://legacy.example/videos/{id}")
                ])));

        var url = await resolver.ResolveAsync(
            EntityKindRegistry.Movie.Code,
            new PluginIdentityRoute("legacy-video", new ExternalIdentity("legacy", "42")),
            CancellationToken.None);

        Assert.Equal("https://legacy.example/videos/42", url);
    }

    [Fact]
    public async Task EscapesCapturedTokensAndPreservesOpaqueIdentityCase() {
        var resolver = Resolver(Provider(
            "openlibrary",
            enabled: true,
            new PluginEntitySupport(
                EntityKindRegistry.Book.Code,
                [IdentifyAction.LookupId.ToCode()],
                ["openlibrary"],
                IdentityUrls:
                [
                    new PluginIdentityUrlFormat(
                        "openlibrary",
                        "series:{name}",
                        "https://openlibrary.org/search?q={name}")
                ])));

        var url = await resolver.ResolveAsync(
            EntityKindRegistry.Book.Code,
            new PluginIdentityRoute(
                "openlibrary",
                new ExternalIdentity("openlibrary", "series:Bluey & Friends/US")),
            CancellationToken.None);

        Assert.Equal("https://openlibrary.org/search?q=Bluey%20%26%20Friends%2FUS", url);
    }

    [Fact]
    public async Task ReturnsNullUnlessProviderKindNamespaceAndWholePatternMatch() {
        var format = new PluginIdentityUrlFormat(
            "tmdbseason",
            "{seriesId}:{seasonNumber}",
            "https://www.themoviedb.org/tv/{seriesId}/season/{seasonNumber}");
        var resolver = Resolver(
            Provider(
                "disabled",
                enabled: false,
                new PluginEntitySupport(
                    EntityKindRegistry.VideoSeason.Code,
                    [IdentifyAction.LookupId.ToCode()],
                    ["tmdbseason"],
                    IdentityUrls: [format])),
            Provider(
                "tmdb",
                enabled: true,
                new PluginEntitySupport(
                    EntityKindRegistry.VideoSeason.Code,
                    [IdentifyAction.LookupId.ToCode()],
                    ["tmdbseason"],
                    IdentityUrls: [format])));

        Assert.Null(await resolver.ResolveAsync(
            EntityKindRegistry.VideoSeason.Code,
            new PluginIdentityRoute("disabled", new ExternalIdentity("tmdbseason", "82728:2")),
            CancellationToken.None));
        Assert.Null(await resolver.ResolveAsync(
            EntityKindRegistry.Video.Code,
            new PluginIdentityRoute("tmdb", new ExternalIdentity("tmdbseason", "82728:2")),
            CancellationToken.None));
        Assert.Null(await resolver.ResolveAsync(
            EntityKindRegistry.VideoSeason.Code,
            new PluginIdentityRoute("tmdb", new ExternalIdentity("tmdbseason", "82728")),
            CancellationToken.None));
        Assert.Null(await resolver.ResolveAsync(
            EntityKindRegistry.VideoSeason.Code,
            new PluginIdentityRoute("missing", new ExternalIdentity("tmdbseason", "82728:2")),
            CancellationToken.None));
    }

    [Fact]
    public async Task FailsClosedWhenAnUnvalidatedCatalogProvidesAnUnsafeTemplate() {
        var resolver = Resolver(Provider(
            "unsafe",
            enabled: true,
            new PluginEntitySupport(
                EntityKindRegistry.Video.Code,
                [IdentifyAction.LookupId.ToCode()],
                ["unsafe"],
                IdentityUrls:
                [
                    new PluginIdentityUrlFormat(
                        "unsafe",
                        "{id}",
                        "https://user@example.test/watch/{id}")
                ])));

        var url = await resolver.ResolveAsync(
            EntityKindRegistry.Video.Code,
            new PluginIdentityRoute("unsafe", new ExternalIdentity("unsafe", "one")),
            CancellationToken.None);

        Assert.Null(url);
    }

    [Fact]
    public async Task FailsClosedWhenUrlTemplateDropsCapturedValueToken() {
        var resolver = Resolver(Provider(
            "tmdb",
            enabled: true,
            new PluginEntitySupport(
                EntityKindRegistry.VideoSeason.Code,
                [IdentifyAction.LookupId.ToCode()],
                ["tmdbseason"],
                IdentityUrls:
                [
                    new PluginIdentityUrlFormat(
                        "tmdbseason",
                        "{seriesId}:{seasonNumber}",
                        "https://www.themoviedb.org/tv/{seriesId}")
                ])));

        var url = await resolver.ResolveAsync(
            EntityKindRegistry.VideoSeason.Code,
            new PluginIdentityRoute("tmdb", new ExternalIdentity("tmdbseason", "82728:2")),
            CancellationToken.None);

        Assert.Null(url);
    }

    private static PluginIdentityUrlResolver Resolver(params PluginProvider[] providers) =>
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
}
