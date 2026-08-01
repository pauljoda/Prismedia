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
                    EntityKind.VideoSeries.ToCode(),
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
                    EntityKind.VideoSeason.ToCode(),
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
