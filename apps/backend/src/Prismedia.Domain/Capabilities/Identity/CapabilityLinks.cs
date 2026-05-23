namespace Prismedia.Domain.Capabilities;

/// <summary>
/// Mutable link capability for entities that support external references and provider identities.
/// </summary>
public sealed class CapabilityLinks : EntityCapability {
    private readonly List<Url> _urls = [];
    private readonly List<ExternalId> _externalIds = [];

    /// <summary>
    /// Creates the link capability.
    /// </summary>
    /// <param name="urls">Initial user-visible URLs.</param>
    /// <param name="externalIds">Initial provider identities.</param>
    public CapabilityLinks(IEnumerable<Url>? urls = null, IEnumerable<ExternalId>? externalIds = null) {
        if (urls is not null) {
            _urls.AddRange(urls);
        }

        if (externalIds is not null) {
            _externalIds.AddRange(externalIds);
        }
    }

    /// <summary>
    /// A user-visible URL associated with an entity.
    /// </summary>
    /// <param name="Value">Absolute external URL.</param>
    /// <param name="Label">Optional label for display, such as a provider or site name.</param>
    public sealed record Url(string Value, string? Label);

    /// <summary>
    /// A provider-specific identity for an entity, such as TMDB, AniList, Stash, or a future provider.
    /// </summary>
    /// <param name="Provider">Stable provider code that owns the identifier.</param>
    /// <param name="Value">Provider-specific identifier value.</param>
    /// <param name="Url">Optional canonical provider URL for opening the entity externally.</param>
    public sealed record ExternalId(string Provider, string Value, string? Url);

    /// <summary>User-visible URLs in insertion order.</summary>
    public IReadOnlyList<Url> Urls => _urls;

    /// <summary>Provider identities in insertion order.</summary>
    public IReadOnlyList<ExternalId> ExternalIds => _externalIds;

    /// <summary>Adds a user-visible URL.</summary>
    public void AddUrl(string value, string? label = null) => _urls.Add(new Url(value, label));

    /// <summary>
    /// Sets a provider identity, replacing any existing identity for the same provider.
    /// </summary>
    public void SetExternalId(string provider, string value, string? url = null) {
        _externalIds.RemoveAll(id => string.Equals(id.Provider, provider, StringComparison.Ordinal));
        _externalIds.Add(new ExternalId(provider, value, url));
    }
}
