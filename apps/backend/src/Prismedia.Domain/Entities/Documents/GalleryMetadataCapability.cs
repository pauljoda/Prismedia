using Prismedia.Domain.Entities;

namespace Prismedia.Contracts.Entities;

/// <summary>Gallery-only storage and organization metadata.</summary>
/// <param name="GalleryType">Gallery organization type.</param>
[CapabilityKind("gallery-metadata")]
public sealed record GalleryMetadataCapability(GalleryType GalleryType) : EntityCapability;
