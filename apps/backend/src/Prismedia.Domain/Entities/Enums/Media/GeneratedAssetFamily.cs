namespace Prismedia.Domain.Entities;

/// <summary>
/// Semantic family that owns the conventional generated cache layout for an entity kind.
/// Multiple kinds may share a family when their derived assets are interchangeable in shape.
/// </summary>
public enum GeneratedAssetFamily {
    /// <summary>The kind owns no conventional generated assets.</summary>
    [Code("none")]
    None,

    /// <summary>Video previews, trickplay, grid thumbnails, and adaptive streams.</summary>
    [Code("video")]
    Video,

    /// <summary>Image thumbnails and previews.</summary>
    [Code("image")]
    Image,

    /// <summary>Rendered book-page thumbnails.</summary>
    [Code("book-page")]
    BookPage,

    /// <summary>Audio waveform assets.</summary>
    [Code("audio-track")]
    AudioTrack
}
