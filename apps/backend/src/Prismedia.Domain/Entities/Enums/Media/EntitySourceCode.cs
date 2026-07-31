namespace Prismedia.Domain.Entities;

/// <summary>
/// Stable provenance keys stored on Entity source rows. These are internal persistence vocabulary;
/// they do not cross the HTTP contract and therefore do not need frontend code generation.
/// </summary>
public enum EntitySourceCode {
    /// <summary>Directory that establishes a structural container or movie's scan provenance.</summary>
    [Code("folder")]
    Folder
}
