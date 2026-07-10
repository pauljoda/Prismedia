namespace Prismedia.Contracts.Entities;

/// <summary>
/// Exact plugin route selected as the Entity's authoritative metadata and monitoring identity.
/// </summary>
/// <param name="PluginId">Stable installed plugin manifest id.</param>
/// <param name="IdentityNamespace">Normalized external identity namespace handled by the plugin.</param>
/// <param name="IdentityValue">Opaque, case-preserving persistent identity value.</param>
/// <param name="Url">Canonical provider page when the plugin declares how to build it.</param>
[CapabilityKind("provider-identity")]
public sealed record ProviderIdentityCapability(
    string PluginId,
    string IdentityNamespace,
    string IdentityValue,
    string? Url) : EntityCapability;
