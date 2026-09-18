using Prismedia.Contracts.Integrations;

namespace Prismedia.Application.Integrations;

/// <summary>Reads provider-owned library boundaries independently of holdings and request policy.</summary>
public interface IIntegrationLibraryGateway {
    /// <summary>Lists the complete bounded library catalog for one connected application.</summary>
    Task<ProviderLibraryCatalog> ListLibrariesAsync(
        string pluginId,
        IntegrationConnectionContext connection,
        CancellationToken cancellationToken);
}
