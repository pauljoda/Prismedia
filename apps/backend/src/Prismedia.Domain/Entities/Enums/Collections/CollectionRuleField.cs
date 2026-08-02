namespace Prismedia.Domain.Entities;

/// <summary>
/// Closed set of fields available to dynamic collection rules. The codes are the persisted rule
/// tree vocabulary and deliberately preserve the existing wire spelling.
/// </summary>
public enum CollectionRuleField {
    /// <summary>Entity title.</summary>
    [Code("title")]
    Title,

    /// <summary>Entity rating value.</summary>
    [Code("rating")]
    Rating,

    /// <summary>Release or air date.</summary>
    [Code("date")]
    Date,

    /// <summary>Whether the entity has been organized.</summary>
    [Code("organized")]
    Organized,

    /// <summary>Whether the entity is marked NSFW.</summary>
    [Code("isNsfw")]
    IsNsfw,

    /// <summary>Tag relationship.</summary>
    [Code("tags")]
    Tags,

    /// <summary>Performer relationship.</summary>
    [Code("performers")]
    Performers,

    /// <summary>Studio relationship.</summary>
    [Code("studio")]
    Studio,

    /// <summary>Library root membership.</summary>
    [Code("libraryRootId")]
    LibraryRootId,

    /// <summary>Source file size.</summary>
    [Code("fileSize")]
    FileSize,

    /// <summary>Media duration.</summary>
    [Code("duration")]
    Duration,

    /// <summary>Pixel height.</summary>
    [Code("height")]
    Height,

    /// <summary>Pixel width.</summary>
    [Code("width")]
    Width,

    /// <summary>Encoded media codec.</summary>
    [Code("codec")]
    Codec,

    /// <summary>Audio bit rate.</summary>
    [Code("bitRate")]
    BitRate,

    /// <summary>Legacy snake-case audio bit-rate field.</summary>
    [Code("bit_rate")]
    BitRateLegacy,

    /// <summary>Audio channel count.</summary>
    [Code("channels")]
    Channels,

    /// <summary>Audio sample rate.</summary>
    [Code("sampleRate")]
    SampleRate,

    /// <summary>Legacy snake-case audio sample-rate field.</summary>
    [Code("sample_rate")]
    SampleRateLegacy,

    /// <summary>User consumption access count.</summary>
    [Code("accessCount")]
    AccessCount,

    /// <summary>User playback skip count.</summary>
    [Code("skipCount")]
    SkipCount,

    /// <summary>Named video resolution tier.</summary>
    [Code("resolution")]
    Resolution,

    /// <summary>Structural parent video series.</summary>
    [Code("videoSeriesId")]
    VideoSeriesId,

    /// <summary>Gallery classification.</summary>
    [Code("galleryType")]
    GalleryType,

    /// <summary>Number of child images.</summary>
    [Code("imageCount")]
    ImageCount,

    /// <summary>Media container or image format.</summary>
    [Code("format")]
    Format,

    /// <summary>Entity creation timestamp.</summary>
    [Code("createdAt")]
    CreatedAt,

    /// <summary>Whether the entity is interactive.</summary>
    [Code("interactive")]
    Interactive
}
