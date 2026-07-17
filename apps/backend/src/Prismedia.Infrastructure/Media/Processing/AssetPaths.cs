namespace Prismedia.Infrastructure.Media.Processing;

/// <summary>
/// Canonical cache-layout vocabulary for generated assets: the public URL prefix, the
/// directory segments under the cache root, and the generated file names. Owned here as
/// the single source of truth so <see cref="AssetPathService"/>, cleanup, eviction, and
/// URL-mapping code never retype a segment — a layout change that skips one consumer
/// silently orphans or misses cache files.
/// </summary>
public static class AssetPaths {
    /// <summary>Public URL prefix the API serves the cache root under.</summary>
    public const string AssetsUrlPrefix = "/assets/";

    // ── Cache directory segments ────────────────────────────────────────
    /// <summary>Per-video generated assets (thumb, preview, sprite, subtitles).</summary>
    public const string Videos = "videos";

    /// <summary>Per-image generated assets.</summary>
    public const string Images = "images";

    /// <summary>Trickplay tile roots keyed by entity id and width.</summary>
    public const string Trickplay = "trickplay";

    /// <summary>Intermediate trickplay frame dumps under a video's asset directory.</summary>
    public const string TrickplayFrames = "trickplay-frames";

    /// <summary>Per-audio-track generated assets (waveforms).</summary>
    public const string AudioTracks = "audio-tracks";

    /// <summary>Per-book-page thumbnails.</summary>
    public const string BookPages = "book-pages";

    /// <summary>Per-book cover thumbnails.</summary>
    public const string BookCovers = "book-covers";

    /// <summary>Flat kind-agnostic grid thumbnail variants keyed by entity id.</summary>
    public const string GridThumbs = "grid-thumbs";

    /// <summary>Generated subtitle files under a video's asset directory.</summary>
    public const string Subtitles = "subtitles";

    /// <summary>Adaptive transcode + remux HLS cache root.</summary>
    public const string Hlsv = "hlsv";

    /// <summary>Legacy HLS package cache root.</summary>
    public const string Hls = "hls";

    /// <summary>Legacy secondary HLS package cache root.</summary>
    public const string Hls2 = "hls2";

    // ── Generated file names ────────────────────────────────────────────
    /// <summary>Primary thumbnail file.</summary>
    public const string ThumbnailFile = "thumb.jpg";

    /// <summary>Hover preview clip.</summary>
    public const string PreviewFile = "preview.mp4";

    /// <summary>Scrub sprite sheet.</summary>
    public const string SpriteFile = "sprite.jpg";

    /// <summary>Trickplay WebVTT index.</summary>
    public const string TrickplayVttFile = "trickplay.vtt";

    /// <summary>Audio waveform JSON.</summary>
    public const string WaveformFile = "waveform.json";

    // ── Groupings ───────────────────────────────────────────────────────
    /// <summary>HLS/transcode cache roots eligible for size reporting and eviction.</summary>
    public static readonly string[] TranscodeCacheRoots = [Hlsv, Hls, Hls2];

    /// <summary>Every cache root that holds per-entity generated output, for id-keyed cleanup.</summary>
    public static readonly string[] GeneratedDirectoryRoots = [
        Videos,
        Images,
        Trickplay,
        AudioTracks,
        BookPages,
        BookCovers,
        Hls,
        Hls2,
        Hlsv,
    ];
}
