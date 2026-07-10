using Prismedia.Application.Plugins;
using Prismedia.Contracts.Plugins;

namespace Prismedia.Infrastructure.Plugins;

/// <summary>
/// Holds one stable installed-plugin snapshot for a request or worker scope. Identity routing and
/// provider-link projection share this service, so hydrating an Entity graph does not rediscover
/// plugins and reload provider configuration once per Entity.
/// </summary>
/// <remarks>
/// Remote provider listings remain uncached. Catalog mutations are serialized with snapshot loads
/// and invalidate the snapshot before returning, preserving read-after-write behavior in one scope.
/// </remarks>
/// <param name="inner">Underlying catalog that owns discovery, persistence, and mutation behavior.</param>
public sealed class ScopedPluginCatalogCache(IPluginCatalogService inner) : IPluginCatalogService, IDisposable {
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<PluginProvider>? _installedProviders;

    /// <inheritdoc />
    public Task<IReadOnlyList<PluginProvider>> ListProvidersAsync(CancellationToken cancellationToken) =>
        inner.ListProvidersAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<PluginProvider>> ListInstalledProvidersAsync(
        CancellationToken cancellationToken) {
        var cached = Volatile.Read(ref _installedProviders);
        if (cached is not null) {
            return cached;
        }

        await _gate.WaitAsync(cancellationToken);
        try {
            cached = _installedProviders;
            if (cached is not null) {
                return cached;
            }

            cached = (await inner.ListInstalledProvidersAsync(cancellationToken)).ToArray();
            Volatile.Write(ref _installedProviders, cached);
            return cached;
        } finally {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public Task<PluginProvider?> InstallAsync(
        string providerId,
        CancellationToken cancellationToken) =>
        MutateAsync(() => inner.InstallAsync(providerId, cancellationToken), cancellationToken);

    /// <inheritdoc />
    public Task<PluginProvider?> UpdateAsync(
        string providerId,
        CancellationToken cancellationToken) =>
        MutateAsync(() => inner.UpdateAsync(providerId, cancellationToken), cancellationToken);

    /// <inheritdoc />
    public Task<bool> RemoveAsync(string providerId, CancellationToken cancellationToken) =>
        MutateAsync(() => inner.RemoveAsync(providerId, cancellationToken), cancellationToken);

    /// <inheritdoc />
    public Task<bool> SaveAuthAsync(
        string providerId,
        IReadOnlyDictionary<string, string?> values,
        CancellationToken cancellationToken) =>
        MutateAsync(() => inner.SaveAuthAsync(providerId, values, cancellationToken), cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<StashScraperListing>> ListStashScrapersAsync(
        CancellationToken cancellationToken) =>
        inner.ListStashScrapersAsync(cancellationToken);

    /// <inheritdoc />
    public void Dispose() => _gate.Dispose();

    private async Task<T> MutateAsync<T>(
        Func<Task<T>> mutation,
        CancellationToken cancellationToken) {
        await _gate.WaitAsync(cancellationToken);
        try {
            Volatile.Write(ref _installedProviders, null);
            return await mutation();
        } finally {
            Volatile.Write(ref _installedProviders, null);
            _gate.Release();
        }
    }
}
