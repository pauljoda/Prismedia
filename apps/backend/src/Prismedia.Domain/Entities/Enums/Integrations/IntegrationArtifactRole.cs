namespace Prismedia.Domain.Entities;

/// <summary>The semantic role of a remote output, independent of its storage name or eventual entity file role.</summary>
public enum IntegrationArtifactRole {
    /// <summary>The requested publication, media file, or gallery page.</summary>
    [Code("content")] Content,
    /// <summary>Artwork accompanying requested content.</summary>
    [Code("cover")] Cover,
    /// <summary>Descriptive metadata accompanying requested content.</summary>
    [Code("sidecar")] Sidecar
}
