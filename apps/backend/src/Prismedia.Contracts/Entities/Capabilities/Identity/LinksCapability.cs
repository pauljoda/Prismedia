namespace Prismedia.Contracts.Entities;

/// <summary>API-facing user-visible URL associated with an entity.</summary>
/// <param name="Value">Absolute external URL.</param>
/// <param name="Label">Optional label for display, such as a provider or site name.</param>
public sealed record EntityUrl(string Value, string? Label);

/// <summary>API-facing provider-specific identity for an entity.</summary>
/// <param name="Provider">Stable provider code that owns the identifier.</param>
/// <param name="Value">Provider-specific identifier value.</param>
/// <param name="Url">Optional canonical provider URL for opening the entity externally.</param>
public sealed record EntityExternalId(string Provider, string Value, string? Url);

/// <summary>API-facing external link capability.</summary>
/// <param name="Urls">External URLs for the entity.</param>
/// <param name="ExternalIds">Provider identifiers for matching and refresh.</param>
[CapabilityKind("links")]
public sealed record LinksCapability(
    IReadOnlyList<EntityUrl> Urls,
    IReadOnlyList<EntityExternalId> ExternalIds) : EntityCapability;
