using Prismedia.Contracts.Integrations;

namespace Prismedia.Application.Integrations;

/// <summary>Reads connected holdings and external management choices through independently versioned plugins.</summary>
public interface IIntegrationManagerGateway {
    /// <summary>Lists existing holdings only; never adds or searches for releases.</summary>
    Task<ManagedLibraryPage> SearchLibraryAsync(string pluginId, IntegrationConnectionContext connection, ManagedLibraryQuery input, CancellationToken cancellationToken);
    /// <summary>Reconciles exact remote file associations while checking pinned metadata identities.</summary>
    Task<ManagedItemSnapshot> GetLibraryItemAsync(string pluginId, IntegrationConnectionContext connection, ManagedItemInput input, CancellationToken cancellationToken);
    /// <summary>Returns the external application's existing profile and root-folder choices.</summary>
    Task<ManagerOptions> GetOptionsAsync(string pluginId, IntegrationConnectionContext connection, ManagerOptionsInput input, CancellationToken cancellationToken);
}
