namespace Prismedia.Domain.Entities;

/// <summary>
/// Associates an entity's canonical <see cref="ExternalIdentity"/> with an optional URL that can
/// open the same item in the external system.
/// </summary>
public sealed record EntityExternalId {
    /// <summary>
    /// Creates an external-id association from legacy provider and value primitives. The values are
    /// validated and normalized by <see cref="ExternalIdentity"/>.
    /// </summary>
    /// <param name="provider">External identity namespace.</param>
    /// <param name="value">Opaque external identity value.</param>
    /// <param name="url">Optional canonical URL for opening the item externally.</param>
    public EntityExternalId(string provider, string value, string? url)
        : this(new ExternalIdentity(provider, value), url) { }

    /// <summary>Creates an external-id association from its canonical identity.</summary>
    /// <param name="identity">Validated external identity.</param>
    /// <param name="url">Optional canonical URL for opening the item externally.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="identity"/> is null.</exception>
    public EntityExternalId(ExternalIdentity identity, string? url = null) {
        ArgumentNullException.ThrowIfNull(identity);
        Identity = identity;
        Url = url;
    }

    /// <summary>Validated identity in the external namespace.</summary>
    public ExternalIdentity Identity { get; }

    /// <summary>Normalized external identity namespace.</summary>
    public string Provider => Identity.Namespace;

    /// <summary>Trimmed, case-preserving external identity value.</summary>
    public string Value => Identity.Value;

    /// <summary>Optional canonical URL for opening the item externally.</summary>
    public string? Url { get; }
}
