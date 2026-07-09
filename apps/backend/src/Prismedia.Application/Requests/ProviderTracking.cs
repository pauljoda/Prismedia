using Prismedia.Domain.Entities;

namespace Prismedia.Application.Requests;

/// <summary>
/// Answers which persistent identity namespaces installed metadata plugins can track through lookup-id.
/// Plugin manifest ids are resolved centrally and are not assumed to equal identity namespaces.
/// </summary>
public interface IProviderTrackingCatalog {
    /// <summary>
    /// The subset of <paramref name="identities"/> namespaces handled by an enabled plugin declaring
    /// lookup-id for <paramref name="pluginKindCode"/>. Empty when nothing can track.
    /// </summary>
    Task<IReadOnlyList<string>> TrackableProvidersAsync(
        string pluginKindCode,
        IReadOnlyList<ExternalIdentity> identities,
        CancellationToken cancellationToken);
}
