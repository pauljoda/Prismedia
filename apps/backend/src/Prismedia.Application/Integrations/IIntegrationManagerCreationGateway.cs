using Prismedia.Contracts.Integrations;

namespace Prismedia.Application.Integrations;

/// <summary>Exact managed-work lookup and creation, separate from subsequent monitoring and search.</summary>
public interface IIntegrationManagerCreationGateway {
    #region Abstract Methods

    /// <summary>Finds a confirmed metadata identity and any current holding without remote side effects.</summary>
    Task<ManagedLookupResult> LookupAsync(string pluginId, IntegrationConnectionContext connection,
        ManagedLookupInput input, CancellationToken token);

    /// <summary>Ensures initial unmonitored holding existence once. Persist ownership and a dispatch fence before calling.</summary>
    Task<EnsureManagedResult> EnsureAsync(string pluginId, IntegrationConnectionContext connection,
        EnsureManagedInput input, CancellationToken token);

    #endregion
}
