namespace Prismedia.Domain.Entities;

/// <summary>
/// Provenance of one readable-chapter-to-audiobook-track association. Manual rows are user-curated
/// and always win; auto rows are recomputed by the chapter-mapping job and never overwrite a manual
/// choice.
/// </summary>
public enum BookChapterMappingOrigin {
    /// <summary>Explicitly chosen by a user through the chapter-mapping editor.</summary>
    [Code("manual")]
    Manual,

    /// <summary>Derived by the server's normalized-title matcher and refreshed on scan.</summary>
    [Code("auto")]
    Auto
}
