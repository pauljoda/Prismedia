namespace Prismedia.Contracts.Entities;

/// <summary>API-facing shared flag capability.</summary>
/// <param name="IsFavorite">Favorite flag when projected.</param>
/// <param name="IsNsfw">NSFW flag when projected.</param>
/// <param name="IsOrganized">Organized/reviewed flag when projected.</param>
[CapabilityKind("flags")]
public sealed record FlagsCapability(bool? IsFavorite, bool? IsNsfw, bool? IsOrganized) : EntityCapability;
