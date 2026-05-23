using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Capabilities;

/// <summary>
/// One image or generated visual asset attached to an entity, keyed by its semantic role.
/// </summary>
/// <param name="Kind">Semantic asset kind declared by the entity type.</param>
/// <param name="Path">Path or URL for the asset.</param>
/// <param name="MimeType">Optional MIME type for serving the asset.</param>
public sealed record EntityImageAsset(EntityFileRole Kind, string Path, string? MimeType);
