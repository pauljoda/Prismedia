namespace Prismedia.Contracts.Entities;

/// <summary>API-facing file attached to an entity.</summary>
/// <param name="Role">Stable semantic role code for the file.</param>
/// <param name="Path">Absolute, app-resolved, or served path to the file.</param>
/// <param name="MimeType">Optional content type when the file is served over HTTP.</param>
public sealed record EntityFile(string Role, string Path, string? MimeType);

/// <summary>API-facing file capability.</summary>
/// <param name="Items">Files attached to the entity.</param>
[CapabilityKind("files")]
public sealed record FilesCapability(IReadOnlyList<EntityFile> Items) : EntityCapability;
