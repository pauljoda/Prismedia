namespace Prismedia.Domain.Entities;

/// <summary>Authority that supplied the currently accepted descriptive value.</summary>
public enum MetadataValueOrigin {
    /// <summary>The existing value predates provenance tracking or was supplied outside metadata application.</summary>
    [Code("unknown")] Unknown,
    /// <summary>A user explicitly edited or cleared the value.</summary>
    [Code("user")] User,
    /// <summary>An accepted provider proposal supplied the value.</summary>
    [Code("provider")] Provider
}
