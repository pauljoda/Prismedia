namespace Prismedia.Contracts.Entities;

/// <summary>API-facing image or generated visual asset attached to an entity.</summary>
/// <param name="Kind">Stable semantic asset kind code.</param>
/// <param name="Path">Path or URL for the asset.</param>
/// <param name="MimeType">Optional MIME type for serving the asset.</param>
public sealed record EntityImageAsset(string Kind, string Path, string? MimeType);

/// <summary>API-facing shared artwork capability.</summary>
/// <param name="SupportedKinds">Asset kinds this entity type can expose.</param>
/// <param name="Items">Actual image or generated visual assets attached to this entity.</param>
/// <param name="ThumbnailUrl">Small artwork URL for cards and rows (the 480w grid variant when generated).</param>
/// <param name="Thumbnail2xUrl">Double-density companion of <see cref="ThumbnailUrl"/> for high-DPI displays.</param>
/// <param name="CoverUrl">Large artwork URL for detail surfaces.</param>
[CapabilityKind("images")]
public sealed record ImagesCapability(
    IReadOnlyList<string> SupportedKinds,
    IReadOnlyList<EntityImageAsset> Items,
    string? ThumbnailUrl,
    string? Thumbnail2xUrl,
    string? CoverUrl) : EntityCapability;
