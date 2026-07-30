using Prismedia.Domain.Entities;

namespace Prismedia.Contracts.Entities;

/// <summary>Configuration and caller permissions for a Collection Entity.</summary>
/// <param name="IsShared">Whether other household users may view the collection.</param>
/// <param name="CanEdit">Whether the current caller owns and may edit the collection.</param>
/// <param name="Mode">Membership evaluation mode.</param>
/// <param name="RuleTreeJson">Normalized rule tree for rule-driven modes.</param>
/// <param name="CoverMode">How collection artwork is selected.</param>
/// <param name="LastRefreshedAt">Most recent dynamic-membership refresh.</param>
[CapabilityKind("collection-configuration")]
public sealed record CollectionConfigurationCapability(
    bool IsShared,
    bool CanEdit,
    CollectionMode Mode,
    string? RuleTreeJson,
    CollectionCoverMode CoverMode,
    DateTimeOffset? LastRefreshedAt) : EntityCapability;
