using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Entities;
using Prismedia.Application.Plugins;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Media;
using Prismedia.Infrastructure.Entities;
using Prismedia.Infrastructure.Entities.Mappers;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;
using Prismedia.Infrastructure.Plugins;

namespace Prismedia.Infrastructure.Tests;

public sealed class ProviderIdentityHydrationCachingTests {
    [Fact]
    public async Task RecursiveHydrationUsesOneInstalledPluginSnapshotForRoutingAndProviderUrls() {
        await using var db = CreateContext();
        var seriesId = Guid.NewGuid();
        var seasonOneId = Guid.NewGuid();
        var seasonTwoId = Guid.NewGuid();
        SeedEntity(db, seriesId, EntityKind.VideoSeries, "Series");
        SeedEntity(db, seasonOneId, EntityKind.VideoSeason, "Season 1", seriesId, 1);
        SeedEntity(db, seasonTwoId, EntityKind.VideoSeason, "Season 2", seriesId, 2);
        AddExternalIdentity(db, seriesId, "tmdb", "82728");
        AddExternalIdentity(db, seasonOneId, "tmdbseason", "82728:1");
        AddExternalIdentity(db, seasonTwoId, "tmdbseason", "82728:2");
        await db.SaveChangesAsync();

        var providerIdentities = new EfEntityProviderIdentityStore(db, TimeProvider.System);
        await providerIdentities.SetAsync(
            seasonOneId,
            "tmdb",
            new ExternalIdentity("tmdbseason", "82728:1"),
            CancellationToken.None);
        await providerIdentities.SetAsync(
            seasonTwoId,
            "tmdb",
            new ExternalIdentity("tmdbseason", "82728:2"),
            CancellationToken.None);
        await db.SaveChangesAsync();

        var innerCatalog = new RecordingPluginCatalog([TmdbProvider()]);
        var catalog = new ScopedPluginCatalogCache(innerCatalog);
        var repository = new EfEntityRepository(
            db,
            TestUserContext.Admin(),
            EntityMappers.Kinds(db),
            EntityMappers.Capabilities(db, TestUserContext.Admin()),
            new EfEntityExternalIdentityStore(db, TimeProvider.System),
            providerIdentities,
            new PluginIdentityRouter(catalog),
            new PluginIdentityUrlResolver(catalog));

        var series = await repository.RequireAsync<VideoSeries>(seriesId, CancellationToken.None);

        Assert.Equal(1, innerCatalog.InstalledListCallCount);
        Assert.Equal("https://www.themoviedb.org/tv/82728", series.ProviderIdentity?.Url);
        Assert.Collection(
            series.ChildrenOf<VideoSeason>().OrderBy(season => season.SortOrder),
            season => Assert.Equal(
                "https://www.themoviedb.org/tv/82728/season/1",
                season.ProviderIdentity?.Url),
            season => Assert.Equal(
                "https://www.themoviedb.org/tv/82728/season/2",
                season.ProviderIdentity?.Url));
    }

    [Fact]
    public async Task CatalogMutationInvalidatesTheInstalledPluginSnapshot() {
        var innerCatalog = new RecordingPluginCatalog([TmdbProvider()]);
        var catalog = new ScopedPluginCatalogCache(innerCatalog);

        var first = await catalog.ListInstalledProvidersAsync(CancellationToken.None);
        var second = await catalog.ListInstalledProvidersAsync(CancellationToken.None);
        var removed = await catalog.RemoveAsync("tmdb", CancellationToken.None);
        var afterMutation = await catalog.ListInstalledProvidersAsync(CancellationToken.None);

        Assert.Same(first, second);
        Assert.True(removed);
        Assert.Empty(afterMutation);
        Assert.Equal(2, innerCatalog.InstalledListCallCount);
    }

    private static PluginProvider TmdbProvider() =>
        new(
            "tmdb",
            "TMDB",
            "1.0.0",
            Installed: true,
            Enabled: true,
            IsNsfw: false,
            Supports:
            [
                new PluginEntitySupport(
                    EntityKindRegistry.VideoSeries.Code,
                    [IdentifyAction.LookupId.ToCode()],
                    ["tmdb"],
                    IdentityUrls:
                    [
                        new PluginIdentityUrlFormat(
                            "tmdb",
                            "{id}",
                            "https://www.themoviedb.org/tv/{id}")
                    ]),
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
                    ])
            ],
            Auth: [],
            MissingAuthKeys: []);

    private static PrismediaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static void SeedEntity(
        PrismediaDbContext db,
        Guid id,
        EntityKind kind,
        string title,
        Guid? parentId = null,
        int? sortOrder = null) =>
        db.Entities.Add(new EntityRow {
            Id = id,
            KindCode = kind.ToCode(),
            Title = title,
            ParentEntityId = parentId,
            SortOrder = sortOrder,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });

    private static void AddExternalIdentity(
        PrismediaDbContext db,
        Guid entityId,
        string provider,
        string value) =>
        db.EntityExternalIds.Add(new EntityExternalIdRow {
            Id = Guid.NewGuid(),
            EntityId = entityId,
            Provider = provider,
            Value = value,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });

    private sealed class RecordingPluginCatalog(
        IReadOnlyList<PluginProvider> installedProviders) : IPluginCatalogService {
        private IReadOnlyList<PluginProvider> _installedProviders = installedProviders;

        public int InstalledListCallCount { get; private set; }

        public Task<IReadOnlyList<PluginProvider>> ListProvidersAsync(CancellationToken cancellationToken) =>
            Task.FromResult(_installedProviders);

        public Task<IReadOnlyList<PluginProvider>> ListInstalledProvidersAsync(
            CancellationToken cancellationToken) {
            InstalledListCallCount++;
            return Task.FromResult(_installedProviders);
        }

        public Task<PluginProvider?> InstallAsync(
            string providerId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PluginProvider?> UpdateAsync(
            string providerId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> RemoveAsync(string providerId, CancellationToken cancellationToken) {
            var remaining = _installedProviders
                .Where(provider => !provider.Id.Equals(providerId, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var removed = remaining.Length != _installedProviders.Count;
            _installedProviders = remaining;
            return Task.FromResult(removed);
        }

        public Task<bool> SaveAuthAsync(
            string providerId,
            IReadOnlyDictionary<string, string?> values,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<StashScraperListing>> ListStashScrapersAsync(
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
