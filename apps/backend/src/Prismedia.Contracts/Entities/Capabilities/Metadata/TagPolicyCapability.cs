namespace Prismedia.Contracts.Entities;

/// <summary>Automatic-tagging policy owned by a Tag Entity.</summary>
/// <param name="IgnoreAutoTag">Whether automatic metadata application must ignore the tag.</param>
[CapabilityKind("tag-policy")]
public sealed record TagPolicyCapability(bool IgnoreAutoTag) : EntityCapability;
