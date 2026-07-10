using Prismedia.Application.Plugins;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Requests;

/// <summary>
/// Answers which installed metadata provider can track an Entity through lookup-id.
/// Plugin manifest ids are resolved centrally and are not assumed to equal identity namespaces.
/// </summary>
public interface IProviderTrackingCatalog {
    /// <summary>
    /// When <paramref name="providerIdentity"/> is present, validates that exact plugin and identity
    /// route and returns its stable plugin id. For an unbound legacy Entity, returns the subset of
    /// <paramref name="identities"/> namespaces handled by an enabled plugin declaring lookup-id for
    /// <paramref name="pluginKindCode"/>. Empty when nothing can track.
    /// </summary>
    Task<IReadOnlyList<string>> TrackableProvidersAsync(
        string pluginKindCode,
        IReadOnlyList<ExternalIdentity> identities,
        PluginIdentityRoute? providerIdentity,
        CancellationToken cancellationToken);
}
