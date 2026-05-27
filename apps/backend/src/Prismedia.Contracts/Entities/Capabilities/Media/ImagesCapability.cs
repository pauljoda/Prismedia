namespace Prismedia.Contracts.Entities;

/// <summary>API-facing image or generated visual asset attached to an entity.</summary>
/// <param name="Kind">Stable semantic asset kind code.</param>
/// <param name="Path">Path or URL for the asset.</param>
/// <param name="MimeType">Optional MIME type for serving the asset.</param>
public sealed record EntityImageAsset(string Kind, string Path, string? MimeType);

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
