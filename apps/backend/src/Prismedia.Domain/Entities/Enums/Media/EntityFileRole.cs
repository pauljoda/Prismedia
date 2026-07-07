namespace Prismedia.Domain.Entities;

/// <summary>
/// Closed set of semantic file roles attached to entities.
/// </summary>
public enum EntityFileRole {
    /// <summary>Original playable or readable source file.</summary>
    [Code("source")]
    Source,

    /// <summary>Small generated thumbnail image.</summary>
    [Code("thumbnail")]
    Thumbnail,

    /// <summary>
    /// Small downscaled variant of the resolved cover, sized for grid cards.
    /// Served alongside the full cover so the frontend can pick a scale-appropriate
    /// image (low-res for small cards, full-res only at large sizes).
    /// </summary>
    [Code("grid-thumbnail")]
    GridThumbnail,

    /// <summary>
    /// Double-density companion of <see cref="GridThumbnail"/> for high-DPI grid
    /// cards. Generated from the same resolved cover so the srcset pair never drifts.
    /// </summary>
    [Code("grid-thumbnail-2x")]
    GridThumbnail2x,

    /// <summary>Primary poster or cover artwork.</summary>
    [Code("poster")]
    Poster,

    /// <summary>Wide background artwork.</summary>
    [Code("backdrop")]
    Backdrop,

    /// <summary>Brand or title-logo artwork.</summary>
    [Code("logo")]
    Logo,

    /// <summary>Short preview clip or representative media file.</summary>
    [Code("preview")]
    Preview,

    /// <summary>Sprite sheet used for timeline previews.</summary>
    [Code("sprite")]
    Sprite,

    /// <summary>Trickplay asset used during seeking.</summary>
    [Code("trickplay")]
    Trickplay,

    /// <summary>Audio waveform image or data asset.</summary>
    [Code("waveform")]
    Waveform,

    /// <summary>Book, gallery, or audio cover image.</summary>
    [Code("cover")]
    Cover,

    /// <summary>HLS manifest or segment asset.</summary>
    [Code("hls")]
    Hls
}
