namespace Prismedia.Domain.Entities;

/// <summary>
/// Provenance of one readable-chapter-to-audio-chapter association. Manual and ordered rows are
/// person-confirmed and always win; auto rows are recomputed by the chapter-mapping job and never
/// overwrite a confirmed choice. Members are declared confirmed-first, so ordering by origin lets
/// confirmed pairs claim their chapters before automatic ones.
/// </summary>
public enum BookChapterMappingOrigin {
    /// <summary>Explicitly chosen by a user through the chapter-mapping editor.</summary>
    [Code("manual")]
    Manual,

    /// <summary>
    /// Filled in playback order by the editor's "fill in order" step, reviewed pair by pair and saved by
    /// a user. Confirmed evidence like <see cref="Manual"/>, but remembered as filled in order.
    /// </summary>
    [Code("ordered")]
    Ordered,

    /// <summary>Derived by the server's exact-title matcher and refreshed on scan.</summary>
    [Code("auto")]
    Auto
}
