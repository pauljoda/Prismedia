using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Integrations;

/// <summary>Typed source discovery and offer-resolution ports, distinct from metadata identity search and transfer execution.</summary>
public interface IIntegrationDiscoveryGateway {
    /// <summary>Searches or browses an authorized connection. Does not submit downloads or modify remote holdings.</summary>
    Task<CatalogPage> DiscoverAsync(string pluginId, IntegrationOperation operation, IntegrationConnectionContext connection,
        IntegrationDiscoveryInput input, CancellationToken cancellationToken);
    /// <summary>Resolves one selected full-content offer into server-only delivery instructions.</summary>
    Task<ResolvedSourceOffer> ResolveAsync(string pluginId, IntegrationConnectionContext connection,
        ResolveSourceOfferInput input, CancellationToken cancellationToken);
}

/// <summary>Protects source cursors and selections from tampering and cross-connection reuse.</summary>
public interface IDiscoveryTokenProtector {
    /// <summary>Issues an expiring item-selection token scoped to the connection.</summary>
    string ProtectSelection(Guid connectionId, SourceSelection selection);
    /// <summary>Reads an unexpired selection, or reports an invalid selection without exposing payload content.</summary>
    SourceSelection ReadSelection(Guid connectionId, string token);
    /// <summary>Issues an expiring continuation token bound to the connection, kind, query, and container.</summary>
    string ProtectCursor(Guid connectionId, BrowseConnectionRequest scope, string cursor);
    /// <summary>Reads a cursor only in the exact discovery context that produced it.</summary>
    string ReadCursor(Guid connectionId, BrowseConnectionRequest scope, string token);
}
