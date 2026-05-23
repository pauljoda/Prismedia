using EntityImageAsset = Prismedia.Domain.Capabilities.EntityImageAsset;

namespace Prismedia.Contracts.Entities;

/// <summary>API-facing shared artwork capability.</summary>
/// <param name="SupportedKinds">Asset kinds this entity type can expose.</param>
/// <param name="Items">Actual image or generated visual assets attached to this entity.</param>
/// <param name="ThumbnailUrl">Small artwork URL for cards and rows.</param>
/// <param name="CoverUrl">Large artwork URL for detail surfaces.</param>
[CapabilityKind("images")]
public sealed record ImagesCapability(
    IReadOnlyList<string> SupportedKinds,
    IReadOnlyList<EntityImageAsset> Items,
    string? ThumbnailUrl,
    string? CoverUrl) : EntityCapability;
