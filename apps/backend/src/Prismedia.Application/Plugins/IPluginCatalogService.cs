using Prismedia.Contracts.Plugins;

namespace Prismedia.Application.Plugins;

/// <summary>
/// Application-facing catalog for compatible community plugin providers.
/// </summary>
public interface IPluginCatalogService {
    /// <summary>Lists providers discovered from installed and development plugin sources.</summary>
    Task<IReadOnlyList<PluginProvider>> ListProvidersAsync(CancellationToken cancellationToken);

    /// <summary>Installs and enables a provider, or returns null when it cannot be found.</summary>
    Task<PluginProvider?> InstallAsync(string providerId, CancellationToken cancellationToken);

    /// <summary>Removes installed provider state while preserving plugin files.</summary>
    Task<bool> RemoveAsync(string providerId, CancellationToken cancellationToken);

    /// <summary>Stores credential values for a provider.</summary>
    Task<bool> SaveAuthAsync(
        string providerId,
        IReadOnlyDictionary<string, string?> values,
        CancellationToken cancellationToken);
}
