using Prismedia.Contracts.Plugins;

namespace Prismedia.Application.Plugins;

/// <summary>
/// Application-facing catalog for compatible community plugin providers.
/// </summary>
public interface IPluginCatalogService {
    /// <summary>Lists providers discovered from installed and development plugin sources.</summary>
    Task<IReadOnlyList<PluginProvider>> ListProvidersAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Lists locally installed/dev-discovered providers only, without consulting the remote community
    /// index. This is the hot read path — identify, monitor eligibility, and request lookups only ever
    /// act on installed providers, so they must never pay (or depend on) a network round-trip.
    /// </summary>
    Task<IReadOnlyList<PluginProvider>> ListInstalledProvidersAsync(CancellationToken cancellationToken);

    /// <summary>Installs and enables a provider, or returns null when it cannot be found.</summary>
    Task<PluginProvider?> InstallAsync(string providerId, CancellationToken cancellationToken);

    /// <summary>Updates an installed provider to the newest compatible remote artifact.</summary>
    Task<PluginProvider?> UpdateAsync(string providerId, CancellationToken cancellationToken);

    /// <summary>Removes installed provider state while preserving plugin files.</summary>
    Task<bool> RemoveAsync(string providerId, CancellationToken cancellationToken);

    /// <summary>Stores credential values for a provider.</summary>
    Task<bool> SaveAuthAsync(
        string providerId,
        IReadOnlyDictionary<string, string?> values,
        CancellationToken cancellationToken);

    /// <summary>Lists Stash community scrapers available for install from the remote index.</summary>
    Task<IReadOnlyList<StashScraperListing>> ListStashScrapersAsync(CancellationToken cancellationToken);
}
