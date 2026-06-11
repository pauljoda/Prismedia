namespace Prismedia.Domain.Entities;

/// <summary>
/// Hover-preview behavior vocabulary for entity thumbnails. The read service emits
/// <see cref="None"/> or <see cref="Sprite"/> on the wire; <see cref="ImageSequence"/> and
/// <see cref="Trickplay"/> are client-derived hover modes that share the same closed set so
/// the frontend's hover discriminators reference one generated vocabulary.
/// </summary>
public enum ThumbnailHoverKind {
    /// <summary>No hover preview is available.</summary>
    [Code("none")]
    None,

    /// <summary>Scrub a VTT-mapped sprite sheet or image playlist.</summary>
    [Code("sprite")]
    Sprite,

    /// <summary>Cycle a small set of still frames (client-derived).</summary>
    [Code("image-sequence")]
    ImageSequence,

    /// <summary>Scrub trickplay stills (client-derived).</summary>
    [Code("trickplay")]
    Trickplay
}
