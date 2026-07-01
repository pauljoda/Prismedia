namespace Prismedia.Contracts.Entities;

/// <summary>API-facing shared flag capability.</summary>
/// <param name="IsFavorite">Favorite flag when projected.</param>
/// <param name="IsNsfw">NSFW flag when projected.</param>
/// <param name="IsOrganized">Organized/reviewed flag when projected.</param>
/// <param name="IsWanted">Wanted-placeholder flag (request-created entity with no file yet) when projected.</param>
[CapabilityKind("flags")]
public sealed record FlagsCapability(bool? IsFavorite, bool? IsNsfw, bool? IsOrganized, bool? IsWanted = null) : EntityCapability;
