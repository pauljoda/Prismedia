using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Capabilities;

/// <summary>
/// Mutable file capability for attached source, generated, or cached files.
/// </summary>
public sealed class CapabilityFiles(IEnumerable<CapabilityFiles.Item>? items = null)
    : CollectionCapability<CapabilityFiles.Item>(items) {
    /// <summary>
    /// Describes a file attached to an entity, including original media, thumbnails, generated assets, and cached artifacts.
    /// </summary>
    /// <param name="Role">Semantic role for the file.</param>
    /// <param name="Path">Absolute or app-resolved path to the file.</param>
    /// <param name="MimeType">Optional content type when the file is served over HTTP.</param>
    public sealed record Item(EntityFileRole Role, string Path, string? MimeType);

    /// <summary>Attaches a file in the given role. Multiple files may share a role.</summary>
    /// <param name="role">Semantic role for the file.</param>
    /// <param name="path">Absolute or app-resolved path.</param>
    /// <param name="mimeType">Optional content type.</param>
    public void Attach(EntityFileRole role, string path, string? mimeType = null) =>
        AddItem(new Item(role, path, mimeType));

    /// <summary>Removes every file attached in the given role.</summary>
    /// <param name="role">Role to detach.</param>
    /// <returns>True when at least one file was removed.</returns>
    public bool DetachRole(EntityFileRole role) =>
        RemoveItems(item => item.Role == role) > 0;
}
