using Prismedia.Contracts.Entities;

namespace Prismedia.Contracts.Media;

/// <summary>
/// API-facing detail shape for an image gallery.
/// </summary>
public sealed record GalleryDetail : EntityDetail {
    /// <summary>Relationship edge metadata for credited people shown on detail pages.</summary>
    public required IReadOnlyList<EntityCreditMetadata> CreditMetadata { get; init; }

    /// <summary>Gallery storage shape.</summary>
    public required string GalleryType { get; init; }

    /// <summary>Selected cover image entity, when one is set.</summary>
    public required Guid? CoverImageId { get; init; }
}
