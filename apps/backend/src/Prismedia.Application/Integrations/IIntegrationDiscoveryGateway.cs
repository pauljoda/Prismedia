using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Integrations;

/// <summary>Typed source discovery and offer-resolution ports, distinct from metadata identity search and transfer execution.</summary>
public interface IIntegrationDiscoveryGateway {
    #region Abstract Methods

    /// <summary>Searches or browses an authorized connection. Does not submit downloads or modify remote holdings.</summary>
    Task<CatalogPage> DiscoverAsync(string pluginId, IntegrationOperation operation, IntegrationConnectionContext connection,
        IntegrationDiscoveryInput input, CancellationToken cancellationToken);

    /// <summary>Resolves one selected full-content offer into server-only delivery instructions.</summary>
    Task<ResolvedSourceOffer> ResolveAsync(string pluginId, IntegrationConnectionContext connection,
        ResolveSourceOfferInput input, CancellationToken cancellationToken);

    #endregion
}
