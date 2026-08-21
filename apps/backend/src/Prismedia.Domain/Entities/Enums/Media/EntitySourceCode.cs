namespace Prismedia.Domain.Entities;

/// <summary>
/// Stable provenance keys stored on Entity source rows. These are internal persistence vocabulary;
/// they do not cross the HTTP contract and therefore do not need frontend code generation.
/// </summary>
public enum EntitySourceCode {
    /// <summary>Directory that establishes a structural container or movie's scan provenance.</summary>
    [Code("folder")]
    Folder,

    /// <summary>
    /// Original directory retained when Prismedia creates a managed file representation from
    /// loose source files. This is provenance only: physical-file deletion must never treat the
    /// directory as an Entity-owned payload.
    /// </summary>
    [Code("generated-from-folder")]
    GeneratedFromFolder
}
