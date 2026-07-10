using Prismedia.Application.Plugins;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Requests;

/// <summary>One Entity's plugin tracking lookup in a bounded monitoring-state batch.</summary>
public sealed record ProviderTrackingQuery(
    Guid EntityId,
    string PluginKindCode,
    IReadOnlyList<ExternalIdentity> Identities,
    PluginIdentityRoute? ProviderIdentity);

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

    /// <summary>
    /// Resolves a bounded set of Entity routes in one application call. Plugin protocols currently expose
    /// lookup-id per Entity, so the default fans out inside the adapter rather than at the HTTP/service layer.
    /// </summary>
    async Task<IReadOnlyDictionary<Guid, IReadOnlyList<string>>> TrackableProvidersBatchAsync(
        IReadOnlyList<ProviderTrackingQuery> queries,
        CancellationToken cancellationToken) {
        var result = new Dictionary<Guid, IReadOnlyList<string>>();
        foreach (var query in queries) {
            result[query.EntityId] = await TrackableProvidersAsync(
                query.PluginKindCode,
                query.Identities,
                query.ProviderIdentity,
                cancellationToken);
        }

        return result;
    }
}
