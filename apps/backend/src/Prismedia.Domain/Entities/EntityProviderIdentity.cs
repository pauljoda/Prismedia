namespace Prismedia.Domain.Entities;

/// <summary>
/// The exact installed plugin route selected as an Entity's authoritative persistent identity for
/// metadata refresh and monitoring. The identity stays valid when the plugin is unavailable; URL is
/// presentation data resolved from the plugin contract when possible.
/// </summary>
public sealed record EntityProviderIdentity(
    string PluginId,
    ExternalIdentity Identity,
    string? Url = null);
