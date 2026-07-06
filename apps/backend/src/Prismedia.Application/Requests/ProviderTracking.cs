namespace Prismedia.Application.Requests;

/// <summary>
/// Answers whether installed metadata plugins can track provider identities — re-resolve an item by its
/// provider id (the plugin lookup-id action). Standing monitors ride on this: a watch is only meaningful
/// when some enabled plugin can notice the tracked id again on the next discovery sweep.
/// </summary>
public interface IProviderTrackingCatalog {
    /// <summary>
    /// The subset of <paramref name="providerIds"/> (distinct provider keys) whose provider is an enabled
    /// plugin declaring the lookup-id action for <paramref name="pluginKindCode"/> (a plugin entity-kind
    /// wire code, see <see cref="RequestKindDescriptor.PluginKindCode"/>). Empty when nothing can track.
    /// </summary>
    Task<IReadOnlyList<string>> TrackableProvidersAsync(string pluginKindCode, IReadOnlyList<ProviderRef> providerIds, CancellationToken cancellationToken);
}
