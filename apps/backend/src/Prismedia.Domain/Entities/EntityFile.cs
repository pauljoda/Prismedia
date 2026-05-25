namespace Prismedia.Domain.Entities;

/// <summary>
/// Describes a file attached to an entity, including original media, thumbnails, generated assets, and cached artifacts.
/// </summary>
/// <param name="Role">Semantic role for the file.</param>
/// <param name="Path">Absolute or app-resolved path to the file.</param>
/// <param name="MimeType">Optional content type when the file is served over HTTP.</param>
public sealed record EntityFile(EntityFileRole Role, string Path, string? MimeType);
