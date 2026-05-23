using EntityUrl = Prismedia.Domain.Capabilities.CapabilityLinks.Url;
using EntityExternalId = Prismedia.Domain.Capabilities.CapabilityLinks.ExternalId;

namespace Prismedia.Contracts.Entities;

/// <summary>API-facing external link capability.</summary>
/// <param name="Urls">External URLs for the entity.</param>
/// <param name="ExternalIds">Provider identifiers for matching and refresh.</param>
[CapabilityKind("links")]
public sealed record LinksCapability(
    IReadOnlyList<EntityUrl> Urls,
    IReadOnlyList<EntityExternalId> ExternalIds) : EntityCapability;
